using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

using DisplayControl.Api.Security;
using DisplayControl.Application.Security;
using DisplayControl.Domain.Identity;
using DisplayControl.Domain.Tenancy;
using DisplayControl.Infrastructure.Identity;
using DisplayControl.Infrastructure.Persistence;
using DisplayControl.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace DisplayControl.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthenticationController(
    DisplayControlDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ScopedTenantContext tenantContext,
    UserSessionService userSessionService,
    UniformPasswordFailureService uniformPasswordFailure,
    ISecureTokenService secureTokenService,
    TenantSecurityAuditService securityAudit,
    HumanAuthenticationOptions humanAuthenticationOptions,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpPost("sign-in")]
    [AllowAnonymous]
    [EnableRateLimiting("authentication")]
    [ProducesResponseType<AuthenticationStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<AuthenticationStatusResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthenticationStatusResponse>> SignIn(
        SignInRequest request,
        CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return ConflictProblem("already_authenticated", "Sign out before starting another session.");
        }

        var normalizedEmail = userManager.NormalizeEmail(request.Email.Trim());
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            uniformPasswordFailure.Perform(request.Password);
            return InvalidCredentials();
        }

        var passwordResult = await signInManager.CheckPasswordSignInAsync(
            user,
            request.Password,
            lockoutOnFailure: true);
        if ((!passwordResult.Succeeded && !passwordResult.RequiresTwoFactor) ||
            user.AccountState != AccountState.Active ||
            !user.EmailConfirmed ||
            !string.Equals(user.NormalizedEmail, normalizedEmail, StringComparison.Ordinal))
        {
            await AuditKnownTenantAsync(user, "failure", "invalid_credentials", cancellationToken);
            return InvalidCredentials();
        }

        var nowUtc = timeProvider.GetUtcNow();
        TenantMembership? membership = null;
        Tenant? tenant = null;
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tenantTransaction = null;
        try
        {
            if (user.HomeTenantId is Guid tenantId)
            {
                tenantContext.SetFromTrustedBoundary(tenantId);
                tenantTransaction = await dbContext.BeginTenantTransactionAsync(tenantId, cancellationToken);
                tenant = await dbContext.Tenants.SingleOrDefaultAsync(
                    value => value.Id == tenantId && value.State == TenantState.Active,
                    cancellationToken);
                membership = await dbContext.TenantMemberships.SingleOrDefaultAsync(
                    value => value.UserId == user.Id && value.State == MembershipState.Active,
                    cancellationToken);
                if (tenant is null || membership is null)
                {
                    securityAudit.Add(
                        tenantId,
                        user.Id,
                        "identity.authentication.sign_in",
                        "user",
                        user.Id,
                        "failure",
                        "tenant_or_membership_inactive");
                    await dbContext.SaveChangesAsync(cancellationToken);
                    await tenantTransaction.CommitAsync(cancellationToken);
                    return InvalidCredentials();
                }
            }

            var isPlatformAdministrator = user.HomeTenantId is null &&
                await userManager.IsInRoleAsync(user, "PlatformAdministrator");
            if (user.HomeTenantId is null && !isPlatformAdministrator)
            {
                return InvalidCredentials();
            }

            var confirmedMfa = await dbContext.UserMfaSecrets.AnyAsync(
                value => value.UserId == user.Id && value.ConfirmedAtUtc != null,
                cancellationToken);
            var mfaRequired = humanAuthenticationOptions.RequireMfa && (
                isPlatformAdministrator ||
                membership?.Role is TenantRole.TenantAdmin or TenantRole.ContentManager ||
                user.TwoFactorEnabled);
            var authenticationStage = !mfaRequired
                ? SessionClaimTypes.FullStage
                : confirmedMfa
                    ? SessionClaimTypes.MfaPendingStage
                    : SessionClaimTypes.MfaEnrollmentStage;

            if (user.HomeTenantId is Guid auditTenantId)
            {
                securityAudit.Add(
                    auditTenantId,
                    user.Id,
                    "identity.authentication.sign_in",
                    "user",
                    user.Id,
                    "success",
                    null,
                    new { authenticationStage });
            }

            var issuedSession = await userSessionService.CreateAsync(
                user,
                membership,
                authenticationStage,
                mfaSatisfied: false,
                Request.Headers.UserAgent.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                nowUtc,
                cancellationToken);

            if (tenantTransaction is not null)
            {
                await tenantTransaction.CommitAsync(cancellationToken);
            }

            await HttpContext.SignInAsync(
                IdentityConstants.ApplicationScheme,
                issuedSession.Principal,
                issuedSession.AuthenticationProperties);

            if (authenticationStage == SessionClaimTypes.FullStage)
            {
                return Ok(new AuthenticationStatusResponse("authenticated"));
            }

            return Accepted(new AuthenticationStatusResponse(
                authenticationStage == SessionClaimTypes.MfaPendingStage
                    ? "mfa_required"
                    : "mfa_enrollment_required"));
        }
        finally
        {
            if (tenantTransaction is not null)
            {
                await tenantTransaction.DisposeAsync();
            }
        }
    }

    [HttpPost("sign-out")]
    [Authorize(Policy = AuthorizationPolicies.AuthenticatedSession)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SignOutCurrent(CancellationToken cancellationToken)
    {
        var rawSessionKey = User.FindFirstValue(SessionClaimTypes.SessionKey);
        if (!string.IsNullOrWhiteSpace(rawSessionKey))
        {
            var digest = secureTokenService.ComputeDigest(rawSessionKey);
            var session = await dbContext.UserSessions.SingleOrDefaultAsync(
                value => value.SessionKeyDigest == digest,
                cancellationToken);
            if (session is not null && session.RevokedAtUtc is null)
            {
                session.Revoke("user_sign_out", timeProvider.GetUtcNow());
                AddCurrentTenantAudit(
                    "identity.session.signed_out",
                    session.UserId,
                    new { allSessions = false });
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        return NoContent();
    }

    [HttpPost("sign-out-all")]
    [Authorize(Policy = AuthorizationPolicies.RecentMfaSession)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SignOutAll(CancellationToken cancellationToken)
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            throw new InvalidOperationException("Authenticated principal has no valid user identifier.");
        }

        var nowUtc = timeProvider.GetUtcNow();
        var activeSessions = await dbContext.UserSessions
            .Where(value => value.UserId == userId && value.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var session in activeSessions)
        {
            session.Revoke("user_sign_out_all", nowUtc);
        }
        AddCurrentTenantAudit(
            "identity.session.signed_out",
            userId,
            new { allSessions = true, revokedSessions = activeSessions.Count });

        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException("Authenticated user no longer exists.");
        var stampResult = await userManager.UpdateSecurityStampAsync(user);
        if (!stampResult.Succeeded)
        {
            throw new InvalidOperationException("Identity security stamp could not be rotated.");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        return NoContent();
    }

    private async Task AuditKnownTenantAsync(
        ApplicationUser user,
        string outcome,
        string reasonCode,
        CancellationToken cancellationToken)
    {
        if (user.HomeTenantId is not Guid tenantId)
        {
            return;
        }

        tenantContext.SetFromTrustedBoundary(tenantId);
        await using var transaction = await dbContext.BeginTenantTransactionAsync(tenantId, cancellationToken);
        securityAudit.Add(
            tenantId,
            user.Id,
            "identity.authentication.sign_in",
            "user",
            user.Id,
            outcome,
            reasonCode);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private void AddCurrentTenantAudit(string action, Guid actorId, object details)
    {
        if (Guid.TryParse(User.FindFirstValue(SessionClaimTypes.TenantId), out var tenantId))
        {
            securityAudit.Add(tenantId, actorId, action, "user", actorId, "success", null, details);
        }
    }

    private static UnauthorizedObjectResult InvalidCredentials() => new(new ProblemDetails
    {
        Type = "https://docs.example.invalid/problems/authentication-failed",
        Title = "Authentication failed.",
        Status = StatusCodes.Status401Unauthorized,
        Extensions = { ["code"] = "authentication_failed" }
    });

    private static ObjectResult ConflictProblem(string code, string title) => new(new ProblemDetails
    {
        Type = "https://docs.example.invalid/problems/authentication-state",
        Title = title,
        Status = StatusCodes.Status409Conflict,
        Extensions = { ["code"] = code }
    })
    {
        StatusCode = StatusCodes.Status409Conflict
    };
}

public sealed record SignInRequest(
    [param: Required, EmailAddress, StringLength(320)] string Email,
    [param: Required, StringLength(1024, MinimumLength = 1)] string Password);

public sealed record AuthenticationStatusResponse(string Status);
