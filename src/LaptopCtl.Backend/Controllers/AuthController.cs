using LaptopCtl.Backend.Services;
using LaptopCtl.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LaptopCtl.Backend.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly AuthService _auth;
    private readonly ILogger<AuthController> _log;

    public AuthController(AuthService auth, ILogger<AuthController> log)
    {
        _auth = auth;
        _log  = log;
    }

    [HttpPost("register")]
    public IActionResult Register([FromBody] RegisterRequest req)
    {
        return StatusCode(StatusCodes.Status403Forbidden,
            new ErrorResponse { Code = "REGISTRATION_DISABLED", Message = "Registration is closed. Only one administrator account is permitted. Use your master admin credentials to sign in." });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _auth.LoginAsync(req.Email, req.Password, ip, ct);

        if (result is null)
            return Unauthorized(new ErrorResponse { Code = "INVALID_CREDENTIALS", Message = "Invalid email or password." });

        return Ok(result);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest req, CancellationToken ct)
    {
        var result = await _auth.RefreshAsync(req.RefreshToken, ct);
        if (result is null)
            return Unauthorized(new ErrorResponse { Code = "INVALID_REFRESH_TOKEN", Message = "Refresh token is invalid or expired." });

        return Ok(result);
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest req, CancellationToken ct)
    {
        await _auth.LogoutAsync(req.RefreshToken, ct);
        return Ok(new { Message = "Logged out." });
    }
}

// Simple register request not in shared (server-only)
public sealed record RegisterRequest
{
    public string Email    { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}
