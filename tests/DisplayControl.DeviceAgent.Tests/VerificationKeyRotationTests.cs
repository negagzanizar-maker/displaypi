using System.Security.Cryptography;

using DisplayControl.Application.Security;

namespace DisplayControl.DeviceAgent.Tests;

public sealed class VerificationKeyRotationTests
{
    [Fact]
    public void AuthenticatedRotationSelectsLeaseKeyAndRejectsMalformedTrustSets()
    {
        using var previous = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var current = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var oldKey = Describe(previous);
        var newKey = Describe(current);
        var response = new HeartbeatResponseContract(DateTimeOffset.UtcNow, 30, "licensed",
            new LeaseContract("token", newKey.KeyId, DateTimeOffset.UtcNow.AddHours(1), "ES256", newKey.SubjectPublicKeyInfoPem),
            null, new DesiredStateContract("licensedNoContent", null, null), [oldKey, newKey]);

        Assert.Equal(newKey, DeviceControlClient.SelectAuthenticatedVerificationKey(response, oldKey));
        Assert.Equal(oldKey, DeviceControlClient.SelectAuthenticatedVerificationKey(response with { LicenseVerificationKeys = null }, oldKey));
        Assert.Null(DeviceControlClient.SelectAuthenticatedVerificationKey(response with { LicenseVerificationKeys = [oldKey] }, oldKey));
        Assert.Null(DeviceControlClient.SelectAuthenticatedVerificationKey(response with { LicenseVerificationKeys = [newKey, newKey] }, oldKey));
        Assert.Null(DeviceControlClient.SelectAuthenticatedVerificationKey(response with { LicenseVerificationKeys = [newKey with { KeyId = "unbound" }] }, oldKey));
        Assert.Null(DeviceControlClient.SelectAuthenticatedVerificationKey(response with { LicenseVerificationKeys = [newKey with { SubjectPublicKeyInfoPem = "invalid" }] }, oldKey));
        using var unsupported = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        Assert.Null(DeviceControlClient.SelectAuthenticatedVerificationKey(response with { LicenseVerificationKeys = [Describe(unsupported)] }, oldKey));
    }

    private static LicenseLeaseVerificationKey Describe(ECDsa key) => new(
        Convert.ToHexStringLower(SHA256.HashData(key.ExportSubjectPublicKeyInfo()).AsSpan(0, 16)),
        "ES256", key.ExportSubjectPublicKeyInfoPem());
}
