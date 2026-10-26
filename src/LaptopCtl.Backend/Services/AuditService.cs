using LaptopCtl.Backend.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LaptopCtl.Backend.Services;

/// <summary>Records audit events. Never stores raw passwords or tokens.</summary>
public sealed class AuditService
{
    private readonly AppDbContext _db;
    private readonly ILogger<AuditService> _log;

    public AuditService(AppDbContext db, ILogger<AuditService> log)
    {
        _db  = db;
        _log = log;
    }

    public async Task LogAsync(
        int ownerId,
        string eventType,
        string description,
        bool success,
        string? ipAddress = null,
        string? deviceId  = null,
        CancellationToken ct = default)
    {
        try
        {
            _db.AuditEvents.Add(new AuditEventEntity
            {
                OwnerId     = ownerId,
                DeviceId    = deviceId,
                EventType   = eventType,
                Description = description,
                Success     = success,
                IpAddress   = ipAddress,
                OccurredAt  = DateTimeOffset.UtcNow
            });
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Audit failures must not crash the request
            _log.LogError(ex, "Failed to write audit event {EventType}", eventType);
        }
    }

    public async Task<List<LaptopCtl.Shared.Models.AuditEvent>> GetRecentAsync(
        int userId, int page, int pageSize, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page     = Math.Max(1, page);

        return await _db.AuditEvents
            .Where(e => e.OwnerId == userId)
            .OrderByDescending(e => e.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new LaptopCtl.Shared.Models.AuditEvent
            {
                EventId     = e.Id.ToString(),
                UserId      = e.OwnerId.ToString(),
                DeviceId    = e.DeviceId,
                EventType   = e.EventType,
                Description = e.Description,
                OccurredAt  = e.OccurredAt,
                Success     = e.Success,
                // IP address redacted for privacy; shown only in security review
                IpAddress   = null
            })
            .ToListAsync(ct);
    }
}
