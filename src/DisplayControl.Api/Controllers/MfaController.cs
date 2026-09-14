using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;

using DisplayControl.Api.Security;
using DisplayControl.Application.Security;
using DisplayControl.Domain.Identity;
using DisplayControl.Infrastructure.Identity;
using DisplayControl.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace DisplayControl.Api.Controllers;

[ApiController]
[Route("api/v1/auth/mfa")]
public sealed class MfaController(
    DisplayControlDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    ITotpService totpService,
    IMfaSecretProtector mfaSecretProtector,
    ISecureTokenService secureTokenService,
    UserSessionService userSessionService,
    TenantSecurityAuditService securityAudit,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpPost("totp/enroll")]
    [Authorize(Policy = AuthorizationPolicies.MfaEnrollment)]
    [EnableRateLimiting("authentication")]
    [ProducesResponseType<TotpEnrollmentResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TotpEnrollmentResponse>> Enroll(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        var existingSecret = await dbContext.UserMfaSecrets.SingleOrDefaultAsync(
            value => value.UserId == user.Id,
            cancellationToken);
        if (existingSecret?.ConfirmedAtUtc is not null)
        {
            return ConflictProblem("mfa_already_enrolled", "MFA is already enrolled.");
        }

        if (existingSecret is not null)
        {
            dbContext.UserMfaSecrets.Remove(existingSecret);
        }

        var rawSecret = totpService.GenerateSecret();
        try
        {
            var protectedSecret = mfaSecretProtector.Protect(rawSecret);
            var nowUtc = timeProvider.GetUtcNow();
            dbContext.UserMfaSecrets.Add(new UserMfaSecret(
                user.Id,
                protectedSecret,
                mfaSecretProtector.ProtectionScheme,
                nowUtc));
            AddTenantAudit(user, "identity.mfa.enrollment_started", "success");
            await dbContext.SaveChangesAsync(cancellationToken);

            return Ok(new TotpEnrollmentResponse(
                totpService.EncodeBase32(rawSecret),
                totpService.CreateOtpAuthUri("Display Control", user.Email ?? user.UserName ?? user.Id.ToString(), rawSecret)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rawSecret);
        }
    }

    [HttpPost("totp/confirm")]
    [Authorize(Policy = AuthorizationPolicies.MfaEnrollment)]
    [EnableRateLimiting("authentication")]
    [ProducesResponseType<MfaCompletionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<MfaCompletionResponse>> Confirm(
        MfaCodeRequest request,
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        var secretRecord = await dbContext.UserMfaSecrets.SingleOrDefaultAsync(
            value => value.UserId == user.Id && value.ConfirmedAtUtc == null,
            cancellationToken);
        if (secretRecord is null)
        {
            return MfaFailed();
        }

        var verification = Verify(secretRecord, request.Code);
        if (!verification.IsValid || verification.TimeStep is not long timeStep)
        {
            await userManager.AccessFailedAsync(user);
            await AddMfaFailureAuditAsync(user, "identity.mfa.enrollment_confirmed", cancellationToken);
            return MfaFailed();
        }

        var nowUtc = timeProvider.GetUtcNow();
        secretRecord.Confirm(nowUtc);
        secretRecord.AcceptTimeStep(timeStep, nowUtc);
        user.TwoFactorEnabled = true;

        var rawRecoveryCodes = CreateRecoveryCodes(user.Id, nowUtc);

        try
        {
            var completion = await CompleteMfaAsync(
                user,
                nowUtc,
                cancellationToken,
                auditAction: "identity.mfa.enrollment_confirmed");
            await userManager.ResetAccessFailedCountAsync(user);
            return Ok(new MfaCompletionResponse(completion.Status, rawRecoveryCodes));
        }
        catch (DbUpdateConcurrencyException)
        {
            return MfaFailed();
        }
    }

    [HttpPost("totp")]
    [Authorize(Policy = AuthorizationPolicies.MfaPending)]
    [EnableRateLimiting("authentication")]
    [ProducesResponseType<MfaCompletionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<MfaCompletionResponse>> VerifyTotp(
        MfaCodeRequest request,
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        var secretRecord = await dbContext.UserMfaSecrets.SingleOrDefaultAsync(
            value => value.UserId == user.Id && value.ConfirmedAtUtc != null,
            cancellationToken);
        if (secretRecord is null)
        {
            return MfaFailed();
        }

        var verification = Verify(secretRecord, request.Code);
        if (!verification.IsValid || verification.TimeStep is not long timeStep)
        {
            await userManager.AccessFailedAsync(user);
            await AddMfaFailureAuditAsync(user, "identity.mfa.verified", cancellationToken);
            return MfaFailed();
        }

        var nowUtc = timeProvider.GetUtcNow();
        try
        {
            secretRecord.AcceptTimeStep(timeStep, nowUtc);
            var completion = await CompleteMfaAsync(
                user,
                nowUtc,
                cancellationToken,
                auditAction: "identity.mfa.verified");
            await userManager.ResetAccessFailedCountAsync(user);
            return Ok(completion);
        }
        catch (DbUpdateConcurrencyException)
        {
            return MfaFailed();
        }
    }

    [HttpPost("step-up")]
    [Authorize(Policy = AuthorizationPolicies.MfaVerifiedSession)]
    [EnableRateLimiting("authentication")]
    [ProducesResponseType<MfaCompletionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<MfaCompletionResponse>> StepUp(
        MfaCodeRequest request,
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        var secretRecord = await dbContext.UserMfaSecrets.SingleOrDefaultAsync(
            value => value.UserId == user.Id && value.ConfirmedAtUtc != null,
            cancellationToken);
        if (secretRecord is null)
        {
            return MfaFailed();
        }

        var verification = Verify(secretRecord, request.Code);
        if (!verification.IsValid || verification.TimeStep is not long timeStep)
        {
            await userManager.AccessFailedAsync(user);
            await AddMfaFailureAuditAsync(user, "identity.mfa.step_up", cancellationToken);
            return MfaFailed();
        }

        var nowUtc = timeProvider.GetUtcNow();
        try
        {
            secretRecord.AcceptTimeStep(timeStep, nowUtc);
            var completion = await CompleteMfaAsync(
                user,
                nowUtc,
                cancellationToken,
                "recent_mfa_confirmed",
                "identity.mfa.step_up");
            await userManager.ResetAccessFailedCountAsync(user);
            return Ok(completion);
        }
        catch (DbUpdateConcurrencyException)
        {
            return MfaFailed();
        }
    }

    [HttpPost("recovery")]
    [Authorize(Policy = AuthorizationPolicies.MfaPending)]
    [EnableRateLimiting("authentication")]
    [ProducesResponseType<MfaCompletionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<MfaCompletionResponse>> UseRecoveryCode(
        RecoveryCodeRequest request,
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        var digest = secureTokenService.ComputeDigest(request.Code.Trim());
        var recoveryCode = await dbContext.UserRecoveryCodes.SingleOrDefaultAsync(
            value => value.UserId == user.Id && value.CodeDigest == digest && value.UsedAtUtc == null,
            cancellationToken);
        if (recoveryCode is null)
        {
            await userManager.AccessFailedAsync(user);
            await AddMfaFailureAuditAsync(user, "identity.mfa.recovery_code_used", cancellationToken);
            return MfaFailed();
        }

        var nowUtc = timeProvider.GetUtcNow();
        try
        {
            recoveryCode.MarkUsed(nowUtc);
            var completion = await CompleteMfaAsync(
                user,
                nowUtc,
                cancellationToken,
                auditAction: "identity.mfa.recovery_code_used");
            await userManager.ResetAccessFailedCountAsync(user);
            return Ok(completion);
        }
        catch (DbUpdateConcurrencyException)
        {
            return MfaFailed();
        }
    }

    [HttpPost("recovery/regenerate")]
    [Authorize(Policy = AuthorizationPolicies.RecentMfaSession)]
    [EnableRateLimiting("authentication")]
    [ProducesResponseType<MfaCompletionResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MfaCompletionResponse>> RegenerateRecoveryCodes(
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        var previousUnusedCodes = await dbContext.UserRecoveryCodes
            .Where(value => value.UserId == user.Id && value.UsedAtUtc == null)
            .ToListAsync(cancellationToken);
        dbContext.UserRecoveryCodes.RemoveRange(previousUnusedCodes);

        var nowUtc = timeProvider.GetUtcNow();
        var rawCodes = CreateRecoveryCodes(user.Id, nowUtc);
        AddTenantAudit(user, "identity.mfa.recovery_codes_regenerated", "success");
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new MfaCompletionResponse("recovery_codes_regenerated", rawCodes));
    }

    private TotpVerificationResult Verify(UserMfaSecret secretRecord, string code)
    {
        if (!string.Equals(
            secretRecord.ProtectionScheme,
            mfaSecretProtector.ProtectionScheme,
            StringComparison.Ordinal))
        {
            return new TotpVerificationResult(false, null);
        }

        byte[] rawSecret;
        try
        {
            rawSecret = mfaSecretProtector.Unprotect(secretRecord.ProtectedSecret);
        }
        catch (CryptographicException)
        {
            return new TotpVerificationResult(false, null);
        }

        try
        {
            return totpService.Verify(rawSecret, code, timeProvider.GetUtcNow());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rawSecret);
        }
    }

    private async Task<MfaCompletionResponse> CompleteMfaAsync(
        ApplicationUser user,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken,
        string status = "authenticated",
        string auditAction = "identity.mfa.verified")
    {
        var rawSessionKey = User.FindFirstValue(SessionClaimTypes.SessionKey)
            ?? throw new InvalidOperationException("Authenticated session has no session key.");
        var sessionDigest = secureTokenService.ComputeDigest(rawSessionKey);
        var session = await dbContext.UserSessions.SingleAsync(
            value => value.SessionKeyDigest == sessionDigest && value.UserId == user.Id,
            cancellationToken);
        var rotatedSessionKey = secureTokenService.Generate();
        session.RotateAfterMfa(rotatedSessionKey.Digest, nowUtc);

        TenantMembership? membership = null;
        if (user.HomeTenantId is not null)
        {
            membership = await dbContext.TenantMemberships.SingleAsync(
                value => value.UserId == user.Id && value.State == MembershipState.Active,
                cancellationToken);
        }

        AddTenantAudit(user, auditAction, "success");

        await dbContext.SaveChangesAsync(cancellationToken);
        var principal = await userSessionService.CreatePrincipalAsync(
            user,
            membership,
            rotatedSessionKey.Value,
            SessionClaimTypes.FullStage,
            mfaSatisfied: true);
        await HttpContext.SignInAsync(
            IdentityConstants.ApplicationScheme,
            principal,
            UserSessionService.CreateAuthenticationProperties(session.CreatedAtUtc, session.AbsoluteExpiresAtUtc));
        Response.Cookies.Delete("__Host-dc.csrf", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/"
        });
        return new MfaCompletionResponse(status, null);
    }

    private async Task AddMfaFailureAuditAsync(
        ApplicationUser user,
        string action,
        CancellationToken cancellationToken)
    {
        AddTenantAudit(user, action, "failure", "invalid_mfa_proof");
        if (user.HomeTenantId is not null)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private void AddTenantAudit(
        ApplicationUser user,
        string action,
        string outcome,
        string? reasonCode = null)
    {
        if (user.HomeTenantId is Guid tenantId)
        {
            securityAudit.Add(tenantId, user.Id, action, "user", user.Id, outcome, reasonCode);
        }
    }

    private List<string> CreateRecoveryCodes(Guid userId, DateTimeOffset createdAtUtc)
    {
        var rawRecoveryCodes = new List<string>(10);
        for (var index = 0; index < 10; index++)
        {
            var generated = secureTokenService.Generate(16);
            var displayCode = $"{generated.Value[..11]}-{generated.Value[11..]}";
            rawRecoveryCodes.Add(displayCode);
            dbContext.UserRecoveryCodes.Add(new UserRecoveryCode(
                Guid.NewGuid(),
                userId,
                secureTokenService.ComputeDigest(displayCode),
                createdAtUtc));
        }

        return rawRecoveryCodes;
    }

    private async Task<ApplicationUser> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            throw new InvalidOperationException("Authenticated principal has no valid user identifier.");
        }

        return await dbContext.Users.SingleAsync(value => value.Id == userId, cancellationToken);
    }

    private static UnauthorizedObjectResult MfaFailed() => new(new ProblemDetails
    {
        Type = "https://docs.example.invalid/problems/mfa-verification-failed",
        Title = "MFA verification failed.",
        Status = StatusCodes.Status401Unauthorized,
        Extensions = { ["code"] = "mfa_verification_failed" }
    });

    private static ObjectResult ConflictProblem(string code, string title) => new(new ProblemDetails
    {
        Type = "https://docs.example.invalid/problems/mfa-state",
        Title = title,
        Status = StatusCodes.Status409Conflict,
        Extensions = { ["code"] = code }
    })
    {
        StatusCode = StatusCodes.Status409Conflict
    };
}

public sealed record MfaCodeRequest(
    [param: Required, RegularExpression("^[0-9]{6}$")] string Code);

public sealed record RecoveryCodeRequest(
    [param: Required, StringLength(128, MinimumLength = 10)] string Code);

public sealed record TotpEnrollmentResponse(string Secret, string OtpAuthUri);

public sealed record MfaCompletionResponse(string Status, IReadOnlyList<string>? RecoveryCodes);
