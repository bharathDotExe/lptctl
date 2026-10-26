using LaptopCtl.Shared.Models;
using Spectre.Console;

namespace LaptopCtl.Controller.Commands;

public static class SessionHandler
{
    public static async Task<int> ListAsync(
        string backend, CredentialStore creds, ApiClient client, CancellationToken ct)
    {
        if (!DeviceHandler.RequireLogin(creds)) return 1;

        try
        {
            var sessions = await AnsiConsole.Status()
                .StartAsync("Fetching active sessions...", async _ =>
                    await client.ListSessionsAsync(backend, ct));

            if (sessions.Count == 0)
            {
                AnsiConsole.MarkupLine("[grey]No active sessions.[/]");
                return 0;
            }

            var table = new Table()
                .Border(TableBorder.Rounded)
                .Title("[bold]Active Remote Sessions[/]")
                .AddColumn("Session ID")
                .AddColumn("Device ID")
                .AddColumn("Started")
                .AddColumn("Status");

            foreach (var s in sessions)
            {
                var status = s.IsActive ? "[green]Active[/]" : "[grey]Ended[/]";
                table.AddRow(
                    s.SessionId[..Math.Min(8, s.SessionId.Length)] + "…",
                    s.DeviceId[..Math.Min(8, s.DeviceId.Length)] + "…",
                    s.StartedAt.LocalDateTime.ToString("g"),
                    status);
            }

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"[grey]Total: {sessions.Count} session(s)[/]");
            return 0;
        }
        catch (ApiException ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {ex.Message}");
            return 1;
        }
    }

    public static async Task<int> DisconnectAsync(
        string backend, string deviceId, CredentialStore creds, ApiClient client, CancellationToken ct)
    {
        if (!DeviceHandler.RequireLogin(creds)) return 1;

        try
        {
            var sessions = await AnsiConsole.Status()
                .StartAsync("Finding sessions for device...", async _ =>
                    await client.ListSessionsAsync(backend, ct));

            var active = sessions.Where(s => s.DeviceId == deviceId && s.IsActive).ToList();

            if (active.Count == 0)
            {
                AnsiConsole.MarkupLine($"[yellow]No active session found for device {deviceId}.[/]");
                return 0;
            }

            foreach (var s in active)
            {
                await client.TerminateSessionAsync(backend, s.SessionId, ct);
                AnsiConsole.MarkupLine($"[green]✓[/] Terminated session [bold]{s.SessionId}[/]");
            }

            return 0;
        }
        catch (ApiException ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {ex.Message}");
            return 1;
        }
    }
}
