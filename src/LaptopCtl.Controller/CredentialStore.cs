using System.Reflection;
using System.Text.Json;

namespace LaptopCtl.Controller;

/// <summary>
/// Stores controller credentials securely in the local user profile.
/// Tokens are not cached indefinitely — they expire per the JWT lifetime.
/// </summary>
public sealed class CredentialStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LaptopCtl", "Controller", "credentials.json");

    private ControllerCredentials? _cached;

    public string? AccessToken  => Load()?.AccessToken;
    public string? RefreshToken => Load()?.RefreshToken;
    public string? BackendUrl   => Load()?.BackendUrl;
    public string? Email        => Load()?.Email;
    public bool    IsLoggedIn   => Load()?.IsLoggedIn ?? false;

    public void Save(string backendUrl, string email, string accessToken,
        string refreshToken, DateTimeOffset expiresAt)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);

        var creds = new ControllerCredentials
        {
            BackendUrl    = backendUrl,
            Email         = email,
            AccessToken   = accessToken,
            RefreshToken  = refreshToken,
            ExpiresAt     = expiresAt
        };

        File.WriteAllText(StorePath, JsonSerializer.Serialize(creds,
            new JsonSerializerOptions { WriteIndented = true }));
        _cached = creds;
    }

    public void Clear()
    {
        if (File.Exists(StorePath)) File.Delete(StorePath);
        _cached = null;
    }

    private ControllerCredentials? Load()
    {
        if (_cached is not null) return _cached;
        if (!File.Exists(StorePath)) return null;

        try
        {
            var json = File.ReadAllText(StorePath);
            _cached  = JsonSerializer.Deserialize<ControllerCredentials>(json);

            // If the access token is expired, return null to trigger re-auth
            if (_cached?.ExpiresAt < DateTimeOffset.UtcNow.AddMinutes(-1))
            {
                _cached = _cached with { AccessToken = string.Empty };
            }

            return _cached;
        }
        catch
        {
            return null;
        }
    }
}

public sealed record ControllerCredentials
{
    public string BackendUrl   { get; init; } = string.Empty;
    public string Email        { get; init; } = string.Empty;
    public string AccessToken  { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; init; }

    public bool IsLoggedIn => !string.IsNullOrEmpty(AccessToken)
                              && ExpiresAt > DateTimeOffset.UtcNow;
}

public static class ControllerVersion
{
    public static readonly string Current =
        System.Reflection.Assembly
            .GetExecutingAssembly()
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
}
