using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LaptopCtl.Shared.Models;

// ─── Enrollment ──────────────────────────────────────────────────────────────

public record EnrollmentRequest
{
    public string EnrollmentCode { get; init; } = string.Empty;
    public string DeviceName { get; init; } = string.Empty;
    public string AgentVersion { get; init; } = string.Empty;
    public string OsVersion { get; init; } = string.Empty;
    public string OsArchitecture { get; init; } = string.Empty;
    /// <summary>RSA public key (Base64 DER) for device identity.</summary>
    public string PublicKeyBase64 { get; init; } = string.Empty;
}

public record EnrollmentResponse
{
    public string DeviceId { get; init; } = string.Empty;
    public string DeviceToken { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

// ─── Auth ────────────────────────────────────────────────────────────────────

public record LoginRequest
{
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}

public record LoginResponse
{
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; init; }
    public string UserId { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
}

public record RefreshRequest
{
    public string RefreshToken { get; init; } = string.Empty;
}

// ─── WebSocket message envelope ──────────────────────────────────────────────

public record WsMessage
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("requestId")]
    public string RequestId { get; init; } = Guid.NewGuid().ToString();

    [JsonPropertyName("payload")]
    public object? Payload { get; init; }

    [JsonPropertyName("issuedAt")]
    public DateTimeOffset IssuedAt { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset ExpiresAt { get; init; } = DateTimeOffset.UtcNow.AddMinutes(2);
}

public static class WsMessageTypes
{
    // Sent by agent → backend
    public const string AgentHello        = "agent.hello";
    public const string AgentPing         = "agent.ping";
    public const string DiagnosticResult  = "diagnostic.result";
    public const string DiagnosticError   = "diagnostic.error";
    public const string CommandResult     = "command.result";
    public const string FileTransferChunk = "file.chunk";
    public const string FileTransferDone  = "file.done";
    public const string FileTransferError = "file.error";

    // Sent by backend → agent
    public const string RequestDiagnostic = "request.diagnostic";
    public const string RequestCommand    = "request.command";
    public const string RequestFileList   = "request.file.list";
    public const string RequestFileGet    = "request.file.get";
    public const string RequestFilePut    = "request.file.put";
    public const string SessionTerminate  = "session.terminate";
    public const string AgentUpdate       = "agent.update";
    public const string BackendPong       = "backend.pong";
}

// ─── Diagnostic request payload ──────────────────────────────────────────────

public record DiagnosticRequest
{
    public string DiagnosticType { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
}

public static class DiagnosticTypes
{
    public const string SystemInfo  = "system.info";
    public const string Processes   = "processes";
    public const string Disks       = "disks";
    public const string Network     = "network";
}

// ─── Command execution payload ────────────────────────────────────────────────

public record CommandExecutionRequest
{
    public string Command { get; init; } = string.Empty;
    public string? Arguments { get; init; }
    public int TimeoutSeconds { get; init; } = 30;
}

public record CommandExecutionResult
{
    public int ExitCode { get; init; }
    public string Stdout { get; init; } = string.Empty;
    public string Stderr { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
}

// ─── Session ─────────────────────────────────────────────────────────────────

public record SessionInfo
{
    public string SessionId { get; init; } = string.Empty;
    public string DeviceId { get; init; } = string.Empty;
    public string ControllerUserId { get; init; } = string.Empty;
    public string ControllerEmail { get; init; } = string.Empty;
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public bool IsActive { get; init; }
}

public record CreateSessionRequest
{
    public string DeviceId { get; init; } = string.Empty;
}

// ─── Audit events ─────────────────────────────────────────────────────────────

public record AuditEvent
{
    public string EventId { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string? DeviceId { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public DateTimeOffset OccurredAt { get; init; }
    public bool Success { get; init; }
    public string? IpAddress { get; init; }
}

// ─── Paged responses ─────────────────────────────────────────────────────────

public record PagedResponse<T>
{
    public List<T> Items { get; init; } = new();
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}

// ─── Error response ──────────────────────────────────────────────────────────

public record ErrorResponse
{
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public Dictionary<string, string[]>? ValidationErrors { get; init; }
}

// ─── File transfer ───────────────────────────────────────────────────────────

public record FileTransferRequest
{
    public string SessionId { get; init; } = string.Empty;
    public string RemotePath { get; init; } = string.Empty;
    public TransferDirection Direction { get; init; }
    public long? FileSizeBytes { get; init; }
    public string? Sha256 { get; init; }
}

public enum TransferDirection { Upload, Download }

public record FileListRequest
{
    public string SessionId { get; init; } = string.Empty;
    public string RemotePath { get; init; } = string.Empty;
}

public record FileEntry
{
    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public bool IsDirectory { get; init; }
    public long? SizeBytes { get; init; }
    public DateTimeOffset? LastModified { get; init; }
}

// ─── Update metadata ─────────────────────────────────────────────────────────

public record AgentReleaseInfo
{
    public string Version { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public DateTimeOffset ReleasedAt { get; init; }
    public string ReleaseNotes { get; init; } = string.Empty;
}

// ─── Enrollment code ─────────────────────────────────────────────────────────

public record GenerateEnrollmentCodeResponse
{
    public string Code { get; init; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; init; }
}
