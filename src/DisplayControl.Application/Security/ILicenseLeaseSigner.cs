using DisplayControl.Domain.Licensing;

namespace DisplayControl.Application.Security;

public interface ILicenseLeaseSigner
{
    public LicenseLeaseVerificationKey VerificationKey { get; }

    public IReadOnlyList<LicenseLeaseVerificationKey> VerificationKeys => [VerificationKey];

    public SignedLicenseLease Issue(
        Guid tenantId,
        Guid deviceId,
        Guid certificateId,
        DeviceLicense license,
        DateTimeOffset serverNowUtc,
        TimeSpan offlineAllowance,
        Guid? desiredStateId,
        long? desiredStateVersion,
        byte[]? desiredStateManifestSha256,
        DateTimeOffset? earlierAuthorizationBoundaryUtc = null);
}

public sealed record LicenseLeaseVerificationKey(
    string KeyId,
    string Algorithm,
    string SubjectPublicKeyInfoPem);

public sealed record SignedLicenseLease(
    string Token,
    string KeyId,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    Guid LeaseId);

public sealed record LicenseLeasePayload(
    int Version,
    string KeyId,
    Guid LeaseId,
    Guid TenantId,
    Guid DeviceId,
    Guid CertificateId,
    Guid LicenseId,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset NotBeforeUtc,
    DateTimeOffset ExpiresAtUtc,
    Guid? DesiredStateId,
    long? DesiredStateVersion,
    string? DesiredStateManifestSha256);
