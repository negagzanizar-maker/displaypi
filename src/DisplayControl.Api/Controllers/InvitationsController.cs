using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using DisplayControl.Api.Identity;
using DisplayControl.Api.Security;
using DisplayControl.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DisplayControl.Api.Controllers;

[ApiController]
public sealed class InvitationsController(InvitationWorkflow workflow) : ControllerBase
{
    [HttpPost("api/v1/tenants/{tenantId:guid}/invitations")]
    [Authorize(Policy = AuthorizationPolicies.TenantAdministrator)]
    [ProducesResponseType<InvitationCreatedResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<InvitationCreatedResponse>> Create(
        Guid tenantId,
        CreateInvitationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await workflow.CreateAsync(
            tenantId,
            ParseCurrentUserId(),
            request,
            cancellationToken);
        return result.Outcome switch
        {
            InvitationCreateOutcome.Created => Created(
                $"/api/v1/tenants/{tenantId}/invitations/{result.Response!.Id}",
                result.Response),
            InvitationCreateOutcome.InvalidEmail => InvalidEmail(),
            InvitationCreateOutcome.PendingInvitationExists => ConflictProblem(
                "pending_invitation_exists",
                "A pending invitation already exists for this email."),
            _ => throw new InvalidOperationException("Unsupported invitation creation outcome.")
        };
    }

    [HttpPost("api/v1/invitations/accept")]
    [AllowAnonymous]
    [EnableRateLimiting("authentication")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Accept(
        AcceptInvitationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await workflow.AcceptAsync(request, cancellationToken);
        return result.Outcome switch
        {
            InvitationAcceptOutcome.Accepted => NoContent(),
            InvitationAcceptOutcome.Invalid => InvalidInvitation(),
            InvitationAcceptOutcome.AccountExists => ConflictProblem(
                "account_exists_sign_in_required",
                "An account already exists for this invitation. Sign in to continue."),
            InvitationAcceptOutcome.IdentityValidationFailed =>
                IdentityValidationProblem(result.IdentityErrors ?? []),
            _ => throw new InvalidOperationException("Unsupported invitation acceptance outcome.")
        };
    }

    private Guid ParseCurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId)
            ? userId
            : throw new InvalidOperationException("Authenticated principal has no valid user identifier.");
    }

    private static BadRequestObjectResult InvalidEmail() => new(new ValidationProblemDetails(
        new Dictionary<string, string[]> { ["email"] = ["A valid email address is required."] })
    {
        Type = "https://docs.example.invalid/problems/validation",
        Title = "The request could not be accepted.",
        Status = StatusCodes.Status400BadRequest,
        Extensions = { ["code"] = "validation_failed" }
    });

    private static BadRequestObjectResult InvalidInvitation() => new(new ProblemDetails
    {
        Type = "https://docs.example.invalid/problems/invitation-invalid",
        Title = "The invitation is invalid, expired, or already used.",
        Status = StatusCodes.Status400BadRequest,
        Extensions = { ["code"] = "invitation_invalid" }
    });

    private static ObjectResult ConflictProblem(string code, string title) => new(new ProblemDetails
    {
        Type = "https://docs.example.invalid/problems/invitation-state",
        Title = title,
        Status = StatusCodes.Status409Conflict,
        Extensions = { ["code"] = code }
    })
    {
        StatusCode = StatusCodes.Status409Conflict
    };

    private static ObjectResult IdentityValidationProblem(IEnumerable<IdentityError> errors)
    {
        var safeErrors = errors.Select(error => error.Code switch
        {
            "PasswordTooShort" => "Password does not meet the minimum length.",
            "PasswordRequiresDigit" => "Password must contain a digit.",
            "PasswordRequiresLower" => "Password must contain a lowercase letter.",
            "PasswordRequiresUpper" => "Password must contain an uppercase letter.",
            "PasswordRequiresUniqueChars" => "Password must contain more unique characters.",
            _ => "Account details could not be accepted."
        }).Distinct(StringComparer.Ordinal).ToArray();
        return new ObjectResult(new ValidationProblemDetails(new Dictionary<string, string[]>
        {
            ["account"] = safeErrors
        })
        {
            Type = "https://docs.example.invalid/problems/validation",
            Title = "The request could not be accepted.",
            Status = StatusCodes.Status400BadRequest,
            Extensions = { ["code"] = "validation_failed" }
        })
        {
            StatusCode = StatusCodes.Status400BadRequest
        };
    }
}

public sealed record CreateInvitationRequest(
    [param: Required, EmailAddress, StringLength(320)] string Email,
    TenantRole Role);

public sealed record AcceptInvitationRequest(
    [param: Required, StringLength(4096, MinimumLength = 16)] string Token,
    [param: Required, StringLength(160, MinimumLength = 1)] string DisplayName,
    [param: Required, StringLength(1024, MinimumLength = 15)] string Password);

public sealed record InvitationCreatedResponse(
    Guid Id,
    string Email,
    TenantRole Role,
    DateTimeOffset ExpiresAtUtc,
    string Token);

internal sealed record InvitationNotificationPayload(string Token);
