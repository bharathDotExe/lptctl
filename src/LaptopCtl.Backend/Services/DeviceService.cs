using LaptopCtl.Backend.Data;
using LaptopCtl.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LaptopCtl.Backend.Services;

public sealed class DeviceService
{
    private readonly AppDbContext _db;
    private readonly ILogger<DeviceService> _log;
    private readonly AuditService _audit;

    public DeviceService(AppDbContext db, ILogger<DeviceService> log, AuditService audit)
    {
        _db    = db;
        _log   = log;
        _audit = audit;
    }

    public async Task<PagedResponse<DeviceInfo>> ListDevicesAsync(
        int userId, int page, int pageSize, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page     = Math.Max(1, page);

        var query = _db.Devices
            .Where(d => d.OwnerId == userId && d.EnrollmentStatus != "Revoked")
            .OrderByDescending(d => d.Id);

        var total = await query.CountAsync(ct);
        var rawEntities = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = rawEntities
            .OrderByDescending(d => d.LastSeen ?? d.EnrolledAt)
            .Select(d => ToModel(d))
            .ToList();

        return new PagedResponse<DeviceInfo>
        {
            Items     = items,
            TotalCount = total,
            Page      = page,
            PageSize  = pageSize
        };
    }

    public async Task<DeviceInfo?> GetDeviceAsync(int userId, string deviceId, CancellationToken ct = default)
    {
        var d = await _db.Devices
            .FirstOrDefaultAsync(x => x.DeviceId == deviceId && x.OwnerId == userId && x.EnrollmentStatus != "Revoked", ct);
        return d is null ? null : ToModel(d);
    }

    public async Task<bool> RevokeDeviceAsync(int userId, string deviceId,
        string? ipAddress, CancellationToken ct = default)
    {
        var device = await _db.Devices
            .FirstOrDefaultAsync(x => x.DeviceId == deviceId && x.OwnerId == userId, ct);

        if (device is null) return false;

        device.EnrollmentStatus = "Revoked";
        device.IsOnline         = false;
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Device revoked: {DeviceId} by user {UserId}", deviceId, userId);
        await _audit.LogAsync(userId, "device.revoked",
            $"Device '{device.DeviceName}' (ID: {deviceId}) revoked",
            true, ipAddress, deviceId: deviceId, ct: ct);

        return true;
    }

    public async Task UpdateHeartbeatAsync(string deviceId, string agentVersion, string? deviceName = null, string? osVersion = null, CancellationToken ct = default)
    {
        var device = await _db.Devices.FirstOrDefaultAsync(d => d.DeviceId == deviceId, ct);
        if (device is null) return;
        device.LastSeen     = DateTimeOffset.UtcNow;
        device.IsOnline     = true;
        device.AgentVersion = agentVersion;
        if (!string.IsNullOrWhiteSpace(deviceName)) device.DeviceName = deviceName;
        if (!string.IsNullOrWhiteSpace(osVersion)) device.OsVersion = osVersion;
        await _db.SaveChangesAsync(ct);
    }

    public async Task MarkOfflineAsync(string deviceId, CancellationToken ct = default)
    {
        var device = await _db.Devices.FirstOrDefaultAsync(d => d.DeviceId == deviceId, ct);
        if (device is null) return;
        device.IsOnline = false;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Validates a device token. Returns the device's owner ID if valid.</summary>
    public async Task<(bool Valid, int OwnerId)> ValidateDeviceTokenAsync(
        string deviceId, string rawToken, string? deviceName = null, string? osVersion = null, CancellationToken ct = default)
    {
        var existing = await _db.Devices
            .FirstOrDefaultAsync(d => d.DeviceId == deviceId, ct);

        if (existing is not null)
        {
            if (existing.EnrollmentStatus == "Revoked")
            {
                _log.LogWarning("Rejected connection from revoked device {DeviceId}", deviceId);
                return (false, 0);
            }

            bool valid = BCrypt.Net.BCrypt.Verify(rawToken, existing.DeviceTokenHash);
            return (valid, existing.OwnerId);
        }

        // Auto-heal: If database was restarted or reset, auto-register connecting agent for admin
        var admin = await _db.Users.FirstOrDefaultAsync(ct);
        var ownerId = admin?.Id ?? 1;

        var tokenHash = BCrypt.Net.BCrypt.HashPassword(rawToken, workFactor: 10);
        var device = new DeviceEntity
        {
            DeviceId         = deviceId,
            DeviceName       = !string.IsNullOrWhiteSpace(deviceName) ? deviceName : "Remote Computer",
            OwnerId          = ownerId,
            DeviceTokenHash  = tokenHash,
            OsVersion        = osVersion ?? "Windows",
            EnrollmentStatus = "Active",
            IsOnline         = true,
            LastSeen         = DateTimeOffset.UtcNow,
            EnrolledAt       = DateTimeOffset.UtcNow
        };
        _db.Devices.Add(device);
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Auto-healed device record {DeviceId} ({Name}) for user {UserId}", deviceId, device.DeviceName, ownerId);
        return (true, ownerId);
    }

    public async Task<bool> UnenrollDeviceAsync(
        string deviceId, string rawToken, string? ipAddress, CancellationToken ct = default)
    {
        var device = await _db.Devices.FirstOrDefaultAsync(d => d.DeviceId == deviceId, ct);
        if (device is null) return false;

        if (!string.IsNullOrEmpty(device.DeviceTokenHash) && !string.IsNullOrEmpty(rawToken))
        {
            bool valid = BCrypt.Net.BCrypt.Verify(rawToken, device.DeviceTokenHash);
            if (!valid)
            {
                _log.LogWarning("Invalid device token on unenroll for {DeviceId}", deviceId);
                return false;
            }
        }

        device.EnrollmentStatus = "Revoked";
        device.IsOnline         = false;
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Device uninstalled/unenrolled: {DeviceId}", deviceId);
        await _audit.LogAsync(device.OwnerId, "device.uninstalled",
            $"Device '{device.DeviceName}' (ID: {deviceId}) uninstalled itself",
            true, ipAddress, deviceId: deviceId, ct: ct);

        return true;
    }

    private static DeviceInfo ToModel(DeviceEntity d) => new()
    {
        DeviceId         = d.DeviceId,
        DeviceName       = d.DeviceName,
        OwnerUserId      = d.OwnerId.ToString(),
        AgentVersion     = d.AgentVersion,
        OsVersion        = d.OsVersion,
        OsArchitecture   = d.OsArchitecture,
        IsOnline         = d.IsOnline,
        LastSeen         = d.LastSeen ?? d.EnrolledAt,
        EnrolledAt       = d.EnrolledAt,
        EnrollmentStatus = d.EnrollmentStatus == "Revoked"
            ? LaptopCtl.Shared.Models.DeviceEnrollmentStatus.Revoked
            : LaptopCtl.Shared.Models.DeviceEnrollmentStatus.Active
    };
}
