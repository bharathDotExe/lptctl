using System.Text.Json;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using LaptopCtl.Shared.Models;

namespace LaptopCtl.Controller;

/// <summary>HTTP + WebSocket client for the LaptopCtl backend API.</summary>
public sealed class ApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly CredentialStore _creds;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public ApiClient(CredentialStore creds)
    {
        _creds = creds;
        _http  = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _http.DefaultRequestHeaders.Add("User-Agent", $"LaptopCtlController/{ControllerVersion.Current}");
    }

    private void AttachAuth()
    {
        var token = _creds.AccessToken;
        if (!string.IsNullOrEmpty(token))
        {
            _http.DefaultRequestHeaders.Remove("Authorization");
            _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        }
    }

    public async Task<LoginResponse> LoginAsync(string backendUrl, string email, string password, CancellationToken ct)
    {
        using var resp = await _http.PostAsJsonAsync(
            $"{backendUrl}/api/auth/login",
            new LoginRequest { Email = email, Password = password },
            JsonOpts, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var err = await ReadErrorAsync(resp, ct);
            throw new ApiException($"Login failed: {err}");
        }

        return await resp.Content.ReadFromJsonAsync<LoginResponse>(JsonOpts, ct)
               ?? throw new ApiException("Empty login response");
    }

    public async Task LogoutAsync(string backendUrl, string refreshToken, CancellationToken ct)
    {
        AttachAuth();
        await _http.PostAsJsonAsync($"{backendUrl}/api/auth/logout",
            new RefreshRequest { RefreshToken = refreshToken }, JsonOpts, ct);
    }

    public async Task<GenerateEnrollmentCodeResponse> GenerateEnrollmentCodeAsync(string backendUrl, CancellationToken ct)
    {
        AttachAuth();
        using var resp = await _http.PostAsync($"{backendUrl}/api/enrollment/code", null, ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<GenerateEnrollmentCodeResponse>(JsonOpts, ct)
               ?? throw new ApiException("Empty enrollment code response");
    }

    public async Task<PagedResponse<DeviceInfo>> ListDevicesAsync(string backendUrl, CancellationToken ct)
    {
        AttachAuth();
        using var resp = await _http.GetAsync($"{backendUrl}/api/devices", ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<PagedResponse<DeviceInfo>>(JsonOpts, ct)
               ?? throw new ApiException("Empty devices response");
    }

    public async Task<DeviceInfo> GetDeviceAsync(string backendUrl, string deviceId, CancellationToken ct)
    {
        AttachAuth();
        using var resp = await _http.GetAsync($"{backendUrl}/api/devices/{deviceId}", ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<DeviceInfo>(JsonOpts, ct)
               ?? throw new ApiException("Empty device response");
    }

    public async Task<SessionInfo> CreateSessionAsync(string backendUrl, string deviceId, CancellationToken ct)
    {
        AttachAuth();
        using var resp = await _http.PostAsJsonAsync(
            $"{backendUrl}/api/devices/{deviceId}/sessions",
            new { }, JsonOpts, ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<SessionInfo>(JsonOpts, ct)
               ?? throw new ApiException("Empty session response");
    }

    public async Task RevokeDeviceAsync(string backendUrl, string deviceId, CancellationToken ct)
    {
        AttachAuth();
        using var resp = await _http.DeleteAsync($"{backendUrl}/api/devices/{deviceId}", ct);
        await EnsureSuccessAsync(resp, ct);
    }

    public async Task<List<AuditEvent>> GetAuditEventsAsync(string backendUrl, CancellationToken ct)
    {
        AttachAuth();
        using var resp = await _http.GetAsync($"{backendUrl}/api/audit", ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<List<AuditEvent>>(JsonOpts, ct)
               ?? new List<AuditEvent>();
    }

    public async Task<List<SessionInfo>> ListSessionsAsync(string backendUrl, CancellationToken ct)
    {
        AttachAuth();
        using var resp = await _http.GetAsync($"{backendUrl}/api/sessions", ct);
        await EnsureSuccessAsync(resp, ct);
        return await resp.Content.ReadFromJsonAsync<List<SessionInfo>>(JsonOpts, ct)
               ?? new List<SessionInfo>();
    }

    public async Task TerminateSessionAsync(string backendUrl, string sessionId, CancellationToken ct)
    {
        AttachAuth();
        using var resp = await _http.DeleteAsync($"{backendUrl}/api/sessions/{sessionId}", ct);
        await EnsureSuccessAsync(resp, ct);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;
        var msg = await ReadErrorAsync(resp, ct);
        throw new ApiException($"API error {(int)resp.StatusCode}: {msg}");
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            var err = await resp.Content.ReadFromJsonAsync<ErrorResponse>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
            return err?.Message ?? resp.ReasonPhrase ?? "Unknown error";
        }
        catch
        {
            return await resp.Content.ReadAsStringAsync(ct);
        }
    }

    public async Task<WsMessage> SendRelayMessageAsync(
        string backendUrl, string deviceId, WsMessage message, CancellationToken ct)
    {
        var token = _creds.AccessToken;
        if (string.IsNullOrEmpty(token))
            throw new ApiException("Not authenticated. Run 'laptopctl login' first.");

        var wsUrl = backendUrl
            .Replace("https://", "wss://")
            .Replace("http://", "ws://")
            .TrimEnd('/') + $"/ws/controller/{deviceId}?token={Uri.EscapeDataString(token)}";

        using var ws = new ClientWebSocket();
        using var linkCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkCts.CancelAfter(TimeSpan.FromSeconds(35));

        await ws.ConnectAsync(new Uri(wsUrl), linkCts.Token);

        var json = JsonSerializer.Serialize(message, JsonOpts);
        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, linkCts.Token);

        var buffer = new byte[64 * 1024];
        using var ms = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), linkCts.Token);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new ApiException($"Connection closed by remote: {result.CloseStatusDescription ?? "No reason given"}");
            ms.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        ms.Seek(0, SeekOrigin.Begin);
        var resp = await JsonSerializer.DeserializeAsync<WsMessage>(ms, JsonOpts, linkCts.Token)
            ?? throw new ApiException("Empty response received from relay");

        if (resp.Type == WsMessageTypes.DiagnosticError || resp.Type == WsMessageTypes.FileTransferError)
        {
            var errStr = resp.Payload?.ToString();
            try
            {
                if (resp.Payload is JsonElement je && je.TryGetProperty("message", out var m))
                {
                    errStr = m.GetString();
                }
            }
            catch { }
            throw new ApiException($"Agent/Relay error: {errStr ?? "Unknown error"}");
        }

        try
        {
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);
        }
        catch { }

        return resp;
    }

    public void Dispose() => _http.Dispose();
}

public sealed class ApiException : Exception
{
    public ApiException(string message) : base(message) { }
}
