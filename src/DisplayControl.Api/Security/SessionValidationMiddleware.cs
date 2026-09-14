using System.Security.Claims;
using System.Security.Cryptography;

using DisplayControl.Application.Security;
using DisplayControl.Domain.Identity;
using DisplayControl.Domain.Tenancy;
using DisplayControl.Infrastructure.Identity;
using DisplayControl.Infrastructure.Persistence;
using DisplayControl.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DisplayControl.Api.Security;

public sealed class SessionValidationMiddleware(RequestDelegate next)
{
    public static readonly object ValidatedSessionItemKey = new();

    public async Task InvokeAsync(
        HttpContext context,
        DisplayControlDbContext dbContext,
        ScopedTenantContext tenantContext,
        ISecureTokenService secureTokenService,
        UserManager<ApplicationUser> userManager,
        TimeProvider timeProvider)
    {
        if (context.Request.Path.StartsWithSegments("/device", StringComparison.OrdinalIgnoreCase))
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity());
            await next(context);
            return;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        var userIdValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var rawSessionKey = context.User.FindFirstValue(SessionClaimTypes.SessionKey);
        if (!Guid.TryParse(userIdValue, out var userId) || string.IsNullOrWhiteSpace(rawSessionKey))
        {
            await RejectSessionAsync(context);
            return;
        }

        var sessionDigest = secureTokenService.ComputeDigest(rawSessionKey);
        var session = await dbContext.UserSessions.SingleOrDefaultAsync(
            value => value.SessionKeyDigest == sessionDigest,
            context.RequestAborted);
        var user = await dbContext.Users.SingleOrDefaultAsync(value => value.Id == userId, context.RequestAborted);
        var nowUtc = timeProvider.GetUtcNow();
        var securityStampMatches = user?.SecurityStamp is not null &&
            CryptographicOperations.FixedTimeEquals(
                session?.SecurityStampDigest ?? [],
                secureTokenService.ComputeDigest(user.SecurityStamp));

        if (session is null ||
            session.UserId != userId ||
            user is null ||
            user.AccountState != AccountState.Active ||
            !user.EmailConfirmed ||
            (user.LockoutEnd is not null && user.LockoutEnd > nowUtc) ||
            !securityStampMatches ||
            !session.IsValidAt(nowUtc))
        {
            await RejectSessionAsync(context);
            return;
        }

        var tenantClaim = context.User.FindFirstValue(SessionClaimTypes.TenantId);
        if (tenantClaim is not null)
        {
            if (!Guid.TryParse(tenantClaim, out var tenantId) ||
                user.HomeTenantId != tenantId ||
                session.SelectedTenantId != tenantId)
            {
                await RejectSessionAsync(context);
                return;
            }

            tenantContext.SetFromTrustedBoundary(tenantId);

            await using var tenantTransaction = await dbContext.BeginTenantTransactionAsync(
                tenantId,
                context.RequestAborted);
            var tenantIsActive = await dbContext.Tenants.AsNoTracking().AnyAsync(
                value => value.Id == tenantId && value.State == TenantState.Active,
                context.RequestAborted);
            var membership = await dbContext.TenantMemberships.AsNoTracking().SingleOrDefaultAsync(
                value => value.UserId == userId && value.State == MembershipState.Active,
                context.RequestAborted);
            var roleClaim = context.User.FindFirstValue(SessionClaimTypes.TenantRole);
            if (!tenantIsActive ||
                membership is null ||
                !string.Equals(roleClaim, membership.Role.ToString(), StringComparison.Ordinal))
            {
                await RejectSessionAsync(context);
                return;
            }

            await tenantTransaction.CommitAsync(context.RequestAborted);
        }
        else if (user.HomeTenantId is not null || session.SelectedTenantId is not null)
        {
            await RejectSessionAsync(context);
            return;
        }
        else if (!await userManager.IsInRoleAsync(user, "PlatformAdministrator"))
        {
            await RejectSessionAsync(context);
            return;
        }

        context.Items[ValidatedSessionItemKey] = session;

        if (nowUtc - session.LastSeenAtUtc >= TimeSpan.FromMinutes(5))
        {
            session.Touch(nowUtc, UserSessionService.IdleLifetime);
            await dbContext.SaveChangesAsync(context.RequestAborted);
        }

        await next(context);
    }

    private static async Task RejectSessionAsync(HttpContext context)
    {
        await context.SignOutAsync(IdentityConstants.ApplicationScheme);
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(
            new
            {
                type = "https://docs.example.invalid/problems/session-invalid",
                title = "Authentication session is invalid or expired.",
                status = StatusCodes.Status401Unauthorized,
                code = "session_invalid"
            },
            context.RequestAborted);
    }
}
