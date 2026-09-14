using System.Text.Json;
using DisplayControl.Api.Controllers;
using DisplayControl.Api.Security;
using DisplayControl.Application.Security;
using DisplayControl.Domain.Identity;
using DisplayControl.Infrastructure.Identity;
using DisplayControl.Infrastructure.Persistence;
using DisplayControl.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace DisplayControl.Api.Identity;

public enum InvitationCreateOutcome
{
    Created,
    InvalidEmail,
    PendingInvitationExists
}

public sealed record InvitationCreateResult(
    InvitationCreateOutcome Outcome,
    InvitationCreatedResponse? Response = null);

public enum InvitationAcceptOutcome
{
    Accepted,
    Invalid,
    AccountExists,
    IdentityValidationFailed
}

public sealed record InvitationAcceptResult(
    InvitationAcceptOutcome Outcome,
    IReadOnlyCollection<IdentityError>? IdentityErrors = null);

public sealed class InvitationWorkflow(
    DisplayControlDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    ScopedTenantContext tenantContext,
    ITenantCapabilityTokenService capabilityTokenService,
    ISensitivePayloadProtector payloadProtector,
    TenantSecurityAuditService securityAudit,
    TimeProvider timeProvider)
{
    public async Task<InvitationCreateResult> CreateAsync(
        Guid tenantId,
        Guid creatorId,
        CreateInvitationRequest request,
        CancellationToken cancellationToken)
    {
        var trimmedEmail = request.Email.Trim();
        var normalizedEmail = userManager.NormalizeEmail(trimmedEmail);
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return new InvitationCreateResult(InvitationCreateOutcome.InvalidEmail);
        }

        var nowUtc = timeProvider.GetUtcNow();
        var expiresAtUtc = nowUtc.AddHours(24);
        var capability = capabilityTokenService.Generate(tenantId);
        var invitation = new Invitation(
            Guid.NewGuid(),
            tenantId,
            normalizedEmail,
            request.Role,
            capability.Digest,
            expiresAtUtc,
            creatorId,
            nowUtc);
        dbContext.Invitations.Add(invitation);
        var notificationPayload = JsonSerializer.Serialize(new InvitationNotificationPayload(capability.Value));
        dbContext.IdentityNotifications.Add(new IdentityNotification(
            Guid.NewGuid(),
            null,
            tenantId,
            "InvitationCreated",
            normalizedEmail,
            payloadProtector.ProtectionScheme,
            payloadProtector.Protect(notificationPayload),
            nowUtc));
        securityAudit.Add(
            tenantId,
            creatorId,
            "identity.invitation.created",
            "invitation",
            invitation.Id,
            "success",
            null,
            new { intendedRole = request.Role.ToString() });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return new InvitationCreateResult(InvitationCreateOutcome.PendingInvitationExists);
        }

        return new InvitationCreateResult(
            InvitationCreateOutcome.Created,
            new InvitationCreatedResponse(invitation.Id, trimmedEmail, request.Role, expiresAtUtc, capability.Value));
    }

    public async Task<InvitationAcceptResult> AcceptAsync(
        AcceptInvitationRequest request,
        CancellationToken cancellationToken)
    {
        var token = request.Token;
        if (!capabilityTokenService.TryReadTenantId(token, out var tenantId))
        {
            return new InvitationAcceptResult(InvitationAcceptOutcome.Invalid);
        }

        tenantContext.SetForCapabilityLookup(tenantId);
        await using var transaction = await dbContext.BeginTenantTransactionAsync(tenantId, cancellationToken);
        var tokenDigest = capabilityTokenService.ComputeDigest(token);
        var invitation = await dbContext.Invitations.SingleOrDefaultAsync(
            value => value.TokenDigest == tokenDigest,
            cancellationToken);
        var nowUtc = timeProvider.GetUtcNow();
        if (invitation is null || !invitation.CanBeConsumedAt(nowUtc))
        {
            return new InvitationAcceptResult(InvitationAcceptOutcome.Invalid);
        }

        var existingUser = await userManager.FindByEmailAsync(invitation.NormalizedEmail);
        if (existingUser is not null)
        {
            return new InvitationAcceptResult(InvitationAcceptOutcome.AccountExists);
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = invitation.NormalizedEmail,
            Email = invitation.NormalizedEmail,
            EmailConfirmed = true,
            DisplayName = request.DisplayName.Trim(),
            AccountState = AccountState.Active,
            HomeTenantId = tenantId,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            LastPasswordChangedAtUtc = nowUtc,
            LockoutEnabled = true
        };
        var creationResult = await userManager.CreateAsync(user, request.Password);
        if (!creationResult.Succeeded)
        {
            return new InvitationAcceptResult(
                InvitationAcceptOutcome.IdentityValidationFailed,
                creationResult.Errors.ToArray());
        }

        invitation.Consume(user.Id, invitation.NormalizedEmail, nowUtc);
        dbContext.TenantMemberships.Add(new TenantMembership(
            Guid.NewGuid(),
            tenantId,
            user.Id,
            invitation.IntendedRole,
            invitation.CreatedByUserId,
            nowUtc));
        securityAudit.Add(
            tenantId,
            user.Id,
            "identity.invitation.accepted",
            "invitation",
            invitation.Id,
            "success",
            null,
            new { role = invitation.IntendedRole.ToString() });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new InvitationAcceptResult(InvitationAcceptOutcome.Accepted);
    }
}
