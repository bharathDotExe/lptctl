using LaptopCtl.Backend.Data;
using LaptopCtl.Backend.Services;
using LaptopCtl.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace LaptopCtl.Backend.Controllers;

[Authorize]
[ApiController]
[Route("api/enrollment")]
public sealed class EnrollmentController : ControllerBase
{
    private readonly EnrollmentService _enrollment;
    private readonly AppDbContext _db;
    private readonly ILogger<EnrollmentController> _log;

    public EnrollmentController(EnrollmentService enrollment, AppDbContext db, ILogger<EnrollmentController> log)
    {
        _enrollment = enrollment;
        _db         = db;
        _log        = log;
    }

    /// <summary>
    /// Authenticated users call this to generate an enrollment code
    /// that can be passed to the installer on a target computer.
    /// </summary>
    [HttpPost("code")]
    public async Task<IActionResult> GenerateCode(CancellationToken ct)
    {
        try
        {
            var userId = await GetUserIdAsync(ct);
            var code = await _enrollment.GenerateCodeAsync(userId, ct);
            return Ok(code);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to generate enrollment code");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse { Code = "CODE_GEN_FAILED", Message = ex.Message });
        }
    }

    /// <summary>
    /// Called by the agent installer with the enrollment code.
    /// Returns a device token ONCE — it is never stored in plain text.
    /// </summary>
    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> Enroll([FromBody] EnrollmentRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.EnrollmentCode))
            return BadRequest(new ErrorResponse { Code = "INVALID_REQUEST", Message = "EnrollmentCode is required." });

        if (string.IsNullOrWhiteSpace(request.PublicKeyBase64))
            return BadRequest(new ErrorResponse { Code = "INVALID_REQUEST", Message = "PublicKeyBase64 is required." });

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        try
        {
            var result = await _enrollment.EnrollDeviceAsync(request, ip, ct);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new ErrorResponse { Code = "INVALID_CODE", Message = ex.Message });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Device enrollment failed");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse { Code = "ENROLLMENT_FAILED", Message = ex.Message });
        }
    }

    private async Task<int> GetUserIdAsync(CancellationToken ct)
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier) 
                 ?? User.FindFirst("sub")
                 ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub);
        if (int.TryParse(claim?.Value, out var id) && id > 0)
            return id;

        var email = User.FindFirst(ClaimTypes.Email)?.Value 
                 ?? User.FindFirst("email")?.Value;
        if (!string.IsNullOrEmpty(email))
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email.ToLower(), ct);
            if (user is not null) return user.Id;
        }

        // Single-admin fallback
        var admin = await _db.Users.FirstOrDefaultAsync(ct);
        return admin?.Id ?? 1;
    }
}
