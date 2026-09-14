using System.Security.Claims;

using DisplayControl.Application.Security;
using DisplayControl.Domain.Identity;
using DisplayControl.Infrastructure.Identity;
using DisplayControl.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace DisplayControl.Api.Security;

public sealed class UserSessionService(
    DisplayControlDbContext dbContext,
    ISecureTokenService secureTokenService,
    IUserClaimsPrincipalFactory<ApplicationUser> principalFactory)
{
    public static readonly TimeSpan IdleLifetime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromHours(8);

    public async Task<SessionIssueResult> CreateAsync(
        ApplicationUser user,
        TenantMembership? membership,
        string authenticationStage,
        bool mfaSatisfied,
        string? userAgent,
        string? sourceAddress,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ValidateAuthenticationStage(authenticationStage);
        var securityStamp = user.SecurityStamp
            ?? throw new InvalidOperationException("Identity user has no security stamp.");
        var generatedSessionKey = secureTokenService.Generate();
        var session = new UserSession(
            Guid.NewGuid(),
            user.Id,
            generatedSessionKey.Digest,
            secureTokenService.ComputeDigest(securityStamp),
            nowUtc,
            IdleLifetime,
            AbsoluteLifetime,
            membership?.TenantId,
            ComputeOptionalDigest(userAgent),
            ComputeOptionalDigest(sourceAddress));
        if (mfaSatisfied)
        {
            session.MarkMfaSatisfied(nowUtc);
        }

        dbContext.UserSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);

        var principal = await CreatePrincipalAsync(
            user,
            membership,
            generatedSessionKey.Value,
            authenticationStage,
            mfaSatisfied);
        return new SessionIssueResult(
            principal,
            CreateAuthenticationProperties(nowUtc, session.AbsoluteExpiresAtUtc),
            generatedSessionKey.Value);
    }

    public async Task<ClaimsPrincipal> CreatePrincipalAsync(
        ApplicationUser user,
        TenantMembership? membership,
        string rawSessionKey,
        string authenticationStage,
        bool mfaSatisfied)
    {
        ValidateAuthenticationStage(authenticationStage);
        var principal = await principalFactory.CreateAsync(user);
        if (principal.Identity is not ClaimsIdentity identity)
        {
            throw new InvalidOperationException("Identity principal factory returned no claims identity.");
        }

        identity.AddClaim(new Claim(SessionClaimTypes.SessionKey, rawSessionKey));
        identity.AddClaim(new Claim(SessionClaimTypes.AuthenticationStage, authenticationStage));
        identity.AddClaim(new Claim(SessionClaimTypes.AuthenticationMethod, "pwd"));
        if (mfaSatisfied)
        {
            identity.AddClaim(new Claim(SessionClaimTypes.AuthenticationMethod, "mfa"));
        }

        if (membership is not null)
        {
            identity.AddClaim(new Claim(SessionClaimTypes.TenantId, membership.TenantId.ToString()));
            identity.AddClaim(new Claim(SessionClaimTypes.TenantRole, membership.Role.ToString()));
        }

        return principal;
    }

    public static AuthenticationProperties CreateAuthenticationProperties(
        DateTimeOffset issuedAtUtc,
        DateTimeOffset expiresAtUtc) =>
        new()
        {
            AllowRefresh = false,
            IsPersistent = false,
            IssuedUtc = issuedAtUtc,
            ExpiresUtc = expiresAtUtc
        };

    private byte[]? ComputeOptionalDigest(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : secureTokenService.ComputeDigest(value);

    private static void ValidateAuthenticationStage(string stage)
    {
        if (stage != SessionClaimTypes.FullStage &&
            stage != SessionClaimTypes.MfaPendingStage &&
            stage != SessionClaimTypes.MfaEnrollmentStage)
        {
            throw new ArgumentOutOfRangeException(nameof(stage), "Unknown authentication stage.");
        }
    }
}
