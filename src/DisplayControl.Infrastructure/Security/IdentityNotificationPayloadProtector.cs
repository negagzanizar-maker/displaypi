using System.Text;

using DisplayControl.Application.Security;
using Microsoft.AspNetCore.DataProtection;

namespace DisplayControl.Infrastructure.Security;

public sealed class IdentityNotificationPayloadProtector : ISensitivePayloadProtector
{
    public const string CurrentProtectionScheme = "aspnet-data-protection:display-control:identity-notifications:v1";
    private readonly IDataProtector _protector;

    public IdentityNotificationPayloadProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector("DisplayControl", "IdentityNotifications", "v1");
    }

    public string ProtectionScheme => CurrentProtectionScheme;

    public byte[] Protect(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        return _protector.Protect(Encoding.UTF8.GetBytes(plaintext));
    }

    public string Unprotect(ReadOnlySpan<byte> protectedPayload)
    {
        if (protectedPayload.IsEmpty)
        {
            throw new ArgumentException("Protected payload cannot be empty.", nameof(protectedPayload));
        }

        return Encoding.UTF8.GetString(_protector.Unprotect(protectedPayload.ToArray()));
    }
}
