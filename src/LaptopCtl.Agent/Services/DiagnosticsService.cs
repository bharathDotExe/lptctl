using LaptopCtl.Shared.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace LaptopCtl.Agent.Services;

/// <summary>Gathers local system diagnostics. All operations are read-only.</summary>
public sealed class DiagnosticsService
{
    private readonly ILogger<DiagnosticsService> _log;

    public DiagnosticsService(ILogger<DiagnosticsService> log) => _log = log;

    // ─── System Info ──────────────────────────────────────────────────────────

    public Task<SystemInfo> GetSystemInfoAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            string? cpuModel = null;
            long? totalRam = null;

            try
            {
                cpuModel = GetCpuModelFromRegistry();
                totalRam = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Could not read CPU/RAM details");
            }

            var info = new SystemInfo
            {
                ComputerName       = Environment.MachineName,
                WindowsVersion     = RuntimeInformation.OSDescription,
                OsArchitecture     = RuntimeInformation.OSArchitecture.ToString(),
                AgentVersion       = AgentVersion.Current,
                CpuModel           = cpuModel,
                TotalMemoryBytes   = totalRam,
                LastConnection     = DateTimeOffset.UtcNow
            };

            return Task.FromResult(info);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to retrieve system info");
            throw;
        }
    }

    private static string? GetCpuModelFromRegistry()
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
            @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        return key?.GetValue("ProcessorNameString") as string;
    }

    // ─── Processes ────────────────────────────────────────────────────────────

    public Task<List<ProcessEntry>> GetProcessesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var results = new List<ProcessEntry>();

        foreach (var proc in Process.GetProcesses())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                results.Add(new ProcessEntry
                {
                    ProcessId       = proc.Id,
                    Name            = proc.ProcessName,
                    Description     = TryGetDescription(proc),
                    WorkingSetBytes = proc.WorkingSet64,
                    StartTime       = TryGetStartTime(proc)
                });
            }
            catch (Exception ex)
            {
                _log.LogTrace(ex, "Skipping inaccessible process {Pid}", proc.Id);
            }
            finally
            {
                proc.Dispose();
            }
        }

        return Task.FromResult(results.OrderBy(p => p.Name).ToList());
    }

    private static string? TryGetDescription(Process proc)
    {
        try { return proc.MainModule?.FileVersionInfo.FileDescription; }
        catch { return null; }
    }

    private static DateTimeOffset? TryGetStartTime(Process proc)
    {
        try { return proc.StartTime; }
        catch { return null; }
    }

    // ─── Disks ────────────────────────────────────────────────────────────────

    public Task<List<LaptopCtl.Shared.Models.DriveInfo>> GetDrivesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var drives = new List<LaptopCtl.Shared.Models.DriveInfo>();

        foreach (var d in System.IO.DriveInfo.GetDrives())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!d.IsReady) continue;
                drives.Add(new LaptopCtl.Shared.Models.DriveInfo
                {
                    Name           = d.Name,
                    VolumeLabel    = d.VolumeLabel,
                    DriveFormat    = d.DriveFormat,
                    TotalBytes     = d.TotalSize,
                    AvailableBytes = d.AvailableFreeSpace
                });
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Skipping drive {Drive}", d.Name);
            }
        }

        return Task.FromResult(drives);
    }

    // ─── Network ─────────────────────────────────────────────────────────────

    public Task<List<NetworkInterfaceInfo>> GetNetworkInterfacesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var result = new List<NetworkInterfaceInfo>();

        foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var addresses = iface.GetIPProperties().UnicastAddresses
                    .Select(a => a.Address.ToString())
                    .ToList();

                result.Add(new NetworkInterfaceInfo
                {
                    Name        = iface.Name,
                    Description = iface.Description,
                    Status      = iface.OperationalStatus.ToString(),
                    Addresses   = addresses
                });
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Skipping interface {Name}", iface.Name);
            }
        }

        return Task.FromResult(result);
    }
}
