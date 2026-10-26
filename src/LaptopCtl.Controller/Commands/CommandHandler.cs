using System.Text.Json;
using LaptopCtl.Shared.Models;
using Spectre.Console;

namespace LaptopCtl.Controller.Commands;

public static class CommandHandler
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task<int> ExecAsync(
        string backend, string deviceId, string command,
        CredentialStore creds, ApiClient client, CancellationToken ct)
    {
        if (!DeviceHandler.RequireLogin(creds)) return 1;

        if (string.IsNullOrWhiteSpace(command))
        {
            AnsiConsole.MarkupLine("[red]✗[/] Command cannot be empty.");
            return 1;
        }

        try
        {
            var msg = new WsMessage
            {
                Type = WsMessageTypes.RequestCommand,
                Payload = new CommandExecutionRequest
                {
                    Command = command,
                    TimeoutSeconds = 60
                }
            };

            var resp = await AnsiConsole.Status()
                .StartAsync($"Executing '{command}' on {deviceId}...", async _ =>
                    await client.SendRelayMessageAsync(backend, deviceId, msg, ct));

            var json = resp.Payload is JsonElement je
                ? je.GetRawText()
                : JsonSerializer.Serialize(resp.Payload, JsonOpts);

            var result = JsonSerializer.Deserialize<CommandExecutionResult>(json, JsonOpts);

            if (result is null)
            {
                AnsiConsole.MarkupLine("[yellow]Empty command execution result.[/]");
                return 0;
            }

            if (!string.IsNullOrEmpty(result.Stdout))
            {
                AnsiConsole.WriteLine(result.Stdout);
            }

            if (!string.IsNullOrEmpty(result.Stderr))
            {
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(result.Stderr)}[/]");
            }

            var statusColor = result.ExitCode == 0 ? "green" : "red";
            AnsiConsole.MarkupLine($"[grey]Process exited with code [{statusColor}]{result.ExitCode}[/] in {result.Duration.TotalMilliseconds:F0}ms[/]");
            return result.ExitCode;
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

    public static async Task<int> GenerateEnrollmentCodeAsync(
        string backend, CredentialStore creds, ApiClient client, CancellationToken ct)
    {
        if (!DeviceHandler.RequireLogin(creds)) return 1;

        try
        {
            var codeResp = await AnsiConsole.Status()
                .StartAsync("Generating enrollment code...", async _ =>
                    await client.GenerateEnrollmentCodeAsync(backend, ct));

            var scriptUrl = $"{backend.TrimEnd('/')}/install.ps1";
            var oneLiner = $"irm {scriptUrl} | iex; Install-LaptopCtl -EnrollmentCode \"{codeResp.Code}\" -Backend \"{backend}\"";

            var panel = new Panel(
                $"[bold green]Code:[/]       [bold yellow]{codeResp.Code}[/]\n" +
                $"[bold]Expires:[/]    {codeResp.ExpiresAt.LocalDateTime:f}\n\n" +
                $"[bold cyan]Run this single command on the target Windows computer (as Admin):[/]\n\n" +
                $"[white on grey15]  {Markup.Escape(oneLiner)}  [/]")
            {
                Header = new PanelHeader(" [bold cornflowerblue]LaptopCtl Device Enrollment[/] "),
                Border = BoxBorder.Rounded
            };

            AnsiConsole.Write(panel);
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
}
