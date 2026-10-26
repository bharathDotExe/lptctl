using LaptopCtl.Backend.Data;
using LaptopCtl.Backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LaptopCtl.Backend.Tests;

public class DeviceServiceTests
{
    private static AppDbContext CreateTestDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task RevokeDeviceAsync_RevokesActiveDevice()
    {
        using var db = CreateTestDb();
        var audit = new AuditService(db, NullLogger<AuditService>.Instance);
        var service = new DeviceService(db, NullLogger<DeviceService>.Instance, audit);

        var device = new DeviceEntity
        {
            DeviceId = "dev-12345",
            DeviceName = "ENGINEERING-PC",
            OwnerId = 1,
            EnrollmentStatus = "Active",
            IsOnline = true,
            EnrolledAt = DateTimeOffset.UtcNow,
            LastSeen = DateTimeOffset.UtcNow,
            DeviceTokenHash = "hash"
        };
        db.Devices.Add(device);
        await db.SaveChangesAsync();

        var success = await service.RevokeDeviceAsync(userId: 1, deviceId: "dev-12345", ipAddress: "127.0.0.1");

        Assert.True(success);
        var updated = await db.Devices.FirstAsync(d => d.DeviceId == "dev-12345");
        Assert.Equal("Revoked", updated.EnrollmentStatus);
        Assert.False(updated.IsOnline);
    }

    [Fact]
    public async Task ListDevicesAsync_ReturnsOnlyDevicesForUser()
    {
        using var db = CreateTestDb();
        var audit = new AuditService(db, NullLogger<AuditService>.Instance);
        var service = new DeviceService(db, NullLogger<DeviceService>.Instance, audit);

        db.Devices.AddRange(
            new DeviceEntity { DeviceId = "dev-1", DeviceName = "PC-1", OwnerId = 1, EnrollmentStatus = "Active", DeviceTokenHash = "h1" },
            new DeviceEntity { DeviceId = "dev-2", DeviceName = "PC-2", OwnerId = 1, EnrollmentStatus = "Active", DeviceTokenHash = "h2" },
            new DeviceEntity { DeviceId = "dev-3", DeviceName = "PC-3", OwnerId = 2, EnrollmentStatus = "Active", DeviceTokenHash = "h3" }
        );
        await db.SaveChangesAsync();

        var result = await service.ListDevicesAsync(userId: 1, page: 1, pageSize: 10);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, item => Assert.True(item.DeviceId is "dev-1" or "dev-2"));
    }
}
