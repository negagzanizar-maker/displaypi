using DisplayControl.Api.Pagination;
using DisplayControl.Api.Security;
using DisplayControl.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DisplayControl.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId:guid}/audit-events")]
public sealed class AuditEventsController(DisplayControlDbContext dbContext) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.TenantAdministrator)]
    public async Task<ActionResult<IReadOnlyList<AuditEventResponse>>> List(
        Guid tenantId,
        [FromQuery] DateTimeOffset? beforeUtc = null,
        [FromQuery, System.ComponentModel.DataAnnotations.Range(1, 200)] int limit = 100,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        if (!CursorPage.TryReadOffset(cursor, out var pageOffset)) return BadRequest("Invalid cursor.");
        if (beforeUtc is { Offset: var offset } && offset != TimeSpan.Zero)
        {
            return BadRequest(new ProblemDetails
            {
                Type = "https://docs.example.invalid/problems/validation",
                Title = "Audit cursor must be UTC.",
                Status = StatusCodes.Status400BadRequest,
                Extensions = { ["code"] = "audit_cursor_invalid" }
            });
        }

        var query = dbContext.AuditEvents.AsNoTracking();
        if (beforeUtc.HasValue)
        {
            query = query.Where(value => value.OccurredAtUtc < beforeUtc.Value);
        }

        var rows = await query
            .OrderByDescending(value => value.OccurredAtUtc)
            .ThenByDescending(value => value.Id)
            .Skip(pageOffset)
            .Take(limit + 1)
            .Select(value => new AuditEventResponse(
                value.Id,
                value.ActorType,
                value.ActorId,
                value.Action,
                value.TargetType,
                value.TargetId,
                value.Outcome,
                value.ReasonCode,
                value.CorrelationId,
                value.OccurredAtUtc))
            .ToListAsync(cancellationToken);
        CursorPage.WriteNext(Response, pageOffset, limit, rows.Count);
        return Ok(rows.Take(limit).ToArray());
    }
}

public sealed record AuditEventResponse(
    Guid Id,
    string ActorType,
    Guid? ActorId,
    string Action,
    string TargetType,
    Guid? TargetId,
    string Outcome,
    string? ReasonCode,
    Guid CorrelationId,
    DateTimeOffset OccurredAtUtc);
