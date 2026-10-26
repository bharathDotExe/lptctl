using System.Text.Json;
using LaptopCtl.Shared.Models;
using Spectre.Console;

namespace LaptopCtl.Controller.Commands;

public static class DiagnosticHandler
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task<int> RunAsync(
        string backend, string deviceId, string operation,
        CredentialStore creds, ApiClient client, CancellationToken ct)
    {
        if (!DeviceHandler.RequireLogin(creds)) return 1;

        try
        {
            var msg = new WsMessage
            {
                Type = WsMessageTypes.RequestDiagnostic,
                Payload = new DiagnosticRequest
                {
                    DiagnosticType = operation
                }
            };

            var resp = await AnsiConsole.Status()
                .StartAsync($"Querying {operation} on device {deviceId}...", async _ =>
                    await client.SendRelayMessageAsync(backend, deviceId, msg, ct));

            var json = resp.Payload is JsonElement je
                ? je.GetRawText()
                : JsonSerializer.Serialize(resp.Payload, JsonOpts);

            switch (operation)
            {
                case DiagnosticTypes.SystemInfo:
                    DisplaySystemInfo(deviceId, JsonSerializer.Deserialize<SystemInfo>(json, JsonOpts));
                    break;

                case DiagnosticTypes.Processes:
                    DisplayProcesses(deviceId, JsonSerializer.Deserialize<List<ProcessEntry>>(json, JsonOpts));
                    break;

                case DiagnosticTypes.Disks:
                    DisplayDisks(deviceId, JsonSerializer.Deserialize<List<LaptopCtl.Shared.Models.DriveInfo>>(json, JsonOpts));
                    break;

                case DiagnosticTypes.Network:
                    DisplayNetwork(deviceId, JsonSerializer.Deserialize<List<NetworkInterfaceInfo>>(json, JsonOpts));
                    break;

                default:
                    AnsiConsole.WriteLine(json);
                    break;
            }

            return 0;
        }
        catch (ApiException ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] An unexpected error occurred: {ex.Message}");
            return 1;
        }
    }

    private static void DisplaySystemInfo(string deviceId, SystemInfo? info)
    {
        if (info is null)
        {
            AnsiConsole.MarkupLine("[yellow]No system info returned.[/]");
            return;
        }

        var memGb = info.TotalMemoryBytes.HasValue
            ? $"{info.TotalMemoryBytes.Value / (1024.0 * 1024 * 1024):F1} GB"
            : "Unknown";

        var panel = new Panel(
            $"[bold]Device ID:[/]       {deviceId}\n" +
            $"[bold]Computer Name:[/]   {info.ComputerName}\n" +
            $"[bold]OS Version:[/]      {info.WindowsVersion}\n" +
            $"[bold]Architecture:[/]    {info.OsArchitecture}\n" +
            $"[bold]CPU Model:[/]       {info.CpuModel ?? "Unknown"}\n" +
            $"[bold]Total Memory:[/]    {memGb}\n" +
            $"[bold]Agent Version:[/]   v{info.AgentVersion}\n" +
            $"[bold]Last Contact:[/]    {info.LastConnection.LocalDateTime:f}")
        {
            Header = new PanelHeader($" [bold cornflowerblue]System Info: {info.ComputerName}[/] ")
        };

        AnsiConsole.Write(panel);
    }

    private static void DisplayProcesses(string deviceId, List<ProcessEntry>? procs)
    {
        if (procs is null || procs.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No processes returned.[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title($"[bold]Processes on {deviceId}[/]")
            .AddColumn(new TableColumn("PID").RightAligned())
            .AddColumn("Name")
            .AddColumn(new TableColumn("Memory (MB)").RightAligned())
            .AddColumn("Description")
            .AddColumn("Started");

        foreach (var p in procs.OrderByDescending(p => p.WorkingSetBytes).Take(40))
        {
            var memMb = $"{p.WorkingSetBytes / (1024.0 * 1024):F1}";
            var started = p.StartTime?.LocalDateTime.ToString("t") ?? "-";
            var desc = p.Description ?? string.Empty;
            if (desc.Length > 35) desc = desc[..35] + "…";

            table.AddRow(
                p.ProcessId.ToString(),
                p.Name,
                memMb,
                desc,
                started);
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[grey]Showing top {Math.Min(40, procs.Count)} of {procs.Count} processes sorted by memory[/]");
    }

    private static void DisplayDisks(string deviceId, List<LaptopCtl.Shared.Models.DriveInfo>? drives)
    {
        if (drives is null || drives.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No drive information returned.[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title($"[bold]Disk Drives on {deviceId}[/]")
            .AddColumn("Drive")
            .AddColumn("Label")
            .AddColumn("Format")
            .AddColumn(new TableColumn("Total").RightAligned())
            .AddColumn(new TableColumn("Free").RightAligned())
            .AddColumn(new TableColumn("Used %").RightAligned());

        foreach (var d in drives)
        {
            var totalGb = d.TotalBytes / (1024.0 * 1024 * 1024);
            var freeGb = d.AvailableBytes / (1024.0 * 1024 * 1024);
            var usedPercent = d.TotalBytes > 0
                ? ((d.TotalBytes - d.AvailableBytes) / (double)d.TotalBytes) * 100.0
                : 0.0;

            var percentMarkup = usedPercent > 90
                ? $"[red]{usedPercent:F1}%[/]"
                : usedPercent > 75
                    ? $"[yellow]{usedPercent:F1}%[/]"
                    : $"[green]{usedPercent:F1}%[/]";

            table.AddRow(
                d.Name,
                d.VolumeLabel ?? string.Empty,
                d.DriveFormat,
                $"{totalGb:F1} GB",
                $"{freeGb:F1} GB",
                percentMarkup);
        }

        AnsiConsole.Write(table);
    }

    private static void DisplayNetwork(string deviceId, List<NetworkInterfaceInfo>? ifaces)
    {
        if (ifaces is null || ifaces.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No network interfaces returned.[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title($"[bold]Network Interfaces on {deviceId}[/]")
            .AddColumn("Name")
            .AddColumn("Status")
            .AddColumn("IP Addresses")
            .AddColumn("Description");

        foreach (var net in ifaces)
        {
            var status = net.Status.Equals("Up", StringComparison.OrdinalIgnoreCase)
                ? "[green]Up[/]"
                : $"[grey]{net.Status}[/]";

            var addrs = string.Join(", ", net.Addresses);
            table.AddRow(net.Name, status, addrs, net.Description);
        }

        AnsiConsole.Write(table);
    }
}
