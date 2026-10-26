using LaptopCtl.Controller;
using Xunit;

namespace LaptopCtl.Controller.Tests;

public class CredentialStoreTests
{
    [Fact]
    public void ControllerCredentials_ValidExpiry_IsLoggedInReturnsTrue()
    {
        var creds = new ControllerCredentials
        {
            AccessToken = "valid-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };

        Assert.True(creds.IsLoggedIn);
    }

    [Fact]
    public void ControllerCredentials_Expired_IsLoggedInReturnsFalse()
    {
        var creds = new ControllerCredentials
        {
            AccessToken = "expired-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };

        Assert.False(creds.IsLoggedIn);
    }

    [Fact]
    public void ControllerCredentials_EmptyToken_IsLoggedInReturnsFalse()
    {
        var creds = new ControllerCredentials
        {
            AccessToken = "",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };

        Assert.False(creds.IsLoggedIn);
    }
}
