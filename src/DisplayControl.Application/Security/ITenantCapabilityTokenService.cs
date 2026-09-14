namespace DisplayControl.Application.Security;

public interface ITenantCapabilityTokenService
{
    public GeneratedSecretToken Generate(Guid tenantId);

    public bool TryReadTenantId(string token, out Guid tenantId);

    public byte[] ComputeDigest(string token);
}
