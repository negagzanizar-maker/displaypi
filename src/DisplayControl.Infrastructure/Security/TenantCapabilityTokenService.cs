using DisplayControl.Application.Security;

namespace DisplayControl.Infrastructure.Security;

public sealed class TenantCapabilityTokenService(ISecureTokenService secureTokenService)
    : ITenantCapabilityTokenService
{
    private const string Version = "v1";

    public GeneratedSecretToken Generate(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant identifier cannot be empty.", nameof(tenantId));
        }

        var secret = secureTokenService.Generate();
        var value = $"{Version}.{tenantId:N}.{secret.Value}";
        return new GeneratedSecretToken(value, secureTokenService.ComputeDigest(value));
    }

    public bool TryReadTenantId(string token, out Guid tenantId)
    {
        tenantId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 160)
        {
            return false;
        }

        var segments = token.Split('.');
        return segments.Length == 3 &&
            string.Equals(segments[0], Version, StringComparison.Ordinal) &&
            segments[1].Length == 32 &&
            segments[2].Length >= 22 &&
            Guid.TryParseExact(segments[1], "N", out tenantId) &&
            tenantId != Guid.Empty;
    }

    public byte[] ComputeDigest(string token)
    {
        if (!TryReadTenantId(token, out _))
        {
            throw new ArgumentException("Tenant capability token is malformed.", nameof(token));
        }

        return secureTokenService.ComputeDigest(token);
    }
}
