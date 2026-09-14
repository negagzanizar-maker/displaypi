using System.Security.Claims;

using DisplayControl.Api.Security;
using DisplayControl.Application.Security;
using DisplayControl.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DisplayControl.Api.Controllers;

[ApiController]
[Route("api/v1/auth/sessions")]
[Authorize(Policy = AuthorizationPolicies.FullSession)]
public sealed class UserSessionsController(
    DisplayControlDbContext dbContext,
    ISecureTokenService secureTokenService,
    TenantSecurityAuditService securityAudit,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserSessionResponse>>> List(CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var digest = secureTokenService.ComputeDigest(User.FindFirstValue(SessionClaimTypes.SessionKey)!);
        var now = timeProvider.GetUtcNow();
        var sessions = await dbContext.UserSessions.AsNoTracking()
            .Where(session => session.UserId == userId && session.RevokedAtUtc == null &&
                session.AbsoluteExpiresAtUtc > now && session.IdleExpiresAtUtc > now)
            .OrderByDescending(session => session.CreatedAtUtc)
            .Select(session => new UserSessionResponse(session.Id, session.CreatedAtUtc,
                session.LastSeenAtUtc, session.AbsoluteExpiresAtUtc, session.SessionKeyDigest == digest))
            .ToListAsync(cancellationToken);
        return Ok(sessions);
    }

    [HttpPost("{id:guid}/revoke")]
    [Authorize(Policy = AuthorizationPolicies.RecentMfaSession)]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var session = await dbContext.UserSessions.SingleOrDefaultAsync(
            value => value.Id == id && value.UserId == userId, cancellationToken);
        if (session is null)
        {
            return NotFound();
        }

        if (session.RevokedAtUtc is null)
        {
            session.Revoke("user_session_revoked", timeProvider.GetUtcNow());
            if (Guid.TryParse(User.FindFirstValue(SessionClaimTypes.TenantId), out var tenantId))
            {
                securityAudit.Add(tenantId, userId, "identity.session.revoked", "session", session.Id, "success", null);
            }
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "The session changed. Refresh the session list and retry."
                });
            }
        }

        var digest = secureTokenService.ComputeDigest(User.FindFirstValue(SessionClaimTypes.SessionKey)!);
        if (session.SessionKeyDigest.AsSpan().SequenceEqual(digest))
        {
            await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        }

        return NoContent();
    }
}

public sealed record UserSessionResponse(Guid Id, DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastSeenAtUtc, DateTimeOffset AbsoluteExpiresAtUtc, bool IsCurrent);
