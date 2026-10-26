using LaptopCtl.Backend.Services;
using LaptopCtl.Backend.WebSockets;
using LaptopCtl.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace LaptopCtl.Backend.Controllers;

[Authorize]
[ApiController]
[Route("api/devices")]
public sealed class DevicesController : ControllerBase
{
    private readonly DeviceService _devices;
    private readonly SessionService _sessions;
    private readonly AgentConnectionManager _agentMgr;

    public DevicesController(DeviceService devices, SessionService sessions,
        AgentConnectionManager agentMgr)
    {
        _devices  = devices;
        _sessions = sessions;
        _agentMgr = agentMgr;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var userId = GetUserId();
        var result = await _devices.ListDevicesAsync(userId, page, pageSize, ct);
        return Ok(result);
    }

    [HttpGet("{deviceId}")]
    public async Task<IActionResult> Get(string deviceId, CancellationToken ct)
    {
        var userId = GetUserId();
        var device = await _devices.GetDeviceAsync(userId, deviceId, ct);
        if (device is null) return NotFound(NotFoundError(deviceId));

        return Ok(device);
    }

    [HttpGet("{deviceId}/status")]
    public async Task<IActionResult> Status(string deviceId, CancellationToken ct)
    {
        var userId = GetUserId();
        var device = await _devices.GetDeviceAsync(userId, deviceId, ct);
        if (device is null) return NotFound(NotFoundError(deviceId));

        return Ok(new
        {
            device.DeviceId,
            device.IsOnline,
            device.LastSeen,
            IsAgentConnected = _agentMgr.IsAgentOnline(deviceId)
        });
    }

    [HttpDelete("{deviceId}")]
    public async Task<IActionResult> Revoke(string deviceId, CancellationToken ct)
    {
        var userId = GetUserId();
        var ip     = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ok     = await _devices.RevokeDeviceAsync(userId, deviceId, ip, ct);

        if (!ok) return NotFound(NotFoundError(deviceId));

        // Disconnect agent WebSocket if active
        await _agentMgr.DisconnectAgentAsync(deviceId);

        return Ok(new { Message = $"Device {deviceId} revoked and removed from dashboard." });
    }

    [HttpPost("{deviceId}/uninstall")]
    public async Task<IActionResult> Uninstall(string deviceId, CancellationToken ct)
    {
        var userId = GetUserId();
        var device = await _devices.GetDeviceAsync(userId, deviceId, ct);
        if (device is null) return NotFound(NotFoundError(deviceId));

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        bool agentWasOnline = _agentMgr.IsAgentOnline(deviceId);

        if (agentWasOnline)
        {
            // Trigger remote self-destruct / uninstaller on the agent:
            // Spawns a detached hidden PowerShell process that waits 2 seconds, downloads uninstall.ps1,
            // terminates agent service/processes, deletes service, and wipes local files.
            var uninstallUrl = $"{Request.Scheme}://{Request.Host}/uninstall.ps1";
            var selfDestructCmd = $"Start-Process powershell.exe -ArgumentList \"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command & {{ Start-Sleep -Seconds 2; try {{ irm '{uninstallUrl}' | iex }} catch {{}} }}\" -WindowStyle Hidden";

            var msg = new WsMessage
            {
                Type = WsMessageTypes.RequestCommand,
                Payload = new CommandExecutionRequest
                {
                    Command = selfDestructCmd,
                    TimeoutSeconds = 10
                }
            };

            try
            {
                using var quickCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, quickCts.Token);
                await _agentMgr.SendToAgentAsync(deviceId, msg, linked.Token);
                await Task.Delay(500, ct); // Let packet flush through TCP stack to agent
            }
            catch { }
        }

        // Revoke device record in DB (EnrollmentStatus = "Revoked" so it completely vanishes from dashboard)
        await _devices.RevokeDeviceAsync(userId, deviceId, ip, ct);

        // Disconnect WebSocket session
        await _agentMgr.DisconnectAgentAsync(deviceId);

