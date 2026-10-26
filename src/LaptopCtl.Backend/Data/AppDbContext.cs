using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace LaptopCtl.Backend.Data;

/// <summary>EF Core DbContext for LaptopCtl backend data.</summary>
public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<UserEntity>             Users             => Set<UserEntity>();
    public DbSet<DeviceEntity>           Devices           => Set<DeviceEntity>();
    public DbSet<EnrollmentCodeEntity>   EnrollmentCodes   => Set<EnrollmentCodeEntity>();
    public DbSet<SessionEntity>          Sessions          => Set<SessionEntity>();
    public DbSet<AuditEventEntity>       AuditEvents       => Set<AuditEventEntity>();
    public DbSet<RefreshTokenEntity>     RefreshTokens     => Set<RefreshTokenEntity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Users
        b.Entity<UserEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(320).IsRequired();
            e.Property(x => x.PasswordHash).IsRequired();
        });

        // Devices
        b.Entity<DeviceEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.DeviceId).IsUnique();
            e.Property(x => x.DeviceId).HasMaxLength(64).IsRequired();
            e.Property(x => x.DeviceName).HasMaxLength(256);
            e.HasOne(x => x.Owner).WithMany(u => u.Devices)
             .HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);
        });

        // Enrollment codes
        b.Entity<EnrollmentCodeEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(64).IsRequired();
            e.HasOne(x => x.Owner).WithMany()
             .HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);
        });

        // Sessions
        b.Entity<SessionEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.SessionId).IsUnique();
            e.HasOne(x => x.Device).WithMany(d => d.Sessions)
             .HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        });

        // Audit events
        b.Entity<AuditEventEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.OwnerId, x.OccurredAt });
        });

        // Refresh tokens
        b.Entity<RefreshTokenEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Token).IsUnique();
            e.HasOne(x => x.User).WithMany(u => u.RefreshTokens)
             .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
