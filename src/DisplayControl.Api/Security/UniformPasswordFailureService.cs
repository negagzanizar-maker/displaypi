using DisplayControl.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace DisplayControl.Api.Security;

public sealed class UniformPasswordFailureService
{
    private readonly IPasswordHasher<ApplicationUser> _passwordHasher;
    private readonly ApplicationUser _dummyUser;
    private readonly string _dummyPasswordHash;

    public UniformPasswordFailureService(IPasswordHasher<ApplicationUser> passwordHasher)
    {
        _passwordHasher = passwordHasher;
        _dummyUser = new ApplicationUser
        {
            Id = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            UserName = "nonexistent@example.invalid",
            Email = "nonexistent@example.invalid",
            DisplayName = "Unavailable account"
        };
        _dummyPasswordHash = passwordHasher.HashPassword(
            _dummyUser,
            "This value is generated only to equalize unknown-account password work.");
    }

    public void Perform(string suppliedPassword)
    {
        ArgumentNullException.ThrowIfNull(suppliedPassword);
        _ = _passwordHasher.VerifyHashedPassword(_dummyUser, _dummyPasswordHash, suppliedPassword);
    }
}
