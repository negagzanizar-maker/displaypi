using System.Security.Claims;

using DisplayControl.Application.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace DisplayControl.Infrastructure.Identity;

public sealed class ApplicationClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IOptions<IdentityOptions> optionsAccessor)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole<Guid>>(userManager, roleManager, optionsAccessor)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(SessionClaimTypes.DisplayName, user.DisplayName));
        return identity;
    }
}
