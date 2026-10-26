using LaptopCtl.Shared.Models;
using Spectre.Console;

namespace LaptopCtl.Controller.Commands;

public static class DeviceHandler
{
    public static async Task<int> ListAsync(
        string backend, CredentialStore creds, ApiClient client, CancellationToken ct)
    {
        if (!RequireLogin(creds)) return 1;

        try
        {
            var devices = await AnsiConsole.Status()
                .StartAsync("Fetching devices...", async _ =>
                    await client.ListDevicesAsync(backend, ct));

            if (devices.Items.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No enrolled devices found.[/]");
                return 0;
            }

            var table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("Device ID")
                .AddColumn("Name")
                .AddColumn("OS")
                .AddColumn("Agent")
                .AddColumn("Status")
                .AddColumn("Last Seen");

            foreach (var d in devices.Items)
            {
                var status = d.IsOnline
                    ? "[green]● Online[/]"
                    : "[grey]○ Offline[/]";
                table.AddRow(
                    d.DeviceId[..Math.Min(8, d.DeviceId.Length)] + "…",
                    d.DeviceName,
                    d.OsVersion.Length > 30 ? d.OsVersion[..30] + "…" : d.OsVersion,
                    d.AgentVersion,
                    status,
                    d.LastSeen.LocalDateTime.ToString("g"));
            }

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"[grey]Total: {devices.TotalCount} device(s)[/]");
            return 0;
        }
        catch (ApiException ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {ex.Message}");
            return 1;
        }
    }

    public static async Task<int> StatusAsync(
        string backend, string deviceId, CredentialStore creds, ApiClient client, CancellationToken ct)
    {
        if (!RequireLogin(creds)) return 1;

        try
        {
            var device = await AnsiConsole.Status()
                .StartAsync($"Getting status for {deviceId}...", async _ =>
                    await client.GetDeviceAsync(backend, deviceId, ct));

            var panel = new Panel(
                $"[bold]Device ID:[/]   {device.DeviceId}\n" +
                $"[bold]Name:[/]        {device.DeviceName}\n" +
                $"[bold]OS:[/]          {device.OsVersion}\n" +
                $"[bold]Agent:[/]       {device.AgentVersion}\n" +
                $"[bold]Status:[/]      {(device.IsOnline ? "[green]Online[/]" : "[grey]Offline[/]")}\n" +
                $"[bold]Last Seen:[/]   {device.LastSeen.LocalDateTime:f}\n" +
                $"[bold]Enrolled:[/]    {device.EnrolledAt.LocalDateTime:f}\n" +
                $"[bold]Enrollment:[/]  {device.EnrollmentStatus}")
            {
                Header = new PanelHeader($" [bold]Device: {device.DeviceName}[/] ")
            };
            AnsiConsole.Write(panel);
            return 0;
        }
        catch (ApiException ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {ex.Message}");
            return 1;
        }
    }

    public static async Task<int> ConnectAsync(
        string backend, string deviceId, CredentialStore creds, ApiClient client, CancellationToken ct)
    {
        if (!RequireLogin(creds)) return 1;

        try
        {
            var session = await AnsiConsole.Status()
                .StartAsync($"Opening session to {deviceId}...", async _ =>
                    await client.CreateSessionAsync(backend, deviceId, ct));

            AnsiConsole.MarkupLine($"[green]✓[/] Session opened: [bold]{session.SessionId}[/]");
            AnsiConsole.MarkupLine($"  Device: {deviceId}");
            AnsiConsole.MarkupLine($"  Started: {session.StartedAt.LocalDateTime:T}");
            AnsiConsole.MarkupLine("[grey]Use 'laptopctl disconnect <device-id>' to end the session.[/]");
            return 0;
        }
        catch (ApiException ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {ex.Message}");
            return 1;
        }
    }

    public static async Task<int> UninstallAsync(
        string backend, string deviceId, CredentialStore creds, ApiClient client, CancellationToken ct)
    {
        if (!RequireLogin(creds)) return 1;

        AnsiConsole.MarkupLine($"[yellow]⚠  This will permanently revoke device [bold]{deviceId}[/].[/]");
        AnsiConsole.MarkupLine("[yellow]   The agent on the target computer will lose access.[/]");
        AnsiConsole.MarkupLine("[yellow]   The device registration in your account will be removed.[/]");

        var confirm = AnsiConsole.Prompt(
            new TextPrompt<string>("Type the device ID to confirm:")
                .Validate(v => v == deviceId
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Device ID does not match[/]")));

        try
        {
            await AnsiConsole.Status()
                .StartAsync("Revoking device...", async _ =>
                    await client.RevokeDeviceAsync(backend, deviceId, ct));

            AnsiConsole.MarkupLine($"[green]✓[/] Device [bold]{deviceId}[/] has been revoked.");
            AnsiConsole.MarkupLine("[grey]Note: The agent binary on the target computer is not automatically removed.[/]");
            AnsiConsole.MarkupLine("[grey]To uninstall the agent from the target, run the uninstall script on that machine.[/]");
            return 0;
        }
        catch (ApiException ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {ex.Message}");
            return 1;
        }
    }

    public static bool RequireLogin(CredentialStore creds)
    {
        if (creds.IsLoggedIn) return true;
        AnsiConsole.MarkupLine("[red]✗[/] Not logged in. Run [bold]laptopctl login[/] first.");
        return false;
    }
}
