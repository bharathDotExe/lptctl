using System;
using System.Collections.Generic;

namespace LaptopCtl.Backend.Data;

// ─── User ─────────────────────────────────────────────────────────────────────

public sealed class UserEntity
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    /// <summary>BCrypt hash — never stored in plain text.</summary>
    public string PasswordHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsActive { get; set; } = true;

    public List<DeviceEntity>       Devices       { get; set; } = new();
    public List<RefreshTokenEntity> RefreshTokens { get; set; } = new();
}

// ─── Device ───────────────────────────────────────────────────────────────────

public sealed class DeviceEntity
{
    public int Id { get; set; }
    /// <summary>Public, URL-safe device identifier (UUID).</summary>
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public int OwnerId { get; set; }
    public UserEntity Owner { get; set; } = null!;

    /// <summary>BCrypt hash of the device token — raw token is only sent once at enrollment.</summary>
    public string DeviceTokenHash { get; set; } = string.Empty;
    /// <summary>RSA public key (Base64 DER) for request signature verification.</summary>
    public string PublicKeyBase64 { get; set; } = string.Empty;

    public string AgentVersion { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public string OsArchitecture { get; set; } = string.Empty;
    public string EnrollmentStatus { get; set; } = "Active"; // Active | Revoked
    public DateTimeOffset EnrolledAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSeen { get; set; }
    public bool IsOnline { get; set; }

    public List<SessionEntity> Sessions { get; set; } = new();
}

// ─── Enrollment code ──────────────────────────────────────────────────────────

public sealed class EnrollmentCodeEntity
{
    public int Id { get; set; }
    /// <summary>Short-lived, single-use enrollment code.</summary>
    public string Code { get; set; } = string.Empty;
    public int OwnerId { get; set; }
    public UserEntity Owner { get; set; } = null!;
    public DateTimeOffset ExpiresAt { get; set; }
    public bool IsUsed { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

// ─── Session ──────────────────────────────────────────────────────────────────

public sealed class SessionEntity
{
    public int Id { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public int DeviceId { get; set; }
    public DeviceEntity Device { get; set; } = null!;
    public int ControllerUserId { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedAt { get; set; }
    public bool IsActive { get; set; } = true;
}

// ─── Audit event ──────────────────────────────────────────────────────────────

public sealed class AuditEventEntity
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public string? DeviceId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public bool Success { get; set; }
    /// <summary>IP address — stored for security, not shown publicly.</summary>
    public string? IpAddress { get; set; }
}

// ─── Refresh token ─────────────────────────────────────────────────────────────

public sealed class RefreshTokenEntity
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public UserEntity User { get; set; } = null!;
    public string Token { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
