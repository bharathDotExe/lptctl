using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using LaptopCtl.Backend.Services;
using LaptopCtl.Shared.Models;
using Microsoft.Extensions.Logging;

namespace LaptopCtl.Backend.WebSockets;

/// <summary>
/// Maintains a registry of connected agents and handles their WebSocket sessions.
/// Agents authenticate with their device token on connection.
/// </summary>
public sealed class AgentConnectionManager
{
    private readonly ConcurrentDictionary<string, AgentConnection> _connections = new();
    private readonly ILogger<AgentConnectionManager> _log;
    private readonly IServiceProvider _services;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AgentConnectionManager(ILogger<AgentConnectionManager> log, IServiceProvider services)
    {
        _log      = log;
        _services = services;
    }

    public bool IsAgentOnline(string deviceId) => _connections.ContainsKey(deviceId);

    public async Task<bool> SendToAgentAsync(string deviceId, WsMessage message, CancellationToken ct)
    {
        if (!_connections.TryGetValue(deviceId, out var conn)) return false;
        await conn.SendAsync(message, ct);
        return true;
    }

    public void RegisterResponseHandler(string requestId, TaskCompletionSource<WsMessage> tcs)
    {
        foreach (var conn in _connections.Values)
            conn.RegisterPending(requestId, tcs);
    }

    public async Task HandleAgentConnectionAsync(
        string deviceId, WebSocket ws, HttpContext ctx)
    {
        // Authenticate device token from Authorization header
        var authHeader = ctx.Request.Headers.Authorization.ToString();
        if (!authHeader.StartsWith("Device ", StringComparison.OrdinalIgnoreCase))
        {
            _log.LogWarning("Agent connection rejected - missing device auth: {DeviceId}", deviceId);
            await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Unauthorized", CancellationToken.None);
            return;
        }

        var rawToken = authHeader["Device ".Length..].Trim();
        var agentVersion = ctx.Request.Headers["X-Agent-Version"].FirstOrDefault() ?? "1.0.0";
        var deviceName   = ctx.Request.Headers["X-Device-Name"].FirstOrDefault();
        var osVersion    = ctx.Request.Headers["X-OS-Version"].FirstOrDefault();

        using var scope = _services.CreateScope();
        var deviceSvc   = scope.ServiceProvider.GetRequiredService<DeviceService>();

        var (valid, ownerId) = await deviceSvc.ValidateDeviceTokenAsync(deviceId, rawToken, deviceName, osVersion);
        if (!valid)
        {
            _log.LogWarning("Invalid device token for {DeviceId}", deviceId);
            await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Invalid device token", CancellationToken.None);
            return;
        }

        var conn = new AgentConnection(deviceId, ws, _log);
        _connections[deviceId] = conn;

        await deviceSvc.UpdateHeartbeatAsync(deviceId, agentVersion, deviceName, osVersion);
        _log.LogInformation("Agent connected: {DeviceId} ({Name})", deviceId, deviceName ?? "Device");

        try
        {
            await conn.RunReceiveLoopAsync(ctx.RequestAborted);
        }
        finally
        {
            _connections.TryRemove(deviceId, out _);
            await deviceSvc.MarkOfflineAsync(deviceId);
            _log.LogInformation("Agent disconnected: {DeviceId}", deviceId);
        }
    }
}

public sealed class AgentConnection
{
    private readonly string _deviceId;
    private readonly WebSocket _ws;
    private readonly ILogger _log;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<WsMessage>> _pending = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AgentConnection(string deviceId, WebSocket ws, ILogger log)
    {
        _deviceId = deviceId;
        _ws       = ws;
        _log      = log;
    }

    public void RegisterPending(string requestId, TaskCompletionSource<WsMessage> tcs)
        => _pending[requestId] = tcs;

    public async Task SendAsync(WsMessage message, CancellationToken ct)
    {
        var json  = JsonSerializer.Serialize(message, JsonOpts);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    public async Task RunReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new ArraySegment<byte>(new byte[64 * 1024]);
        while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await _ws.ReceiveAsync(buffer, ct);
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
                _log.LogWarning(ex, "Malformed message from agent {DeviceId}", _deviceId);
                continue;
            }

            if (msg is null) continue;

            // Replay protection
            if (msg.ExpiresAt < DateTimeOffset.UtcNow)
            {
                _log.LogWarning("Dropping expired message {RequestId} from {DeviceId}", msg.RequestId, _deviceId);
                continue;
            }

            // Route to pending request
            if (_pending.TryRemove(msg.RequestId, out var tcs))
            {
                tcs.TrySetResult(msg);
            }
        }
    }
}
