using DisplayControl.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace DisplayControl.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;

    public AccountState AccountState { get; set; } = AccountState.Active;

    public Guid? HomeTenantId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public DateTimeOffset? LastPasswordChangedAtUtc { get; set; }
}
