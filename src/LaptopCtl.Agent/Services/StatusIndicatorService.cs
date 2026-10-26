using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LaptopCtl.Agent.Services;

/// <summary>
/// Shows a visible indicator in the system notification area title
/// and writes a status file so the device owner can see the agent is running.
/// This fulfills the requirement that the agent must not be hidden.
/// </summary>
public sealed class StatusIndicatorService : BackgroundService
{
    private readonly ILogger<StatusIndicatorService> _log;
    private static readonly string StatusFile =
        Path.Combine(AgentPaths.BaseDirectory, "status.txt");

    public StatusIndicatorService(ILogger<StatusIndicatorService> log) => _log = log;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await WriteStatus("RUNNING", stoppingToken);
        _log.LogInformation(
            "LaptopCtl Agent is running. Status file: {Path}. " +
            "To stop: stop the 'LaptopCtlAgent' Windows service or kill the process. " +
            "To uninstall: run the uninstall PowerShell script.",
            StatusFile);

        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await WriteStatus("RUNNING", stoppingToken);
            }
        }
        finally
        {
            await WriteStatus("STOPPED", CancellationToken.None);
        }
    }

    private async Task WriteStatus(string status, CancellationToken ct)
    {
        try
        {
            var content = $"LaptopCtl Agent\nStatus: {status}\nTimestamp: {DateTimeOffset.UtcNow:O}\nVersion: {AgentVersion.Current}\n";
            await File.WriteAllTextAsync(StatusFile, content, ct);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Could not write status file");
        }
    }
}
