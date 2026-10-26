using LaptopCtl.Agent.Services;
using LaptopCtl.Shared.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace LaptopCtl.Agent;

/// <summary>
/// Main background loop: connects to backend, maintains heartbeat,
/// dispatches incoming commands to appropriate handlers.
/// </summary>
public sealed class AgentWorker : BackgroundService
{
    private readonly ILogger<AgentWorker> _log;
    private readonly AgentIdentityService _identity;
    private readonly BackendClient _backend;
    private readonly DiagnosticsService _diagnostics;
    private readonly FileTransferService _fileTransfer;

    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PingInterval   = TimeSpan.FromSeconds(30);

    public AgentWorker(
        ILogger<AgentWorker> log,
        AgentIdentityService identity,
        BackendClient backend,
        DiagnosticsService diagnostics,
        FileTransferService fileTransfer)
    {
        _log          = log;
        _identity     = identity;
        _backend      = backend;
        _diagnostics  = diagnostics;
        _fileTransfer = fileTransfer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("AgentWorker starting");
        var id = await _identity.GetOrCreateIdentityAsync(stoppingToken);

        if (!id.IsEnrolled)
        {
            _log.LogWarning(
                "Device is not yet enrolled. Run the installer with an enrollment code to register this device.");
            // Wait for enrollment — installer will set it and restart the service
            await Task.Delay(Timeout.Infinite, stoppingToken);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndRunLoopAsync(id, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Connection failed, retrying in {Delay}", ReconnectDelay);
            }

            if (!stoppingToken.IsCancellationRequested)
                await Task.Delay(ReconnectDelay, stoppingToken);
        }

        await _backend.DisconnectAsync();
        _log.LogInformation("AgentWorker stopped");
    }

    private async Task ConnectAndRunLoopAsync(IdentityData id, CancellationToken ct)
    {
        await _backend.ConnectAsync(id.DeviceId, id.DeviceToken, ct);

        // Announce presence
        await _backend.SendAsync(new WsMessage
        {
            Type    = WsMessageTypes.AgentHello,
            Payload = new { AgentVersion = AgentVersion.Current, DeviceId = id.DeviceId }
        }, ct);

        using var pingTimer = new PeriodicTimer(PingInterval);

        var receiveTask = ReceiveLoopAsync(id, ct);
        var pingTask    = PingLoopAsync(pingTimer, ct);

        await Task.WhenAny(receiveTask, pingTask);

        // Re-throw any exception
        if (receiveTask.IsCompleted) await receiveTask;
        if (pingTask.IsCompleted)    await pingTask;
    }

