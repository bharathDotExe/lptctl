using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using LaptopCtl.Backend.Data;
using LaptopCtl.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using BCrypt.Net;

namespace LaptopCtl.Backend.Services;

public sealed class AuthService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<AuthService> _log;
    private readonly AuditService _audit;

    public AuthService(AppDbContext db, IConfiguration config,
        ILogger<AuthService> log, AuditService audit)
    {
        _db    = db;
        _config = config;
        _log   = log;
        _audit = audit;
    }

    public async Task<(bool Success, string? Error)> RegisterAsync(
        string email, string password, CancellationToken ct = default)
    {
        if (await _db.Users.AnyAsync(u => u.Email == email.ToLower(), ct))
            return (false, "Email already registered");

        if (password.Length < 12)
            return (false, "Password must be at least 12 characters");

        var user = new UserEntity
        {
            Email        = email.ToLower(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12)
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("User registered: {Email}", email);
        return (true, null);
    }

    public async Task<LoginResponse?> LoginAsync(
        string email, string password, string? ipAddress, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(
            u => u.Email == email.ToLower() && u.IsActive, ct);

        bool success = user is not null && BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);

        await _audit.LogAsync(
            ownerId: user?.Id ?? 0,
            eventType: "auth.login",
            description: $"Login attempt for {email}",
            success: success,
            ipAddress: ipAddress,
            ct: ct);

        if (!success || user is null)
        {
            _log.LogWarning("Failed login for {Email} from {IP}", email, ipAddress);
            return null;
        }

        var (access, refresh) = await GenerateTokensAsync(user, ct);
        _log.LogInformation("Successful login for {Email}", user.Email);
        return new LoginResponse
        {
            AccessToken  = access.Token,
            RefreshToken = refresh,
            ExpiresAt    = access.ExpiresAt,
            UserId       = user.Id.ToString(),
            Email        = user.Email
        };
    }

    public async Task<LoginResponse?> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var existing = await _db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == refreshToken && !t.IsRevoked, ct);

        if (existing is null || existing.ExpiresAt < DateTimeOffset.UtcNow)
            return null;

        // Rotate the refresh token
        existing.IsRevoked = true;
        _db.RefreshTokens.Update(existing);

        var (access, newRefresh) = await GenerateTokensAsync(existing.User, ct);
        return new LoginResponse
        {
            AccessToken  = access.Token,
            RefreshToken = newRefresh,
            ExpiresAt    = access.ExpiresAt,
            UserId       = existing.User.Id.ToString(),
            Email        = existing.User.Email
        };
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        var token = await _db.RefreshTokens
            .FirstOrDefaultAsync(t => t.Token == refreshToken, ct);
        if (token is null) return;
        token.IsRevoked = true;
        await _db.SaveChangesAsync(ct);
    }

    private async Task<((string Token, DateTimeOffset ExpiresAt) Access, string Refresh)>
        GenerateTokensAsync(UserEntity user, CancellationToken ct)
    {
        var jwtKey    = _config["Jwt:Key"]!;
        var issuer    = _config["Jwt:Issuer"]!;
        var audience  = _config["Jwt:Audience"]!;
        var expMins   = int.Parse(_config["Jwt:ExpiryMinutes"] ?? "60");
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(expMins);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub,   user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString())
        };

        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer:   issuer,
            audience: audience,
            claims:   claims,
            expires:  expiresAt.UtcDateTime,
            signingCredentials: creds);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

        // Generate opaque refresh token
        var refreshBytes = RandomNumberGenerator.GetBytes(64);
        var refreshToken = Convert.ToBase64String(refreshBytes);

        _db.RefreshTokens.Add(new RefreshTokenEntity
        {
            UserId    = user.Id,
            Token     = refreshToken,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
        });
        await _db.SaveChangesAsync(ct);

        return ((accessToken, expiresAt), refreshToken);
    }
}
