using LaptopCtl.Agent.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LaptopCtl.Agent.Tests;

public class FileTransferSecurityTests
{
    private readonly FileTransferService _service;

    public FileTransferSecurityTests()
    {
        _service = new FileTransferService(NullLogger<FileTransferService>.Instance);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ListDirectory_EmptyOrWhitespacePath_ThrowsArgumentException(string path)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.ListDirectoryAsync(path, CancellationToken.None));
    }

    [Theory]
    [InlineData(@"\\.\PhysicalDrive0")]
    [InlineData(@"\\.\COM1")]
    [InlineData(@"\\?\C:\Windows\System32")]
    public async Task ListDirectory_PathOutsideAllowedRoots_ThrowsUnauthorizedAccessException(string path)
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.ListDirectoryAsync(path, CancellationToken.None));
    }

    [Fact]
    public async Task ListDirectory_AllowedRootPath_DoesNotThrowSecurityException()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!Directory.Exists(userProfile)) return;

        // Should successfully execute without throwing UnauthorizedAccessException
        var result = await _service.ListDirectoryAsync(userProfile, CancellationToken.None);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ListDirectory_DrivesKeyword_ReturnsSystemDrives()
    {
        var result = await _service.ListDirectoryAsync("drives", CancellationToken.None);
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }
}
