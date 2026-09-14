using DisplayControl.Application.Security;
using Microsoft.AspNetCore.Authorization;

namespace DisplayControl.Api.Security;

public sealed class TenantRouteRequirement : AuthorizationHandler<TenantRouteRequirement>, IAuthorizationRequirement
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TenantRouteRequirement requirement)
    {
        if (context.Resource is HttpContext httpContext &&
            httpContext.Request.RouteValues.TryGetValue("tenantId", out var routeValue) &&
            Guid.TryParse(Convert.ToString(routeValue, System.Globalization.CultureInfo.InvariantCulture), out var routeTenantId) &&
            Guid.TryParse(context.User.FindFirst(SessionClaimTypes.TenantId)?.Value, out var claimTenantId) &&
            routeTenantId == claimTenantId)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
