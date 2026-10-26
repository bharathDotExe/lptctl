using LaptopCtl.Backend.Services;
using LaptopCtl.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace LaptopCtl.Backend.Controllers;

[Authorize]
[ApiController]
[Route("api")]
public sealed class MiscController : ControllerBase
{
    private readonly AuditService _audit;
    private readonly SessionService _sessions;

    public MiscController(AuditService audit, SessionService sessions)
    {
        _audit    = audit;
        _sessions = sessions;
    }

    // ─── Audit events ─────────────────────────────────────────────────────────

    [HttpGet("audit")]
    public async Task<IActionResult> GetAuditEvents(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var userId = GetUserId();
        var events = await _audit.GetRecentAsync(userId, page, pageSize, ct);
        return Ok(events);
    }

    // ─── Sessions ─────────────────────────────────────────────────────────────

    [HttpGet("sessions")]
    public async Task<IActionResult> ListSessions(CancellationToken ct)
    {
        var userId   = GetUserId();
        var sessions = await _sessions.ListActiveSessionsAsync(userId, ct);
        return Ok(sessions);
    }

    [HttpDelete("sessions/{sessionId}")]
    public async Task<IActionResult> TerminateSession(string sessionId, CancellationToken ct)
    {
        var userId = GetUserId();
        var ok     = await _sessions.TerminateSessionAsync(userId, sessionId, ct);
        if (!ok) return NotFound(new ErrorResponse { Code = "SESSION_NOT_FOUND", Message = "Session not found." });
        return Ok(new { Message = "Session terminated." });
    }

    // ─── Agent update metadata ─────────────────────────────────────────────────

    [AllowAnonymous]
    [HttpGet("agent/latest")]
    public IActionResult GetLatestAgent()
    {
        // In production this would read from the database / release manifest.
        return Ok(new AgentReleaseInfo
        {
            Version      = "1.0.0",
            DownloadUrl  = "https://github.com/YOUR_ORG/laptopctl/releases/download/v1.0.0/LaptopCtl-Agent-win-x64.zip",
            Sha256       = "PLACEHOLDER_REPLACE_AFTER_REAL_BUILD",
            ReleasedAt   = DateTimeOffset.UtcNow,
            ReleaseNotes = "Initial release."
        });
    }

    private int GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
        return int.TryParse(claim?.Value, out var id) ? id : 0;
    }
}
