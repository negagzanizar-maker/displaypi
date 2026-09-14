using System.Globalization;
using System.Text;
using DisplayControl.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace DisplayControl.Api.Security;

public sealed class CommonPasswordValidator : IPasswordValidator<ApplicationUser>
{
    private static readonly HashSet<string> CommonPasswords = new(StringComparer.Ordinal)
    {
        "123456789012345",
        "adminadminadmin",
        "changemechangeme",
        "displaycontroldisplaycontrol",
        "letmeinletmeinletmein",
        "passwordpassword",
        "password123456",
        "qwertyuiopasdfgh",
        "welcome123456789"
    };

    public Task<IdentityResult> ValidateAsync(
        UserManager<ApplicationUser> manager,
        ApplicationUser user,
        string? password)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(user);
        if (string.IsNullOrEmpty(password))
        {
            return Task.FromResult(IdentityResult.Success);
        }

        var normalized = password.Normalize(NormalizationForm.FormKC).ToLower(CultureInfo.InvariantCulture);
        var emailLocalPart = user.Email?.Split('@', 2)[0].Normalize(NormalizationForm.FormKC)
            .ToLower(CultureInfo.InvariantCulture);
        var displayName = user.DisplayName?.Normalize(NormalizationForm.FormKC)
            .ToLower(CultureInfo.InvariantCulture)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        var repeatedCharacter = normalized.Distinct().Take(2).Count() == 1;
        var containsPersonalValue =
            (emailLocalPart is { Length: >= 4 } && normalized.Contains(emailLocalPart, StringComparison.Ordinal)) ||
            (displayName is { Length: >= 4 } && normalized.Replace(" ", string.Empty, StringComparison.Ordinal)
                .Contains(displayName, StringComparison.Ordinal));

        return CommonPasswords.Contains(normalized) || repeatedCharacter || containsPersonalValue
            ? Task.FromResult(IdentityResult.Failed(new IdentityError
            {
                Code = "CommonPassword",
                Description = "The password is too common or contains account information."
            }))
            : Task.FromResult(IdentityResult.Success);
    }
}
