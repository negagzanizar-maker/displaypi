using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using DisplayControl.Api.Pagination;
using DisplayControl.Api.Security;
using DisplayControl.Application.Security;
using DisplayControl.Domain.Identity;
using DisplayControl.Domain.Operations;
using DisplayControl.Domain.Tenancy;
using DisplayControl.Infrastructure.Identity;
using DisplayControl.Infrastructure.Persistence;
using DisplayControl.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace DisplayControl.Api.Controllers;

[ApiController]
[Route("api/v1/platform/tenants")]
public sealed class PlatformTenantsController(
    DisplayControlDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    ScopedTenantContext tenantContext,
    ITenantCapabilityTokenService capabilityTokenService,
    ISensitivePayloadProtector payloadProtector,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.PlatformAdministrator)]
    public async Task<ActionResult<IReadOnlyList<PlatformTenantResponse>>> List(
        [FromQuery, Range(1, 200)] int limit = 100,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        if (!CursorPage.TryReadOffset(cursor, out var offset)) return BadRequest("Invalid cursor.");
        await using var transaction = await dbContext.BeginPlatformCatalogTransactionAsync(cancellationToken);
        var tenants = await dbContext.Tenants.AsNoTracking()
            .OrderBy(value => value.Name)
            .ThenBy(value => value.Id)
            .Skip(offset)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        CursorPage.WriteNext(Response, offset, limit, tenants.Count);
        await transaction.CommitAsync(cancellationToken);
        return Ok(tenants.Take(limit).Select(ToResponse).ToArray());
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.PlatformAdministratorRecentMfa)]
    public async Task<ActionResult<PlatformTenantCreatedResponse>> Create(
        CreatePlatformTenantRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = userManager.NormalizeEmail(request.InitialAdministratorEmail.Trim());
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return ValidationProblem("initialAdministratorEmail", "A valid email address is required.");
        }

        if (await userManager.FindByEmailAsync(normalizedEmail) is not null)
        {
            return ConflictProblem(
                "initial_administrator_exists",
                "An account already exists for the initial administrator email.");
        }

        var nowUtc = timeProvider.GetUtcNow();
        Tenant tenant;
        try
        {
            tenant = new Tenant(Guid.NewGuid(), request.Name, request.Slug, request.TimeZone, nowUtc);
        }
        catch (ArgumentException exception)
        {
            return ValidationProblem("tenant", exception.Message);
        }

        var actorId = CurrentUserId();
        var capability = capabilityTokenService.Generate(tenant.Id);
        var invitation = new Invitation(
            Guid.NewGuid(),
            tenant.Id,
            normalizedEmail,
            TenantRole.TenantAdmin,
            capability.Digest,
            nowUtc.AddHours(24),
            actorId,
            nowUtc);

        tenantContext.SetForCapabilityLookup(tenant.Id);
        await using var transaction = await dbContext.BeginTenantTransactionAsync(tenant.Id, cancellationToken);
        dbContext.Tenants.Add(tenant);
        dbContext.Invitations.Add(invitation);
        var notificationPayload = JsonSerializer.Serialize(new InvitationNotificationPayload(capability.Value));
        dbContext.IdentityNotifications.Add(new IdentityNotification(
            Guid.NewGuid(),
            null,
            tenant.Id,
            "InvitationCreated",
            normalizedEmail,
            payloadProtector.ProtectionScheme,
            payloadProtector.Protect(notificationPayload),
            nowUtc));
        dbContext.AuditEvents.Add(new TenantAuditEvent(
            Guid.NewGuid(),
            tenant.Id,
            "Human",
            actorId,
            "tenant.created",
            "Tenant",
            tenant.Id,
            "Succeeded",
            null,
            HttpContext.TraceIdentifierAsGuid(),
            JsonSerializer.Serialize(new { tenant.Slug, InitialAdministratorEmail = normalizedEmail }),
            nowUtc));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return ConflictProblem("tenant_conflict", "The tenant slug or administrator invitation already exists.");
        }

        return Created(
            $"/api/v1/platform/tenants/{tenant.Id}",
            new PlatformTenantCreatedResponse(
                ToResponse(tenant),
                invitation.Id,
                request.InitialAdministratorEmail.Trim(),
                invitation.ExpiresAtUtc,
                capability.Value));
    }

    [HttpPatch("{tenantId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.PlatformAdministratorRecentMfa)]
    public async Task<ActionResult<PlatformTenantResponse>> Update(
        Guid tenantId,
        UpdatePlatformTenantRequest request,
        CancellationToken cancellationToken)
    {
        tenantContext.SetForCapabilityLookup(tenantId);
        await using var transaction = await dbContext.BeginTenantTransactionAsync(tenantId, cancellationToken);
        var tenant = await dbContext.Tenants.SingleOrDefaultAsync(value => value.Id == tenantId, cancellationToken);
        if (tenant is null)
        {
            return NotFound();
        }

        if (tenant.ConcurrencyToken != request.ConcurrencyToken)
        {
            return ConflictProblem("tenant_changed", "The tenant changed; reload it before updating.");
        }

        try
        {
            tenant.UpdateDetails(request.Name, request.TimeZone, timeProvider.GetUtcNow());
        }
        catch (ArgumentException exception)
        {
            return ValidationProblem("tenant", exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return ConflictProblem("tenant_state_conflict", exception.Message);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(ToResponse(tenant));
    }

    [HttpPatch("{tenantId:guid}/state")]
    [Authorize(Policy = AuthorizationPolicies.PlatformAdministratorRecentMfa)]
    public async Task<ActionResult<PlatformTenantResponse>> ChangeState(
        Guid tenantId,
        ChangePlatformTenantStateRequest request,
        CancellationToken cancellationToken)
    {
        tenantContext.SetForCapabilityLookup(tenantId);
        await using var transaction = await dbContext.BeginTenantTransactionAsync(tenantId, cancellationToken);
        var tenant = await dbContext.Tenants.SingleOrDefaultAsync(value => value.Id == tenantId, cancellationToken);
        if (tenant is null)
        {
            return NotFound();
        }

        if (tenant.ConcurrencyToken != request.ConcurrencyToken)
        {
            return ConflictProblem("tenant_changed", "The tenant changed; reload it before updating.");
        }

        var nowUtc = timeProvider.GetUtcNow();
        try
        {
            tenant.ChangeState(request.State, nowUtc);
        }
        catch (InvalidOperationException exception)
        {
            return ConflictProblem("tenant_state_conflict", exception.Message);
        }

        if (request.State != TenantState.Active)
        {
            var tenantUserIds = await dbContext.Users
                .Where(value => value.HomeTenantId == tenantId)
                .Select(value => value.Id)
                .ToListAsync(cancellationToken);
            var activeSessions = await dbContext.UserSessions
                .Where(value => tenantUserIds.Contains(value.UserId) && value.RevokedAtUtc == null)
                .ToListAsync(cancellationToken);
            foreach (var session in activeSessions)
            {
                session.Revoke("tenant_inactive", nowUtc);
            }
        }

        dbContext.AuditEvents.Add(new TenantAuditEvent(
            Guid.NewGuid(),
            tenantId,
            "Human",
            CurrentUserId(),
            "tenant.state_changed",
            "Tenant",
            tenantId,
            "Succeeded",
            request.Reason,
            HttpContext.TraceIdentifierAsGuid(),
            JsonSerializer.Serialize(new { State = request.State.ToString(), request.Reason }),
            nowUtc));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(ToResponse(tenant));
    }

    private Guid CurrentUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : throw new InvalidOperationException("Authenticated principal has no valid user identifier.");

    private static PlatformTenantResponse ToResponse(Tenant tenant) => new(
        tenant.Id,
        tenant.Name,
        tenant.Slug,
        tenant.TimeZone,
        tenant.State,
        tenant.CreatedAtUtc,
        tenant.UpdatedAtUtc,
        tenant.ConcurrencyToken);

    private static ObjectResult ValidationProblem(string field, string message) => new(new ValidationProblemDetails(
        new Dictionary<string, string[]> { [field] = [message] })
    {
        Type = "https://docs.example.invalid/problems/validation",
        Title = "The request could not be accepted.",
        Status = StatusCodes.Status400BadRequest,
        Extensions = { ["code"] = "validation_failed" }
    })
    {
        StatusCode = StatusCodes.Status400BadRequest
    };

    private static ObjectResult ConflictProblem(string code, string title) => new(new ProblemDetails
    {
        Type = "https://docs.example.invalid/problems/platform-tenant",
        Title = title,
        Status = StatusCodes.Status409Conflict,
        Extensions = { ["code"] = code }
    })
    {
        StatusCode = StatusCodes.Status409Conflict
    };
}

public sealed record CreatePlatformTenantRequest(
    [param: Required, StringLength(160, MinimumLength = 1)] string Name,
    [param: Required, StringLength(80, MinimumLength = 3)] string Slug,
    [param: Required, StringLength(80, MinimumLength = 1)] string TimeZone,
    [param: Required, EmailAddress, StringLength(320)] string InitialAdministratorEmail);

public sealed record UpdatePlatformTenantRequest(
    [param: Required, StringLength(160, MinimumLength = 1)] string Name,
    [param: Required, StringLength(80, MinimumLength = 1)] string TimeZone,
    Guid ConcurrencyToken);

public sealed record ChangePlatformTenantStateRequest(
    TenantState State,
    Guid ConcurrencyToken,
    [param: Required, StringLength(256, MinimumLength = 3)] string Reason);

public sealed record PlatformTenantResponse(
    Guid Id,
    string Name,
    string Slug,
    string TimeZone,
    TenantState State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    Guid ConcurrencyToken);

public sealed record PlatformTenantCreatedResponse(
    PlatformTenantResponse Tenant,
    Guid InvitationId,
    string InitialAdministratorEmail,
    DateTimeOffset InvitationExpiresAtUtc,
    string InvitationToken);

internal static class PlatformTenantHttpContextExtensions
{
    public static Guid TraceIdentifierAsGuid(this HttpContext context) =>
        Guid.TryParse(context.TraceIdentifier, out var correlationId)
            ? correlationId
            : Guid.NewGuid();
}