        return Ok(new
        {
            Success = true,
            AgentWasOnline = agentWasOnline,
            Message = agentWasOnline
                ? $"Uninstall command dispatched to {device.DeviceName}. The agent service and files will be removed from that computer and it has vanished from your dashboard."
                : $"Device {device.DeviceName} has been removed from your dashboard. (Note: Device was offline; run 'irm {Request.Scheme}://{Request.Host}/uninstall.ps1 | iex' on that computer if you want to remove local files)."
        });
    }

    [AllowAnonymous]
    [HttpPost("{deviceId}/unenroll")]
    public async Task<IActionResult> Unenroll(string deviceId, CancellationToken ct)
    {
        var authHeader = Request.Headers.Authorization.ToString();
        string rawToken = string.Empty;
        if (authHeader.StartsWith("Device ", StringComparison.OrdinalIgnoreCase))
        {
            rawToken = authHeader["Device ".Length..].Trim();
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ok = await _devices.UnenrollDeviceAsync(deviceId, rawToken, ip, ct);
        if (!ok)
        {
            return BadRequest(new { Message = "Could not unenroll device." });
        }

        await _agentMgr.DisconnectAgentAsync(deviceId);
        return Ok(new { Message = $"Device {deviceId} uninstalled and removed from dashboard successfully." });
    }

    [HttpPost("{deviceId}/sessions")]
    public async Task<IActionResult> CreateSession(string deviceId, CancellationToken ct)
    {
        var userId = GetUserId();
        try
        {
            var session = await _sessions.CreateSessionAsync(userId, deviceId, userId, ct);
            return Ok(session);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("{deviceId}/sessions")]
    public async Task<IActionResult> ListSessions(CancellationToken ct)
    {
        var userId = GetUserId();
        var list   = await _sessions.ListActiveSessionsAsync(userId, ct);
        return Ok(list);
    }

    [HttpPost("{deviceId}/exec")]
    public async Task<IActionResult> ExecuteCommand(
        string deviceId, [FromBody] CommandExecutionRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var device = await _devices.GetDeviceAsync(userId, deviceId, ct);
        if (device is null) return NotFound(NotFoundError(deviceId));

        if (!_agentMgr.IsAgentOnline(deviceId))
        {
            return StatusCode(503, new ErrorResponse
            {
                Code = "AGENT_OFFLINE",
                Message = $"Device '{deviceId}' is currently offline."
            });
        }

        var msg = new WsMessage
        {
            Type = WsMessageTypes.RequestCommand,
            Payload = request
        };

        var tcs = new TaskCompletionSource<WsMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _agentMgr.RegisterResponseHandler(msg.RequestId, tcs);

        var sent = await _agentMgr.SendToAgentAsync(deviceId, msg, ct);
        if (!sent)
        {
            return StatusCode(503, new ErrorResponse { Code = "AGENT_OFFLINE", Message = "Failed to dispatch command to agent." });
        }

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Min(request.TimeoutSeconds + 5, 60)));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            var response = await tcs.Task.WaitAsync(linkedCts.Token);
            if (response.Type == WsMessageTypes.DiagnosticError)
            {
                return BadRequest(response.Payload);
            }
            return Ok(response.Payload);
        }
        catch (OperationCanceledException)
        {
            return StatusCode(504, new ErrorResponse { Code = "TIMEOUT", Message = "Agent command execution timed out." });
        }
    }

    [HttpGet("{deviceId}/files")]
    public async Task<IActionResult> ListFiles(
        string deviceId, [FromQuery] string? path = "", CancellationToken ct = default)
    {
        var userId = GetUserId();
        var device = await _devices.GetDeviceAsync(userId, deviceId, ct);
        if (device is null) return NotFound(NotFoundError(deviceId));

        if (!_agentMgr.IsAgentOnline(deviceId))
        {
            return StatusCode(503, new ErrorResponse
            {
                Code = "AGENT_OFFLINE",
                Message = $"Device '{deviceId}' is currently offline."
            });
        }

        var msg = new WsMessage
        {
            Type = WsMessageTypes.RequestFileList,
            Payload = new FileListRequest { RemotePath = path ?? string.Empty }
        };

        var tcs = new TaskCompletionSource<WsMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _agentMgr.RegisterResponseHandler(msg.RequestId, tcs);

        var sent = await _agentMgr.SendToAgentAsync(deviceId, msg, ct);
        if (!sent)
        {
            return StatusCode(503, new ErrorResponse { Code = "AGENT_OFFLINE", Message = "Failed to dispatch file request to agent." });
        }

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            var response = await tcs.Task.WaitAsync(linkedCts.Token);
            if (response.Type == WsMessageTypes.DiagnosticError)
            {
                return BadRequest(response.Payload);
            }
            return Ok(response.Payload);
        }
        catch (OperationCanceledException)
        {
            return StatusCode(504, new ErrorResponse { Code = "TIMEOUT", Message = "Agent file listing timed out." });
        }
    }

    [HttpGet("{deviceId}/files/download")]
    public async Task<IActionResult> DownloadFile(
        string deviceId, [FromQuery] string path, CancellationToken ct = default)
    {
        var userId = GetUserId();
        var device = await _devices.GetDeviceAsync(userId, deviceId, ct);
        if (device is null) return NotFound(NotFoundError(deviceId));

        if (!_agentMgr.IsAgentOnline(deviceId))
        {
            return StatusCode(503, new ErrorResponse { Code = "AGENT_OFFLINE", Message = $"Device '{deviceId}' is offline." });
        }

        var msg = new WsMessage
        {
            Type = WsMessageTypes.RequestFileGet,
            Payload = new FileTransferRequest { RemotePath = path }
        };

        var tcs = new TaskCompletionSource<WsMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _agentMgr.RegisterResponseHandler(msg.RequestId, tcs);

        var sent = await _agentMgr.SendToAgentAsync(deviceId, msg, ct);
        if (!sent)
        {
            return StatusCode(503, new ErrorResponse { Code = "AGENT_OFFLINE", Message = "Failed to dispatch file download request." });
        }

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            var response = await tcs.Task.WaitAsync(linkedCts.Token);
            if (response.Type == WsMessageTypes.DiagnosticError)
            {
                return BadRequest(response.Payload);
            }
            if (response.Payload is System.Text.Json.JsonElement elem)
            {
                var download = System.Text.Json.JsonSerializer.Deserialize<FileDownloadResponse>(
                    elem.GetRawText(),
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (download != null && !string.IsNullOrEmpty(download.Base64Data))
                {
                    byte[] bytes = Convert.FromBase64String(download.Base64Data);
                    return File(bytes, "application/octet-stream", download.FileName);
                }
            }
            return Ok(response.Payload);
        }
        catch (OperationCanceledException)
        {
            return StatusCode(504, new ErrorResponse { Code = "TIMEOUT", Message = "Agent file download timed out." });
        }
    }

    private int GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
        if (int.TryParse(claim?.Value, out var id) && id > 0)
            return id;
        return 1;
    }

    private static ErrorResponse NotFoundError(string deviceId) => new()
    {
        Code    = "DEVICE_NOT_FOUND",
        Message = $"Device '{deviceId}' not found or not owned by this account."
    };
}
