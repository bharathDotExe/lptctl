using System.Security.Cryptography;
using System.Text.Json;
using LaptopCtl.Shared.Models;
using Spectre.Console;

namespace LaptopCtl.Controller.Commands;

public static class FileHandler
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task<int> ListAsync(
        string backend, string deviceId, string remotePath,
        CredentialStore creds, ApiClient client, CancellationToken ct)
    {
        if (!DeviceHandler.RequireLogin(creds)) return 1;

        try
        {
            var msg = new WsMessage
            {
                Type = WsMessageTypes.RequestFileList,
                Payload = new FileListRequest
                {
                    RemotePath = remotePath
                }
            };

            var resp = await AnsiConsole.Status()
                .StartAsync($"Listing directory '{remotePath}' on {deviceId}...", async _ =>
                    await client.SendRelayMessageAsync(backend, deviceId, msg, ct));

            var json = resp.Payload is JsonElement je
                ? je.GetRawText()
                : JsonSerializer.Serialize(resp.Payload, JsonOpts);

            var entries = JsonSerializer.Deserialize<List<FileEntry>>(json, JsonOpts);

            if (entries is null || entries.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]Directory is empty or not found.[/]");
                return 0;
            }

            var table = new Table()
                .Border(TableBorder.Rounded)
                .Title($"[bold]Directory listing: {remotePath}[/]")
                .AddColumn("Name")
                .AddColumn("Type")
                .AddColumn(new TableColumn("Size").RightAligned())
                .AddColumn("Last Modified");

            foreach (var entry in entries.OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name))
            {
                var icon = entry.IsDirectory ? "[blue]📁 (dir)[/]" : "[grey]📄 file[/]";
                var size = entry.IsDirectory
                    ? "-"
                    : FormatBytes(entry.SizeBytes ?? 0);
                var modified = entry.LastModified?.LocalDateTime.ToString("g") ?? "-";

                table.AddRow(entry.Name, icon, size, modified);
            }

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"[grey]Total: {entries.Count} items[/]");
            return 0;
        }
        catch (ApiException ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Error: {ex.Message}");
            return 1;
        }
    }

    public static async Task<int> UploadAsync(
        string backend, string deviceId, string localPath, string remotePath,
        CredentialStore creds, ApiClient client, CancellationToken ct)
    {
        if (!DeviceHandler.RequireLogin(creds)) return 1;

        if (!File.Exists(localPath))
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Local file not found: {localPath}");
            return 1;
        }

        try
        {
            var fi = new FileInfo(localPath);
            var sha256 = await ComputeSha256Async(localPath, ct);

            AnsiConsole.MarkupLine($"[grey]Uploading {fi.Name} ({FormatBytes(fi.Length)}, SHA-256: {sha256[..8]}...)[/]");

            var initMsg = new WsMessage
            {
                Type = WsMessageTypes.RequestFilePut,
                Payload = new FileTransferRequest
                {
                    RemotePath = remotePath,
                    Direction = TransferDirection.Upload,
                    FileSizeBytes = fi.Length,
                    Sha256 = sha256
                }
            };

            var resp = await AnsiConsole.Status()
                .StartAsync($"Initiating upload to {remotePath}...", async _ =>
                    await client.SendRelayMessageAsync(backend, deviceId, initMsg, ct));

            AnsiConsole.MarkupLine($"[green]✓[/] Upload initiated successfully to '{remotePath}' on device {deviceId}.");
            return 0;
        }
        catch (ApiException ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Error: {ex.Message}");
            return 1;
        }
    }

    public static async Task<int> DownloadAsync(
        string backend, string deviceId, string remotePath, string localPath,
        CredentialStore creds, ApiClient client, CancellationToken ct)
    {
        if (!DeviceHandler.RequireLogin(creds)) return 1;

        try
        {
            var msg = new WsMessage
            {
                Type = WsMessageTypes.RequestFileGet,
                Payload = new FileTransferRequest
                {
                    RemotePath = remotePath,
                    Direction = TransferDirection.Download
                }
            };

            var resp = await AnsiConsole.Status()
                .StartAsync($"Requesting download of '{remotePath}'...", async _ =>
                    await client.SendRelayMessageAsync(backend, deviceId, msg, ct));

            AnsiConsole.MarkupLine($"[green]✓[/] Download request received from device {deviceId}.");
            return 0;
        }
        catch (ApiException ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Error: {ex.Message}");
            return 1;
        }
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):F1} GB",
            >= 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            >= 1024 => $"{bytes / 1024.0:F1} KB",
            _ => $"{bytes} B"
        };
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct)
    {
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = await SHA256.Create().ComputeHashAsync(fs, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
