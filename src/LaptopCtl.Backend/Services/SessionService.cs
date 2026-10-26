using LaptopCtl.Backend.Data;
using LaptopCtl.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LaptopCtl.Backend.Services;

public sealed class SessionService
{
    private readonly AppDbContext _db;
    private readonly ILogger<SessionService> _log;
    private readonly AuditService _audit;

    public SessionService(AppDbContext db, ILogger<SessionService> log, AuditService audit)
    {
        _db    = db;
        _log   = log;
        _audit = audit;
    }

    public async Task<SessionInfo> CreateSessionAsync(
        int controllerUserId, string deviceId, int deviceOwnerId, CancellationToken ct = default)
    {
        // Verify device belongs to the requesting user
        var device = await _db.Devices
            .FirstOrDefaultAsync(d =>
                d.DeviceId == deviceId &&
                d.OwnerId == controllerUserId &&
                d.EnrollmentStatus == "Active", ct);

        if (device is null)
            throw new UnauthorizedAccessException("Device not found or not owned by this user.");

        var session = new SessionEntity
        {
            SessionId       = Guid.NewGuid().ToString("N"),
            DeviceId        = device.Id,
            ControllerUserId = controllerUserId,
            IsActive        = true
        };

        _db.Sessions.Add(session);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Session created {SessionId} for device {DeviceId}", session.SessionId, deviceId);
        await _audit.LogAsync(controllerUserId, "session.created",
            $"Session started for device '{device.DeviceName}'",
            true, deviceId: deviceId, ct: ct);

        var controller = await _db.Users.FindAsync(new object[] { controllerUserId }, ct);
        return new SessionInfo
        {
            SessionId       = session.SessionId,
            DeviceId        = deviceId,
            ControllerUserId = controllerUserId.ToString(),
            ControllerEmail  = controller?.Email ?? string.Empty,
            StartedAt        = session.StartedAt,
            IsActive         = true
        };
    }

    public async Task<bool> TerminateSessionAsync(
        int userId, string sessionId, CancellationToken ct = default)
    {
        var session = await _db.Sessions
            .Include(s => s.Device)
            .FirstOrDefaultAsync(s =>
                s.SessionId == sessionId &&
                s.IsActive &&
                (s.ControllerUserId == userId || s.Device.OwnerId == userId), ct);

        if (session is null) return false;

        session.IsActive = false;
        session.EndedAt  = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Session terminated: {SessionId}", sessionId);
        await _audit.LogAsync(userId, "session.terminated",
            $"Session {sessionId} terminated", true, ct: ct);

        return true;
    }

    public async Task<List<SessionInfo>> ListActiveSessionsAsync(int userId, CancellationToken ct = default)
    {
        return await _db.Sessions
            .Include(s => s.Device)
            .Where(s => s.Device.OwnerId == userId && s.IsActive)
            .OrderByDescending(s => s.Id)
            .Select(s => new SessionInfo
            {
                SessionId        = s.SessionId,
                DeviceId         = s.Device.DeviceId,
                ControllerUserId = s.ControllerUserId.ToString(),
                StartedAt        = s.StartedAt,
                EndedAt          = s.EndedAt,
                IsActive         = s.IsActive
            })
            .ToListAsync(ct);
    }
}
