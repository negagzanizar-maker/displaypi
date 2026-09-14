using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;

namespace DisplayControl.Api.Security;

public sealed record SessionIssueResult(
    ClaimsPrincipal Principal,
    AuthenticationProperties AuthenticationProperties,
    string RawSessionKey);
