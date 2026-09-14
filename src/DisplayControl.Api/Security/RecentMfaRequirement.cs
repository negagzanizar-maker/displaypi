using DisplayControl.Domain.Identity;
using Microsoft.AspNetCore.Authorization;

namespace DisplayControl.Api.Security;

public sealed record RecentMfaRequirement(TimeSpan MaximumAge) : IAuthorizationRequirement
{
    public static readonly TimeSpan DefaultMaximumAge = TimeSpan.FromMinutes(10);
}

public sealed class RecentMfaAuthorizationHandler(TimeProvider timeProvider)
    : AuthorizationHandler<RecentMfaRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RecentMfaRequirement requirement)
    {
        if (requirement.MaximumAge <= TimeSpan.Zero)
        {
            return Task.CompletedTask;
        }

        if (context.Resource is HttpContext httpContext &&
            httpContext.Items.TryGetValue(SessionValidationMiddleware.ValidatedSessionItemKey, out var item) &&
            item is UserSession session &&
            session.HasRecentMfaAt(timeProvider.GetUtcNow(), requirement.MaximumAge))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
