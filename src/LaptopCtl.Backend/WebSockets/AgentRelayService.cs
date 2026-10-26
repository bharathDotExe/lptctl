using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using LaptopCtl.Backend.Services;
using LaptopCtl.Shared.Models;
using Microsoft.Extensions.Logging;

namespace LaptopCtl.Backend.WebSockets;

/// <summary>
/// Relays commands from authenticated controllers to connected agents.
/// Controllers connect with their JWT. The relay verifies device ownership
/// before forwarding any command to the agent.
/// </summary>
public sealed class AgentRelayService
{
    private readonly AgentConnectionManager _agents;
    private readonly ILogger<AgentRelayService> _log;
    private readonly IServiceProvider _services;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AgentRelayService(AgentConnectionManager agents,
        ILogger<AgentRelayService> log, IServiceProvider services)
    {
        _agents   = agents;
        _log      = log;
        _services = services;
    }

    public async Task HandleControllerConnectionAsync(
        string deviceId, WebSocket ws, HttpContext ctx)
    {
        var userId = GetUserId(ctx);
        if (userId == 0)
        {
            await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Unauthorized", CancellationToken.None);
            return;
        }

        // Verify device ownership
        using var scope = _services.CreateScope();
        var deviceSvc   = scope.ServiceProvider.GetRequiredService<DeviceService>();
        var device      = await deviceSvc.GetDeviceAsync(userId, deviceId);

        if (device is null)
        {
            _log.LogWarning("User {UserId} attempted to connect to unowned device {DeviceId}", userId, deviceId);
            await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Device not found", CancellationToken.None);
            return;
        }

        if (!_agents.IsAgentOnline(deviceId))
        {
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Agent offline", CancellationToken.None);
            return;
        }

        _log.LogInformation("Controller user {UserId} connected to device {DeviceId}", userId, deviceId);
        var sessionSvc = scope.ServiceProvider.GetRequiredService<SessionService>();
        var session    = await sessionSvc.CreateSessionAsync(userId, deviceId, device.OwnerUserId is not null ? userId : userId);

        try
        {
            await RunRelayLoopAsync(ws, deviceId, userId, session.SessionId, ctx.RequestAborted);
        }
        finally
        {
            await sessionSvc.TerminateSessionAsync(userId, session.SessionId);
            _log.LogInformation("Controller disconnected from {DeviceId}", deviceId);
        }
    }

    private async Task RunRelayLoopAsync(
        WebSocket controllerWs, string deviceId, int userId,
        string sessionId, CancellationToken ct)
    {
        var buffer = new ArraySegment<byte>(new byte[64 * 1024]);

        while (controllerWs.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await controllerWs.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) return;
                ms.Write(buffer.Array!, buffer.Offset, result.Count);
            }
            while (!result.EndOfMessage);

            ms.Seek(0, SeekOrigin.Begin);
            WsMessage? msg;
            try
            {
                msg = await JsonSerializer.DeserializeAsync<WsMessage>(ms, JsonOpts, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Malformed message from controller user {UserId}", userId);
                continue;
            }

            if (msg is null) continue;

            // Validate only allowed message types can be forwarded by controllers
            if (!IsAllowedControllerMessage(msg.Type))
            {
                _log.LogWarning("Controller sent disallowed message type {Type}", msg.Type);
                var errBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                    new WsMessage
                    {
                        Type      = WsMessageTypes.DiagnosticError,
                        RequestId = msg.RequestId,
                        Payload   = new ErrorResponse { Code = "FORBIDDEN", Message = $"Message type '{msg.Type}' is not allowed from controllers" }
                    }, JsonOpts));
                await controllerWs.SendAsync(errBytes, WebSocketMessageType.Text, true, ct);
                continue;
            }

            // Forward to agent and wait for response
            var tcs = new TaskCompletionSource<WsMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            _agents.RegisterResponseHandler(msg.RequestId, tcs);

            bool sent = await _agents.SendToAgentAsync(deviceId, msg, ct);
            if (!sent)
            {
                tcs.TrySetCanceled();
                var errBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                    new WsMessage { Type = WsMessageTypes.DiagnosticError, RequestId = msg.RequestId,
                        Payload = new ErrorResponse { Code = "AGENT_OFFLINE", Message = "Agent is offline" } }, JsonOpts));
                await controllerWs.SendAsync(errBytes, WebSocketMessageType.Text, true, ct);
                continue;
            }

            // Wait for agent response (timeout after 60 seconds)
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var linked  = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            try
            {
                var response = await tcs.Task.WaitAsync(linked.Token);
                var respBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response, JsonOpts));
                await controllerWs.SendAsync(respBytes, WebSocketMessageType.Text, true, ct);
            }
            catch (OperationCanceledException)
            {
                var errBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                    new WsMessage { Type = WsMessageTypes.DiagnosticError, RequestId = msg.RequestId,
                        Payload = new ErrorResponse { Code = "TIMEOUT", Message = "Agent did not respond in time" } }, JsonOpts));
                await controllerWs.SendAsync(errBytes, WebSocketMessageType.Text, true, ct);
            }
        }
    }

    private static bool IsAllowedControllerMessage(string msgType) => msgType switch
    {
        WsMessageTypes.RequestDiagnostic => true,
        WsMessageTypes.RequestCommand    => true,
        WsMessageTypes.RequestFileList   => true,
        WsMessageTypes.RequestFileGet    => true,
        WsMessageTypes.RequestFilePut    => true,
        WsMessageTypes.SessionTerminate  => true,
        _                                => false
    };

    private static int GetUserId(HttpContext ctx)
    {
        var claim = ctx.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)
            ?? ctx.User?.FindFirst("sub");
        return int.TryParse(claim?.Value, out var id) ? id : 0;
    }
}
