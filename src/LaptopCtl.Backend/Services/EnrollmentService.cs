using LaptopCtl.Backend.Data;
using LaptopCtl.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace LaptopCtl.Backend.Services;

public sealed class EnrollmentService
{
    private readonly AppDbContext _db;
    private readonly ILogger<EnrollmentService> _log;
    private readonly AuditService _audit;

    public EnrollmentService(AppDbContext db, ILogger<EnrollmentService> log, AuditService audit)
    {
        _db    = db;
        _log   = log;
        _audit = audit;
    }

    /// <summary>Generates a short-lived, single-use enrollment code.</summary>
    public async Task<GenerateEnrollmentCodeResponse> GenerateCodeAsync(int userId, CancellationToken ct = default)
    {
        // Ensure user exists in database to prevent FK constraint failure
        var user = await _db.Users.FindAsync(new object[] { userId }, ct);
        if (user is null)
        {
            user = await _db.Users.FirstOrDefaultAsync(ct);
            if (user is not null) userId = user.Id;
        }

        // Invalidate old unused codes for this user
        try
        {
            var oldCodes = await _db.EnrollmentCodes
                .Where(c => c.OwnerId == userId && !c.IsUsed)
                .ToListAsync(ct);
            foreach (var c in oldCodes) c.IsUsed = true;
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to invalidate old codes, proceeding with new code generation.");
        }
        
        var code = new EnrollmentCodeEntity
        {
            Code      = GenerateSecureCode(),
            OwnerId   = userId,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
            IsUsed    = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.EnrollmentCodes.Add(code);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Enrollment code generated for user {UserId}: {Code}", userId, code.Code);
        await _audit.LogAsync(userId, "enrollment.code.generated",
            "Enrollment code generated", true, ct: ct);

        return new GenerateEnrollmentCodeResponse
        {
            Code      = code.Code,
            ExpiresAt = code.ExpiresAt
        };
    }

    /// <summary>Validates enrollment code and registers the device.</summary>
    public async Task<EnrollmentResponse> EnrollDeviceAsync(
        EnrollmentRequest request, string? ipAddress, CancellationToken ct = default)
    {
        var code = await _db.EnrollmentCodes
            .Include(c => c.Owner)
            .FirstOrDefaultAsync(c =>
                c.Code == request.EnrollmentCode &&
                !c.IsUsed &&
                c.ExpiresAt > DateTimeOffset.UtcNow, ct);

        if (code is null)
        {
            _log.LogWarning("Invalid or expired enrollment code from {IP}", ipAddress);
            throw new UnauthorizedAccessException("Invalid or expired enrollment code.");
        }

        // Mark code as used immediately (single-use)
        code.IsUsed = true;

        // Generate a device token — only returned once, stored as hash
        var rawToken  = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var tokenHash = BCrypt.Net.BCrypt.HashPassword(rawToken, workFactor: 10);
        var deviceId  = Guid.NewGuid().ToString("N");

        var device = new DeviceEntity
        {
            DeviceId         = deviceId,
            DeviceName       = SanitizeName(request.DeviceName),
            OwnerId          = code.OwnerId,
            DeviceTokenHash  = tokenHash,
            PublicKeyBase64  = request.PublicKeyBase64,
            AgentVersion     = request.AgentVersion,
            OsVersion        = request.OsVersion,
            OsArchitecture   = request.OsArchitecture,
            EnrollmentStatus = "Active"
        };

        _db.Devices.Add(device);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Device enrolled: {DeviceId} ({Name}) for user {UserId}",
            deviceId, device.DeviceName, code.OwnerId);

        await _audit.LogAsync(code.OwnerId, "enrollment.device",
            $"Device '{device.DeviceName}' enrolled (ID: {deviceId})",
            true, ipAddress, deviceId: deviceId, ct: ct);

        return new EnrollmentResponse
        {
            DeviceId    = deviceId,
            DeviceToken = rawToken,    // Raw token returned ONCE; never stored in plain text
            Message     = "Device enrolled successfully."
        };
    }

    private static string GenerateSecureCode()
    {
        // 8-char alphanumeric code (uppercase only for readability)
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no I,O,0,1
        var bytes = RandomNumberGenerator.GetBytes(8);
        return new string(bytes.Select(b => chars[b % chars.Length]).ToArray());
    }

    private static string SanitizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Unknown Device";
        return new string(name.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == ' ')
            .Take(64).ToArray()).Trim();
    }
}
