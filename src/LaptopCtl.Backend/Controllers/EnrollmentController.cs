using LaptopCtl.Backend.Services;
using LaptopCtl.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace LaptopCtl.Backend.Controllers;

[Authorize]
[ApiController]
[Route("api/enrollment")]
public sealed class EnrollmentController : ControllerBase
{
    private readonly EnrollmentService _enrollment;

    public EnrollmentController(EnrollmentService enrollment) => _enrollment = enrollment;

    /// <summary>
    /// Authenticated users call this to generate an enrollment code
    /// that can be passed to the installer on a target computer.
    /// </summary>
    [HttpPost("code")]
    public async Task<IActionResult> GenerateCode(CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();

        var code = await _enrollment.GenerateCodeAsync(userId, ct);
        return Ok(code);
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
    }

    private int GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
        return int.TryParse(claim?.Value, out var id) ? id : 0;
    }
}
