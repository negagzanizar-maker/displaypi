using DisplayControl.Application.Security;
using Microsoft.AspNetCore.DataProtection;

namespace DisplayControl.Infrastructure.Security;

public sealed class DataProtectionMfaSecretProtector : IMfaSecretProtector
{
    public const string CurrentProtectionScheme = "aspnet-data-protection:display-control:mfa-totp:v1";
    private readonly IDataProtector _protector;

    public DataProtectionMfaSecretProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector("DisplayControl", "MfaTotpSecrets", "v1");
    }

    public string ProtectionScheme => CurrentProtectionScheme;

    public byte[] Protect(ReadOnlySpan<byte> secret)
    {
        if (secret.IsEmpty)
        {
            throw new ArgumentException("MFA secret cannot be empty.", nameof(secret));
        }

        return _protector.Protect(secret.ToArray());
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedSecret)
    {
        if (protectedSecret.IsEmpty)
        {
            throw new ArgumentException("Protected MFA secret cannot be empty.", nameof(protectedSecret));
        }

        return _protector.Unprotect(protectedSecret.ToArray());
    }
}
