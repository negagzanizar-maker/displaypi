using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using DisplayControl.Api.Pagination;
using DisplayControl.Api.Security;
using DisplayControl.Domain.Identity;
using DisplayControl.Domain.Operations;
using DisplayControl.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DisplayControl.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId:guid}/members")]
[Authorize(Policy = AuthorizationPolicies.TenantAdministrator)]
public sealed class TenantMembersController(
    DisplayControlDbContext dbContext,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TenantMemberResponse>>> List(
        Guid tenantId,
        CancellationToken cancellationToken,
        [FromQuery, Range(1, 200)] int limit = 100,
        [FromQuery] string? cursor = null)
    {
        if (!CursorPage.TryReadOffset(cursor, out var offset)) return BadRequest("Invalid cursor.");
        var members = await (
            from membership in dbContext.TenantMemberships.AsNoTracking()
            join user in dbContext.Users.AsNoTracking() on membership.UserId equals user.Id
            orderby user.DisplayName, membership.Id
            select new TenantMemberResponse(
                membership.Id,
                user.Id,
                user.DisplayName,
                user.Email ?? string.Empty,
                membership.Role,
                membership.State,
                user.TwoFactorEnabled,
                membership.AcceptedAtUtc,
                membership.ConcurrencyToken))
            .Skip(offset)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        CursorPage.WriteNext(Response, offset, limit, members.Count);
        return Ok(members.Take(limit).ToArray());
    }

    [HttpPost("{membershipId:guid}/role")]
    [Authorize(Policy = AuthorizationPolicies.RecentMfaSession)]
    public async Task<ActionResult<TenantMemberResponse>> ChangeRole(
        Guid tenantId,
        Guid membershipId,
        ChangeMemberRoleRequest request,
        CancellationToken cancellationToken)
    {
        var membership = await FindMembershipAsync(membershipId, cancellationToken);
        if (membership is null)
        {
            return NotFound();
        }

        if (membership.ConcurrencyToken != request.ConcurrencyToken)
        {
            return ConcurrencyConflict();
        }

        if (membership.UserId == CurrentUserId())
        {
            return ConflictProblem("self_membership_change_forbidden", "Another tenant administrator must change your role.");
        }

        if (membership.Role == TenantRole.TenantAdmin && request.Role != TenantRole.TenantAdmin &&
            !await HasAnotherAdministratorAsync(membership.Id, cancellationToken))
        {
            return ConflictProblem("last_tenant_admin", "The last active tenant administrator cannot be demoted.");
        }

        var nowUtc = timeProvider.GetUtcNow();
        var previousRole = membership.Role;
        try
        {
            membership.ChangeRole(request.Role, nowUtc);
        }
        catch (InvalidOperationException)
        {
            return ConflictProblem("membership_not_active", "Only an active membership can change role.");
        }

        await RevokeSessionsAsync(membership.UserId, "membership_role_changed", nowUtc, cancellationToken);
        AddAudit(tenantId, "membership.role_changed", membership.Id, request.Reason, new
        {
            userId = membership.UserId,
            previousRole,
            newRole = request.Role
        }, nowUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(await ProjectMemberAsync(membership.Id, cancellationToken));
    }

    [HttpPost("{membershipId:guid}/suspend")]
    public Task<ActionResult<TenantMemberResponse>> Suspend(
        Guid tenantId,
        Guid membershipId,
        ChangeMemberStateRequest request,
        CancellationToken cancellationToken) => ChangeState(
            tenantId,
            membershipId,
            request,
            "membership.suspended",
            static (membership, atUtc) => membership.Suspend(atUtc),
            "membership_suspended",
            cancellationToken);

    [HttpPost("{membershipId:guid}/reactivate")]
    public Task<ActionResult<TenantMemberResponse>> Reactivate(
        Guid tenantId,
        Guid membershipId,
        ChangeMemberStateRequest request,
        CancellationToken cancellationToken) => ChangeState(
            tenantId,
            membershipId,
            request,
            "membership.reactivated",
            static (membership, atUtc) => membership.Reactivate(atUtc),
            null,
            cancellationToken);

    [HttpPost("{membershipId:guid}/remove")]
    public Task<ActionResult<TenantMemberResponse>> Remove(
        Guid tenantId,
        Guid membershipId,
        ChangeMemberStateRequest request,
        CancellationToken cancellationToken) => ChangeState(
            tenantId,
            membershipId,
            request,
            "membership.removed",
            static (membership, atUtc) => membership.Remove(atUtc),
            "membership_removed",
            cancellationToken);

    private async Task<ActionResult<TenantMemberResponse>> ChangeState(
        Guid tenantId,
        Guid membershipId,
        ChangeMemberStateRequest request,
        string auditAction,
        Action<TenantMembership, DateTimeOffset> change,
        string? sessionRevocationReason,
        CancellationToken cancellationToken)
    {
        var membership = await FindMembershipAsync(membershipId, cancellationToken);
        if (membership is null)
        {
            return NotFound();
        }

        if (membership.ConcurrencyToken != request.ConcurrencyToken)
        {
            return ConcurrencyConflict();
        }

        if (membership.UserId == CurrentUserId())
        {
            return ConflictProblem("self_membership_change_forbidden", "Another tenant administrator must change your access.");
        }

        if (membership.Role == TenantRole.TenantAdmin && membership.State == MembershipState.Active &&
            auditAction is "membership.suspended" or "membership.removed" &&
            !await HasAnotherAdministratorAsync(membership.Id, cancellationToken))
        {
            return ConflictProblem("last_tenant_admin", "The last active tenant administrator cannot be disabled.");
        }

        var nowUtc = timeProvider.GetUtcNow();
        try
        {
            change(membership, nowUtc);
        }
        catch (InvalidOperationException)
        {
            return ConflictProblem("membership_state_invalid", "The requested membership transition is not allowed.");
        }

        if (sessionRevocationReason is not null)
        {
            await RevokeSessionsAsync(membership.UserId, sessionRevocationReason, nowUtc, cancellationToken);
        }

        AddAudit(tenantId, auditAction, membership.Id, request.Reason, new
        {
            userId = membership.UserId,
            state = membership.State
        }, nowUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(await ProjectMemberAsync(membership.Id, cancellationToken));
    }

    private Task<TenantMembership?> FindMembershipAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.TenantMemberships.SingleOrDefaultAsync(value => value.Id == id, cancellationToken);

    private Task<bool> HasAnotherAdministratorAsync(Guid membershipId, CancellationToken cancellationToken) =>
        dbContext.TenantMemberships.AnyAsync(value => value.Id != membershipId &&
            value.Role == TenantRole.TenantAdmin && value.State == MembershipState.Active, cancellationToken);

    private async Task RevokeSessionsAsync(
        Guid userId,
        string reason,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var sessions = await dbContext.UserSessions
            .Where(value => value.UserId == userId && value.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.Revoke(reason, nowUtc);
        }
    }

    private async Task<TenantMemberResponse> ProjectMemberAsync(Guid membershipId, CancellationToken cancellationToken) =>
        await (
            from membership in dbContext.TenantMemberships.AsNoTracking()
            join user in dbContext.Users.AsNoTracking() on membership.UserId equals user.Id
            where membership.Id == membershipId
            select new TenantMemberResponse(
                membership.Id,
                user.Id,
                user.DisplayName,
                user.Email ?? string.Empty,
                membership.Role,
                membership.State,
                user.TwoFactorEnabled,
                membership.AcceptedAtUtc,
                membership.ConcurrencyToken))
            .SingleAsync(cancellationToken);

    private void AddAudit(
        Guid tenantId,
        string action,
        Guid membershipId,
        string reason,
        object details,
        DateTimeOffset nowUtc) => dbContext.AuditEvents.Add(new TenantAuditEvent(
            Guid.NewGuid(),
            tenantId,
            "user",
            CurrentUserId(),
            action,
            "membership",
            membershipId,
            "success",
            reason,
            HttpContext.TraceIdentifierGuid(),
            JsonSerializer.Serialize(details),
            nowUtc));

    private Guid CurrentUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
        ? userId
        : throw new InvalidOperationException("Authenticated principal has no valid user identifier.");

    private static ObjectResult ConcurrencyConflict() =>
        ConflictProblem("concurrency_conflict", "The membership changed. Refresh it before retrying.");

    private static ObjectResult ConflictProblem(string code, string title) => new(new ProblemDetails
    {
        Type = "https://docs.example.invalid/problems/membership-state",
        Title = title,
        Status = StatusCodes.Status409Conflict,
        Extensions = { ["code"] = code }
    })
    {
        StatusCode = StatusCodes.Status409Conflict
    };
}

public sealed record ChangeMemberRoleRequest(
    TenantRole Role,
    Guid ConcurrencyToken,
    [param: Required, StringLength(64, MinimumLength = 3)] string Reason);

public sealed record ChangeMemberStateRequest(
    Guid ConcurrencyToken,
    [param: Required, StringLength(64, MinimumLength = 3)] string Reason);

public sealed record TenantMemberResponse(
    Guid MembershipId,
    Guid UserId,
    string DisplayName,
    string Email,
    TenantRole Role,
    MembershipState State,
    bool MfaEnabled,
    DateTimeOffset AcceptedAtUtc,
    Guid ConcurrencyToken);
