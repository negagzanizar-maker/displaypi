using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json;

using DisplayControl.Api.Security;
using DisplayControl.Application.Security;
using DisplayControl.Domain.Identity;
using DisplayControl.Infrastructure.Identity;
using DisplayControl.Infrastructure.Persistence;
using DisplayControl.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace DisplayControl.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/auth")]
public sealed class PasswordRecoveryController(
    DisplayControlDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    ScopedTenantContext tenantContext,
    ISensitivePayloadProtector payloadProtector,
    UniformPasswordFailureService uniformPasswordFailure,
    TenantSecurityAuditService securityAudit,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpPost("forgot-password")]
    [EnableRateLimiting("authentication")]
    [ProducesResponseType<PasswordRecoveryAcceptedResponse>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult<PasswordRecoveryAcceptedResponse>> ForgotPassword(
        ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        var user = await userManager.FindByEmailAsync(email);
        if (user is not null &&
            user.AccountState == AccountState.Active &&
            user.EmailConfirmed &&
            !string.IsNullOrWhiteSpace(user.NormalizedEmail))
        {
            if (user.HomeTenantId is Guid tenantId)
            {
                tenantContext.SetForCapabilityLookup(tenantId);
            }

            await using var transaction = user.HomeTenantId is Guid scopedTenantId
                ? await dbContext.BeginTenantTransactionAsync(scopedTenantId, cancellationToken)
                : await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
            var payload = JsonSerializer.Serialize(new PasswordResetNotificationPayload(resetToken));
            dbContext.IdentityNotifications.Add(new IdentityNotification(
                Guid.NewGuid(),
                user.Id,
                user.HomeTenantId,
                "PasswordResetRequested",
                user.NormalizedEmail,
                payloadProtector.ProtectionScheme,
                payloadProtector.Protect(payload),
                timeProvider.GetUtcNow()));
            if (user.HomeTenantId is Guid auditTenantId)
            {
                securityAudit.Add(
                    auditTenantId,
                    user.Id,
                    "identity.password_recovery.requested",
                    "user",
                    user.Id,
                    "success",
                    null);
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            var dummyPayload = payloadProtector.Protect(Guid.NewGuid().ToString("N"));
            CryptographicOperations.ZeroMemory(dummyPayload);
        }

        return Accepted(new PasswordRecoveryAcceptedResponse(
            "If an eligible account exists, password recovery instructions have been queued."));
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting("authentication")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null || user.AccountState != AccountState.Active || !user.EmailConfirmed)
        {
            uniformPasswordFailure.Perform(request.NewPassword);
            return ResetFailed();
        }

        if (user.HomeTenantId is Guid tenantId)
        {
            tenantContext.SetForCapabilityLookup(tenantId);
        }

        await using var transaction = user.HomeTenantId is Guid scopedTenantId
            ? await dbContext.BeginTenantTransactionAsync(scopedTenantId, cancellationToken)
            : await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var result = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            return ResetFailed();
        }

        var nowUtc = timeProvider.GetUtcNow();
        user.LastPasswordChangedAtUtc = nowUtc;
        user.UpdatedAtUtc = nowUtc;
        var activeSessions = await dbContext.UserSessions
            .Where(value => value.UserId == user.Id && value.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var session in activeSessions)
        {
            session.Revoke("password_reset", nowUtc);
        }

        if (user.HomeTenantId is Guid auditTenantId)
        {
            securityAudit.Add(
                auditTenantId,
                user.Id,
                "identity.password_recovery.completed",
                "user",
                user.Id,
                "success",
                null,
                new { revokedSessions = activeSessions.Count });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    private static BadRequestObjectResult ResetFailed() => new(new ProblemDetails
    {
        Type = "https://docs.example.invalid/problems/password-reset-failed",
        Title = "The password reset request is invalid or expired.",
        Status = StatusCodes.Status400BadRequest,
        Extensions = { ["code"] = "password_reset_failed" }
    });
}

public sealed record ForgotPasswordRequest(
    [param: Required, EmailAddress, StringLength(320)] string Email);

public sealed record ResetPasswordRequest(
    [param: Required, EmailAddress, StringLength(320)] string Email,
    [param: Required, StringLength(4096, MinimumLength = 16)] string Token,
    [param: Required, StringLength(1024, MinimumLength = 15)] string NewPassword);

public sealed record PasswordRecoveryAcceptedResponse(string Message);

internal sealed record PasswordResetNotificationPayload(string Token);
