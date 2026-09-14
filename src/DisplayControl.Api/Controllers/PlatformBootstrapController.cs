using System.ComponentModel.DataAnnotations;

using DisplayControl.Api.Security;
using DisplayControl.Domain.Identity;
using DisplayControl.Infrastructure.Identity;
using DisplayControl.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DisplayControl.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/bootstrap/platform-administrator")]
public sealed class PlatformBootstrapController(
    DisplayControlDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    PlatformBootstrapCredential bootstrapCredential,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("platform-bootstrap")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(
        CreatePlatformAdministratorRequest request,
        CancellationToken cancellationToken)
    {
        var suppliedToken = Request.Headers["X-Platform-Bootstrap-Token"].ToString();
        if (!bootstrapCredential.Verify(suppliedToken) ||
            await userManager.GetUsersInRoleAsync("PlatformAdministrator") is { Count: > 0 })
        {
            return NotFound();
        }

        var normalizedEmail = userManager.NormalizeEmail(request.Email.Trim());
        if (string.IsNullOrWhiteSpace(normalizedEmail) || await userManager.FindByEmailAsync(normalizedEmail) is not null)
        {
            return ValidationProblem("email", "The platform administrator email cannot be used.");
        }

        var nowUtc = timeProvider.GetUtcNow();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = normalizedEmail,
            Email = request.Email.Trim(),
            EmailConfirmed = true,
            DisplayName = request.DisplayName.Trim(),
            AccountState = AccountState.Active,
            HomeTenantId = null,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            LastPasswordChangedAtUtc = nowUtc,
            LockoutEnabled = true
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var creation = await userManager.CreateAsync(user, request.Password);
        if (!creation.Succeeded)
        {
            return IdentityValidationProblem(creation.Errors);
        }

        var roleResult = await userManager.AddToRoleAsync(user, "PlatformAdministrator");
        if (!roleResult.Succeeded)
        {
            throw new InvalidOperationException("The seeded platform administrator role is unavailable.");
        }

        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    private static ObjectResult ValidationProblem(string field, string message) => new(new ValidationProblemDetails(
        new Dictionary<string, string[]> { [field] = [message] })
    {
        Type = "https://docs.example.invalid/problems/validation",
        Title = "The request could not be accepted.",
        Status = StatusCodes.Status400BadRequest,
        Extensions = { ["code"] = "validation_failed" }
    })
    {
        StatusCode = StatusCodes.Status400BadRequest
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

public sealed record CreatePlatformAdministratorRequest(
    [param: Required, EmailAddress, StringLength(320)] string Email,
    [param: Required, StringLength(160, MinimumLength = 1)] string DisplayName,
    [param: Required, StringLength(1024, MinimumLength = 15)] string Password);
