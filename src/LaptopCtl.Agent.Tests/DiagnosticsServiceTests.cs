using LaptopCtl.Agent.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LaptopCtl.Agent.Tests;

public class DiagnosticsServiceTests
{
    private readonly DiagnosticsService _service;

    public DiagnosticsServiceTests()
    {
        _service = new DiagnosticsService(NullLogger<DiagnosticsService>.Instance);
    }

    [Fact]
    public async Task GetDrivesAsync_ReturnsReadyDrives()
    {
        var drives = await _service.GetDrivesAsync(CancellationToken.None);

        Assert.NotNull(drives);
        Assert.NotEmpty(drives);

        var cDrive = drives.FirstOrDefault(d => d.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase));
        if (cDrive is not null)
        {
            Assert.True(cDrive.TotalBytes > 0);
            Assert.True(cDrive.AvailableBytes >= 0);
        }
    }

    [Fact]
    public async Task GetProcessesAsync_ReturnsProcessList()
    {
        var procs = await _service.GetProcessesAsync(CancellationToken.None);

        Assert.NotNull(procs);
        Assert.NotEmpty(procs);

        // Current process should be in the list
        var currentPid = Environment.ProcessId;
        Assert.Contains(procs, p => p.ProcessId == currentPid);
    }

    [Fact]
    public async Task GetNetworkInterfacesAsync_ReturnsAdapters()
    {
        var ifaces = await _service.GetNetworkInterfacesAsync(CancellationToken.None);

        Assert.NotNull(ifaces);
        Assert.NotEmpty(ifaces);
    }

    [Fact]
    public async Task GetSystemInfoAsync_ReturnsValidHardwareSpecs()
    {
        var sysInfo = await _service.GetSystemInfoAsync(CancellationToken.None);

        Assert.NotNull(sysInfo);
        Assert.False(string.IsNullOrWhiteSpace(sysInfo.ComputerName));
        Assert.False(string.IsNullOrWhiteSpace(sysInfo.WindowsVersion));
        Assert.False(string.IsNullOrWhiteSpace(sysInfo.AgentVersion));
    }
}
