using System.Security.Cryptography;

using DisplayControl.Application.Security;
using DisplayControl.Domain.Licensing;

namespace DisplayControl.Infrastructure.Security;

public sealed class EcdsaLicenseLeaseSigner : ILicenseLeaseSigner, IDisposable
{
    private readonly ECDsa _privateKey;
    private readonly object _signingLock = new();

    public EcdsaLicenseLeaseSigner(ECDsa privateKey, IReadOnlyList<string>? additionalPublicKeys = null)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        var parameters = privateKey.ExportParameters(includePrivateParameters: false);
        if (!string.Equals(parameters.Curve.Oid.Value, ECCurve.NamedCurves.nistP256.Oid.Value, StringComparison.Ordinal))
        {
            throw new ArgumentException("The licence signing key must use ECDSA P-256.", nameof(privateKey));
        }

        _privateKey = privateKey;
        var publicKeyBytes = privateKey.ExportSubjectPublicKeyInfo();
        var keyDigest = SHA256.HashData(publicKeyBytes);
        var keyId = Convert.ToHexString(keyDigest.AsSpan(0, 16)).ToLowerInvariant();
        VerificationKey = new LicenseLeaseVerificationKey(
            keyId,
            "ES256",
            PemEncoding.WriteString("PUBLIC KEY", publicKeyBytes));
        if (additionalPublicKeys is { Count: > 3 })
        {
            throw new ArgumentException("At most three additional verification keys are supported.", nameof(additionalPublicKeys));
        }

        var keys = new List<LicenseLeaseVerificationKey> { VerificationKey };
        foreach (var pem in additionalPublicKeys ?? [])
        {
            if (!pem.StartsWith("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal))
            {
                throw new ArgumentException("Additional verification keys must be public SPKI PEM keys.", nameof(additionalPublicKeys));
            }

            using var key = ECDsa.Create();
            key.ImportFromPem(pem);
            if (key.ExportParameters(false).Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
            {
                throw new ArgumentException("Verification keys must use P-256.", nameof(additionalPublicKeys));
            }

            var bytes = key.ExportSubjectPublicKeyInfo();
            var id = Convert.ToHexString(SHA256.HashData(bytes).AsSpan(0, 16)).ToLowerInvariant();
            if (keys.Any(existing => existing.KeyId == id))
            {
                throw new ArgumentException("Verification keys must be distinct.", nameof(additionalPublicKeys));
            }

            keys.Add(new LicenseLeaseVerificationKey(id, "ES256", key.ExportSubjectPublicKeyInfoPem()));
        }

        VerificationKeys = keys.AsReadOnly();
    }

    public LicenseLeaseVerificationKey VerificationKey { get; }

    public IReadOnlyList<LicenseLeaseVerificationKey> VerificationKeys { get; }

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
        DateTimeOffset? earlierAuthorizationBoundaryUtc = null)
    {
        if (tenantId == Guid.Empty || deviceId == Guid.Empty || certificateId == Guid.Empty ||
            license.TenantId != tenantId || license.DeviceId != deviceId ||
            (desiredStateId is null) != (desiredStateVersion is null) ||
            (desiredStateId is null) != (desiredStateManifestSha256 is null) ||
            desiredStateId == Guid.Empty || desiredStateVersion is <= 0)
        {
            throw new InvalidOperationException("Lease bindings are invalid.");
        }

        if (license.ControlState != LicenseControlState.Enabled)
        {
            throw new InvalidOperationException("Only an enabled active licence can issue a lease.");
        }

        var expiresAtUtc = new LicenseWindow(license.ValidFromUtc, license.ExpiresAtUtc).CalculateLeaseExpiry(
            serverNowUtc,
            offlineAllowance,
            earlierAuthorizationBoundaryUtc);
        var leaseId = Guid.NewGuid();
        var payload = new LicenseLeasePayload(
            1,
            VerificationKey.KeyId,
            leaseId,
            tenantId,
            deviceId,
            certificateId,
            license.Id,
            serverNowUtc,
            serverNowUtc.AddMinutes(-2),
            expiresAtUtc,
            desiredStateId,
            desiredStateVersion,
            desiredStateManifestSha256 is null
                ? null
                : Convert.ToHexString(ValidateManifestHash(desiredStateManifestSha256)).ToLowerInvariant());
        string token;
        lock (_signingLock)
        {
            token = LicenseLeaseTokenCodec.CreateSignedToken(payload, _privateKey);
        }

        return new SignedLicenseLease(token, VerificationKey.KeyId, serverNowUtc, expiresAtUtc, leaseId);
    }

    public void Dispose() => _privateKey.Dispose();

    private static byte[] ValidateManifestHash(byte[] value) => value is { Length: 32 }
        ? value
        : throw new InvalidOperationException("Desired-state manifest SHA-256 must contain exactly 32 bytes.");
}