    private async Task ReceiveLoopAsync(IdentityData id, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var msg = await _backend.ReceiveAsync(ct);
            if (msg is null)
            {
                _log.LogInformation("Server closed connection");
                return;
            }

            _log.LogDebug("Received message type={Type} requestId={RequestId}", msg.Type, msg.RequestId);

            // Replay protection: reject messages older than 5 minutes
            if (msg.ExpiresAt < DateTimeOffset.UtcNow)
            {
                _log.LogWarning("Rejecting expired message requestId={RequestId}", msg.RequestId);
                continue;
            }

            _ = Task.Run(() => HandleMessageAsync(msg, id, ct), ct);
        }
    }

    private async Task HandleMessageAsync(WsMessage msg, IdentityData id, CancellationToken ct)
    {
        try
        {
            switch (msg.Type)
            {
                case WsMessageTypes.RequestDiagnostic:
                    await HandleDiagnosticAsync(msg, ct);
                    break;

                case WsMessageTypes.RequestFileList:
                    await HandleFileListAsync(msg, ct);
                    break;

                case WsMessageTypes.RequestFileGet:
                    await HandleFileGetAsync(msg, ct);
                    break;

                case WsMessageTypes.RequestFilePut:
                    await HandleFilePutAsync(msg, ct);
                    break;

                case WsMessageTypes.RequestCommand:
                    await HandleCommandAsync(msg, ct);
                    break;

                case WsMessageTypes.SessionTerminate:
                    _log.LogInformation("Session terminated by server");
                    break;

                case WsMessageTypes.BackendPong:
                    // heartbeat reply
                    break;

                default:
                    _log.LogWarning("Unsupported message type: {Type}", msg.Type);
                    await SendErrorAsync(msg.RequestId, "UNSUPPORTED_OPERATION",
                        $"Operation '{msg.Type}' is not supported by this agent version", ct);
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error handling message {Type}", msg.Type);
            await SendErrorAsync(msg.RequestId, "INTERNAL_ERROR", ex.Message, ct);
        }
    }

    private async Task HandleDiagnosticAsync(WsMessage msg, CancellationToken ct)
    {
        var req = DeserializePayload<DiagnosticRequest>(msg.Payload);
        if (req is null)
        {
            await SendErrorAsync(msg.RequestId, "INVALID_REQUEST", "Missing diagnostic request payload", ct);
            return;
        }

        object result = req.DiagnosticType switch
        {
            DiagnosticTypes.SystemInfo => await _diagnostics.GetSystemInfoAsync(ct),
            DiagnosticTypes.Processes  => await _diagnostics.GetProcessesAsync(ct),
            DiagnosticTypes.Disks      => await _diagnostics.GetDrivesAsync(ct),
            DiagnosticTypes.Network    => await _diagnostics.GetNetworkInterfacesAsync(ct),
            _                          => throw new NotSupportedException(
                                              $"Diagnostic type '{req.DiagnosticType}' is not supported")
        };

        await _backend.SendAsync(new WsMessage
        {
            Type      = WsMessageTypes.DiagnosticResult,
            RequestId = msg.RequestId,
            Payload   = result
        }, ct);
    }

    private async Task HandleFileListAsync(WsMessage msg, CancellationToken ct)
    {
        var req = DeserializePayload<FileListRequest>(msg.Payload);
        if (req is null)
        {
            await SendErrorAsync(msg.RequestId, "INVALID_REQUEST", "Missing file list request payload", ct);
            return;
        }

        try
        {
            var entries = await _fileTransfer.ListDirectoryAsync(req.RemotePath, ct);
            await _backend.SendAsync(new WsMessage
            {
                Type      = WsMessageTypes.DiagnosticResult,
                RequestId = msg.RequestId,
                Payload   = entries
            }, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to list directory: {Path}", req.RemotePath);
            await SendErrorAsync(msg.RequestId, "DIRECTORY_LIST_FAILED", ex.Message, ct);
        }
    }

    private async Task HandleFileGetAsync(WsMessage msg, CancellationToken ct)
    {
        var req = DeserializePayload<FileTransferRequest>(msg.Payload);
        if (req is null)
        {
            await SendErrorAsync(msg.RequestId, "INVALID_REQUEST", "Missing file transfer request payload", ct);
            return;
        }

        try
        {
            var content = await _fileTransfer.ReadFileContentAsync(req.RemotePath, ct);
            await _backend.SendAsync(new WsMessage
            {
                Type      = WsMessageTypes.DiagnosticResult,
                RequestId = msg.RequestId,
                Payload   = content
            }, ct);
        }
        catch (Exception ex)
        {
            await SendErrorAsync(msg.RequestId, "FILE_READ_FAILED", ex.Message, ct);
        }
    }

    private async Task HandleFilePutAsync(WsMessage msg, CancellationToken ct)
    {
        var req = DeserializePayload<FileTransferRequest>(msg.Payload);
        if (req is null)
        {
            await SendErrorAsync(msg.RequestId, "INVALID_REQUEST", "Missing file transfer request payload", ct);
            return;
        }
        await _fileTransfer.ReceiveFileAsync(req.RemotePath, req.FileSizeBytes, req.Sha256, msg.RequestId, _backend, ct);
    }

    private async Task HandleCommandAsync(WsMessage msg, CancellationToken ct)
    {
        var req = DeserializePayload<CommandExecutionRequest>(msg.Payload);
        if (req is null || string.IsNullOrWhiteSpace(req.Command))
        {
            await SendErrorAsync(msg.RequestId, "INVALID_REQUEST", "Command cannot be empty", ct);
            return;
        }

        // Check if command is an interactive request (open YouTube, open folder, launch GUI app)
        if (InteractiveLauncher.IsInteractiveCommand(req.Command, out var interactiveTarget))
        {
            var swInter = Stopwatch.StartNew();
            bool ok = InteractiveLauncher.TryLaunchInActiveSession(interactiveTarget, out string launchOutput);
            swInter.Stop();

            await _backend.SendAsync(new WsMessage
            {
                Type = WsMessageTypes.CommandResult,
                RequestId = msg.RequestId,
                Payload = new CommandExecutionResult
                {
                    ExitCode = ok ? 0 : 1,
                    Stdout = launchOutput,
                    Stderr = ok ? string.Empty : launchOutput,
                    Duration = swInter.Elapsed
                }
            }, ct);
            return;
        }

        var sw = Stopwatch.StartNew();
        var timeout = TimeSpan.FromSeconds(Math.Clamp(req.TimeoutSeconds, 1, 300));

        string commandToRun = req.Command + (string.IsNullOrEmpty(req.Arguments) ? "" : " " + req.Arguments);
        string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(commandToRun));
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = new Process { StartInfo = psi };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdoutBuilder.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderrBuilder.AppendLine(e.Data); };

        try
        {
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            await proc.WaitForExitAsync(cts.Token);
            sw.Stop();

            await _backend.SendAsync(new WsMessage
            {
                Type = WsMessageTypes.CommandResult,
                RequestId = msg.RequestId,
                Payload = new CommandExecutionResult
                {
                    ExitCode = proc.ExitCode,
                    Stdout = stdoutBuilder.ToString().TrimEnd(),
                    Stderr = stderrBuilder.ToString().TrimEnd(),
                    Duration = sw.Elapsed
                }
            }, ct);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            await SendErrorAsync(msg.RequestId, "TIMEOUT", $"Command timed out after {timeout.TotalSeconds} seconds", ct);
        }
        catch (Exception ex)
        {
            await SendErrorAsync(msg.RequestId, "EXEC_FAILED", ex.Message, ct);
        }
    }

    private async Task SendErrorAsync(string requestId, string code, string message, CancellationToken ct)
    {
        try
        {
            await _backend.SendAsync(new WsMessage
            {
                Type      = WsMessageTypes.DiagnosticError,
                RequestId = requestId,
                Payload   = new ErrorResponse { Code = code, Message = message }
            }, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to send error response for {RequestId}", requestId);
        }
    }

    private async Task PingLoopAsync(PeriodicTimer timer, CancellationToken ct)
    {
        while (await timer.WaitForNextTickAsync(ct))
        {
            await _backend.SendAsync(new WsMessage { Type = WsMessageTypes.AgentPing }, ct);
        }
    }

    private static T? DeserializePayload<T>(object? payload)
    {
        if (payload is null) return default;
        if (payload is JsonElement el)
            return el.Deserialize<T>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var json = JsonSerializer.Serialize(payload);
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
}
