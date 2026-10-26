using System;
using System.Collections.Generic;

namespace LaptopCtl.Shared.Models;

/// <summary>Represents an enrolled device's identity and status.</summary>
public record DeviceInfo
{
    public string DeviceId { get; init; } = string.Empty;
    public string DeviceName { get; init; } = string.Empty;
    public string OwnerUserId { get; init; } = string.Empty;
    public string AgentVersion { get; init; } = string.Empty;
    public string OsVersion { get; init; } = string.Empty;
    public string OsArchitecture { get; init; } = string.Empty;
    public bool IsOnline { get; init; }
    public DateTimeOffset LastSeen { get; init; }
    public DateTimeOffset EnrolledAt { get; init; }
    public DeviceEnrollmentStatus EnrollmentStatus { get; init; }
}

public enum DeviceEnrollmentStatus
{
    Pending,
    Active,
    Revoked
}

/// <summary>System information returned by the agent.</summary>
public record SystemInfo
{
    public string ComputerName { get; init; } = string.Empty;
    public string WindowsVersion { get; init; } = string.Empty;
    public string OsArchitecture { get; init; } = string.Empty;
    public string AgentVersion { get; init; } = string.Empty;
    public string? CpuModel { get; init; }
    public long? TotalMemoryBytes { get; init; }
    public DateTimeOffset LastConnection { get; init; }
}

/// <summary>A single process entry in the remote process listing.</summary>
public record ProcessEntry
{
    public int ProcessId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public long WorkingSetBytes { get; init; }
    public DateTimeOffset? StartTime { get; init; }
}

/// <summary>Storage information for a single drive.</summary>
public record DriveInfo
{
    public string Name { get; init; } = string.Empty;
    public string? VolumeLabel { get; init; }
    public string DriveFormat { get; init; } = string.Empty;
    public long TotalBytes { get; init; }
    public long AvailableBytes { get; init; }
}

/// <summary>Network interface summary.</summary>
public record NetworkInterfaceInfo
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public List<string> Addresses { get; init; } = new();
}
