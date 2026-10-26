using LaptopCtl.Backend.Data;
using LaptopCtl.Backend.Services;
using LaptopCtl.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LaptopCtl.Backend.Tests;

public class EnrollmentServiceTests
{
    private static AppDbContext CreateTestDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task GenerateCodeAsync_CreatesEightCharUppercaseCode()
    {
        using var db = CreateTestDb();
        var audit = new AuditService(db, NullLogger<AuditService>.Instance);
        var service = new EnrollmentService(db, NullLogger<EnrollmentService>.Instance, audit);

        var result = await service.GenerateCodeAsync(userId: 1);

        Assert.NotNull(result);
        Assert.Equal(8, result.Code.Length);
        Assert.Matches("^[A-Z0-9]{8}$", result.Code);
        Assert.True(result.ExpiresAt > DateTimeOffset.UtcNow);

        var entity = await db.EnrollmentCodes.FirstOrDefaultAsync(c => c.Code == result.Code);
        Assert.NotNull(entity);
        Assert.False(entity.IsUsed);
    }

    [Fact]
    public async Task EnrollDeviceAsync_ValidCode_EnrollsDeviceAndReturnsToken()
    {
        using var db = CreateTestDb();
        var audit = new AuditService(db, NullLogger<AuditService>.Instance);
        var service = new EnrollmentService(db, NullLogger<EnrollmentService>.Instance, audit);

        // Seed a valid user
        db.Users.Add(new UserEntity
        {
            Id = 1,
            Email = "admin@example.com",
            PasswordHash = "hashed",
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var codeResult = await service.GenerateCodeAsync(userId: 1);

        var request = new EnrollmentRequest
        {
            EnrollmentCode = codeResult.Code,
            DeviceName = "TEST-LAPTOP-01",
            AgentVersion = "1.0.0",
            OsVersion = "Windows 11 Pro",
            OsArchitecture = "X64",
            PublicKeyBase64 = Convert.ToBase64String(new byte[32])
        };

        var response = await service.EnrollDeviceAsync(request, "192.168.1.100");

        Assert.NotNull(response);
        Assert.False(string.IsNullOrWhiteSpace(response.DeviceId));
        Assert.False(string.IsNullOrWhiteSpace(response.DeviceToken));

        var enrolledDevice = await db.Devices.FirstOrDefaultAsync(d => d.DeviceId == response.DeviceId);
        Assert.NotNull(enrolledDevice);
        Assert.Equal("TEST-LAPTOP-01", enrolledDevice.DeviceName);
        Assert.Equal("Active", enrolledDevice.EnrollmentStatus);

        // Verify code was marked as used
        var usedCode = await db.EnrollmentCodes.FirstAsync(c => c.Code == codeResult.Code);
        Assert.True(usedCode.IsUsed);
    }

    [Fact]
    public async Task EnrollDeviceAsync_InvalidCode_ThrowsUnauthorizedAccessException()
    {
        using var db = CreateTestDb();
        var audit = new AuditService(db, NullLogger<AuditService>.Instance);
        var service = new EnrollmentService(db, NullLogger<EnrollmentService>.Instance, audit);

        var request = new EnrollmentRequest
        {
            EnrollmentCode = "INVALIDCODE",
            DeviceName = "TEST-LAPTOP-02",
            PublicKeyBase64 = Convert.ToBase64String(new byte[32])
        };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.EnrollDeviceAsync(request, "127.0.0.1"));
    }
}
