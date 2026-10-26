using LaptopCtl.Shared.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace LaptopCtl.Agent.Services;

/// <summary>
/// Handles all communication with the LaptopCtl backend:
/// REST for enrollment, WebSockets for real-time commands.
/// </summary>
public sealed class BackendClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<BackendClient> _log;
    private ClientWebSocket? _ws;
    private readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public BackendClient(HttpClient http, IConfiguration config, ILogger<BackendClient> log)
    {
        _http   = http;
        _config = config;
        _log    = log;
    }

    private string BackendUrl
    {
        get
        {
            var url = _config["BackendUrl"]
                   ?? _config["LAPTOPCTL_BackendUrl"]
                   ?? _config["LaptopCtl:BackendUrl"];
            if (!string.IsNullOrWhiteSpace(url)) return url.TrimEnd('/');

            // Fallback: read from identity.json
            try
            {
                if (File.Exists(AgentPaths.IdentityFile))
                {
                    var json = File.ReadAllText(AgentPaths.IdentityFile);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("BackendUrl", out var prop) && !string.IsNullOrWhiteSpace(prop.GetString()))
                    {
                        return prop.GetString()!.TrimEnd('/');
                    }
                }
            }
            catch { }

            throw new InvalidOperationException(
                "BackendUrl is not configured. Set LAPTOPCTL_BackendUrl or specify in appsettings.json/identity.json.");
        }
    }

    // ─── Enrollment ───────────────────────────────────────────────────────────

    public async Task<EnrollmentResponse> EnrollAsync(EnrollmentRequest request, CancellationToken ct)
    {
        _log.LogInformation("Enrolling device with backend");
        using var resp = await _http.PostAsJsonAsync(
            $"{BackendUrl}/api/enrollment", request, _jsonOpts, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Enrollment failed ({(int)resp.StatusCode}): {body}");
        }

        return await resp.Content.ReadFromJsonAsync<EnrollmentResponse>(_jsonOpts, ct)
               ?? throw new InvalidOperationException("Empty enrollment response");
    }

    // ─── WebSocket connection ─────────────────────────────────────────────────

    public bool IsConnected => _ws?.State == WebSocketState.Open;

    public async Task ConnectAsync(string deviceId, string deviceToken, CancellationToken ct)
    {
        var wsUrl = BackendUrl
            .Replace("https://", "wss://")
            .Replace("http://", "ws://")
            + $"/ws/agent/{deviceId}";

        _log.LogInformation("Connecting WebSocket to {Url}", wsUrl);

        _ws?.Dispose();
        _ws = new ClientWebSocket();
        _ws.Options.SetRequestHeader("Authorization", $"Device {deviceToken}");
        _ws.Options.SetRequestHeader("X-Agent-Version", AgentVersion.Current);
        _ws.Options.SetRequestHeader("X-Device-Name", Environment.MachineName);
        _ws.Options.SetRequestHeader("X-OS-Version", Environment.OSVersion.ToString());

        await _ws.ConnectAsync(new Uri(wsUrl), ct);
        _log.LogInformation("WebSocket connected");
    }

    public async Task SendAsync(WsMessage message, CancellationToken ct)
    {
        if (_ws is null || _ws.State != WebSocketState.Open)
            throw new InvalidOperationException("WebSocket not connected");

        var json = JsonSerializer.Serialize(message, _jsonOpts);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    public async Task<WsMessage?> ReceiveAsync(CancellationToken ct)
    {
        if (_ws is null) return null;

        var buffer = new ArraySegment<byte>(new byte[64 * 1024]);
        using var ms = new MemoryStream();

        WebSocketReceiveResult result;
        do
        {
            result = await _ws.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                _log.LogInformation("Server closed WebSocket: {Description}", result.CloseStatusDescription);
                return null;
            }
            ms.Write(buffer.Array!, buffer.Offset, result.Count);
        }
        while (!result.EndOfMessage);

        ms.Seek(0, SeekOrigin.Begin);
        return await JsonSerializer.DeserializeAsync<WsMessage>(ms, _jsonOpts, ct);
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        if (_ws?.State == WebSocketState.Open)
        {
            try
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Agent shutting down", ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Error during WebSocket close");
            }
        }
    }

    public void Dispose()
    {
        _ws?.Dispose();
        _http.Dispose();
    }
}
