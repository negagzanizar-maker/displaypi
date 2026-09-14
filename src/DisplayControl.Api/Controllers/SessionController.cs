using System.Security.Claims;

using DisplayControl.Api.Security;
using DisplayControl.Application.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DisplayControl.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/session")]
public sealed class SessionController(
    IAntiforgery antiforgery,
    HumanAuthenticationOptions humanAuthenticationOptions) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    public ActionResult<SessionResponse> Get()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        var isAuthenticated = User.Identity?.IsAuthenticated == true;
        return Ok(new SessionResponse(
            isAuthenticated,
            isAuthenticated ? User.FindFirstValue(SessionClaimTypes.AuthenticationStage) : null,
            tokens.RequestToken ?? throw new InvalidOperationException("Antiforgery service returned no request token."),
            isAuthenticated ? User.FindFirstValue(ClaimTypes.NameIdentifier) : null,
            isAuthenticated ? User.FindFirstValue(ClaimTypes.Email) : null,
            isAuthenticated ? User.FindFirstValue(SessionClaimTypes.DisplayName) : null,
            isAuthenticated ? User.FindFirstValue(SessionClaimTypes.TenantId) : null,
            isAuthenticated ? User.FindFirstValue(SessionClaimTypes.TenantRole) : null,
            isAuthenticated && User.HasClaim(SessionClaimTypes.AuthenticationMethod, "mfa"),
            humanAuthenticationOptions.RequireMfa));
    }
}

public sealed record SessionResponse(
    bool Authenticated,
    string? AuthenticationStage,
    string CsrfToken,
    string? UserId,
    string? Email,
    string? DisplayName,
    string? TenantId,
    string? TenantRole,
    bool MfaSatisfied,
    bool MfaRequired);
