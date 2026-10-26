using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace LaptopCtl.Agent.Services;

/// <summary>
/// Manages the per-device RSA identity keypair and the enrolled device token.
/// The private key never leaves this machine.
/// </summary>
public sealed class AgentIdentityService
{
    private readonly ILogger<AgentIdentityService> _log;
    private IdentityData? _identity;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public AgentIdentityService(ILogger<AgentIdentityService> log)
    {
        _log = log;
    }

    public async Task<IdentityData> GetOrCreateIdentityAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_identity is not null) return _identity;

            if (File.Exists(AgentPaths.IdentityFile))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(AgentPaths.IdentityFile, ct);
                    _identity = JsonSerializer.Deserialize<IdentityData>(json);
                    if (_identity is not null)
                    {
                        _log.LogInformation("Loaded existing device identity {DeviceId}", _identity.DeviceId);
                        return _identity;
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Failed to load existing identity, generating new one");
                }
            }

            _identity = await CreateNewIdentityAsync(ct);
            return _identity;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IdentityData> CreateNewIdentityAsync(CancellationToken ct)
    {
        _log.LogInformation("Generating new device identity keypair");
        using var rsa = RSA.Create(2048);
        var publicKeyDer = rsa.ExportSubjectPublicKeyInfo();
        var privateKeyDer = rsa.ExportPkcs8PrivateKey();

        var data = new IdentityData
        {
            DeviceId = string.Empty, // set after enrollment
            PublicKeyBase64 = Convert.ToBase64String(publicKeyDer),
            PrivateKeyBase64 = Convert.ToBase64String(privateKeyDer),
            DeviceToken = string.Empty,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await PersistIdentityAsync(data, ct);
        return data;
    }

    public async Task UpdateAfterEnrollmentAsync(string deviceId, string deviceToken, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_identity is null) throw new InvalidOperationException("Identity not initialized");
            _identity = _identity with { DeviceId = deviceId, DeviceToken = deviceToken };
            await PersistIdentityAsync(_identity, ct);
            _log.LogInformation("Device enrolled as {DeviceId}", deviceId);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static async Task PersistIdentityAsync(IdentityData data, CancellationToken ct)
    {
        var tempFile = AgentPaths.IdentityFile + ".tmp";
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(tempFile, json, ct);
        File.Move(tempFile, AgentPaths.IdentityFile, overwrite: true);
    }

    public bool IsEnrolled => _identity?.IsEnrolled ?? false;
}

public sealed record IdentityData
{
    public string DeviceId { get; init; } = string.Empty;
    public string PublicKeyBase64 { get; init; } = string.Empty;
    /// <summary>PKCS#8 private key – never sent to server.</summary>
    public string PrivateKeyBase64 { get; init; } = string.Empty;
    public string DeviceToken { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }

    public bool IsEnrolled => !string.IsNullOrEmpty(DeviceId) && !string.IsNullOrEmpty(DeviceToken);
}
