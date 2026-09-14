using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

using DisplayControl.Application.Security;
using DisplayControl.Domain.Licensing;
using DisplayControl.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;

namespace DisplayControl.IntegrationTests.Security;

public sealed class CryptographicIdentityServicesTests
{
    [Fact]
    public void LeaseVerificationTrustSetIncludesCurrentAndRetiringKeysAndRejectsInvalidKeys()
    {
        using var retiring = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var signer = new EcdsaLicenseLeaseSigner(ECDsa.Create(ECCurve.NamedCurves.nistP256),
            [retiring.ExportSubjectPublicKeyInfoPem()]);
        Assert.Equal(2, signer.VerificationKeys.Count);
        Assert.Equal(signer.VerificationKey, signer.VerificationKeys[0]);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(retiring.ExportSubjectPublicKeyInfo()).AsSpan(0, 16)),
            signer.VerificationKeys[1].KeyId);

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Throws<ArgumentException>(() => new EcdsaLicenseLeaseSigner(key, [key.ExportSubjectPublicKeyInfoPem()]));
        using var wrongCurve = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        Assert.Throws<ArgumentException>(() => new EcdsaLicenseLeaseSigner(key, [wrongCurve.ExportSubjectPublicKeyInfoPem()]));
        Assert.Throws<ArgumentException>(() => new EcdsaLicenseLeaseSigner(key, [retiring.ExportECPrivateKeyPem()]));
    }

    [Fact]
    public void SecureTokenIsHighEntropyUrlSafeAndPepperBound()
    {
        var service = new HmacSecureTokenService(Enumerable.Repeat((byte)0xA5, 32).ToArray());
        var otherService = new HmacSecureTokenService(Enumerable.Repeat((byte)0x5A, 32).ToArray());

        var generated = service.Generate();

        Assert.Equal(43, generated.Value.Length);
        Assert.DoesNotContain('+', generated.Value);
        Assert.DoesNotContain('/', generated.Value);
        Assert.DoesNotContain('=', generated.Value);
        Assert.Equal(32, generated.Digest.Length);
        Assert.True(service.VerifyDigest(generated.Value, generated.Digest));
        Assert.False(service.VerifyDigest(generated.Value + "x", generated.Digest));
        Assert.False(otherService.VerifyDigest(generated.Value, generated.Digest));
    }

    [Theory]
    [InlineData(59, "287082")]
    [InlineData(1_111_111_109, "081804")]
    [InlineData(1_111_111_111, "050471")]
    public void TotpMatchesRfc6238Sha1VectorsTruncatedToSixDigits(long unixSeconds, string expectedCode)
    {
        var service = new Rfc6238TotpService();
        var secret = Encoding.ASCII.GetBytes("12345678901234567890");
        var now = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);

        var result = service.Verify(secret, expectedCode, now, allowedAdjacentSteps: 0);

        Assert.True(result.IsValid);
        Assert.Equal(unixSeconds / Rfc6238TotpService.TimeStepSeconds, result.TimeStep);
    }

    [Fact]
    public void TotpRejectsMalformedCodeAndNonUtcTimestamp()
    {
        var service = new Rfc6238TotpService();
        var secret = Encoding.ASCII.GetBytes("12345678901234567890");

        Assert.False(service.Verify(secret, "12345", DateTimeOffset.UnixEpoch).IsValid);
        Assert.False(service.Verify(secret, "12A456", DateTimeOffset.UnixEpoch).IsValid);
        Assert.False(service.Verify(secret, "755224", DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(1))).IsValid);
    }

    [Fact]
    public void TotpProvisioningUriUsesExpectedStandardParameters()
    {
        var service = new Rfc6238TotpService();

        var uri = service.CreateOtpAuthUri("Display Control", "admin@example.test", [0x01, 0x02, 0x03]);

        Assert.StartsWith("otpauth://totp/Display%20Control%3Aadmin%40example.test?", uri, StringComparison.Ordinal);
        Assert.Contains("secret=AEBAG", uri, StringComparison.Ordinal);
        Assert.Contains("algorithm=SHA1&digits=6&period=30", uri, StringComparison.Ordinal);
    }

    [Fact]
    public void MfaSecretProtectorRoundTripsWithoutPlaintextPersistence()
    {
        var protector = new DataProtectionMfaSecretProtector(new EphemeralDataProtectionProvider());
        byte[] secret = [1, 2, 3, 4, 5];

        var protectedSecret = protector.Protect(secret);
        var recovered = protector.Unprotect(protectedSecret);

        Assert.NotEqual(secret, protectedSecret);
        Assert.Equal(secret, recovered);
        Assert.Equal(DataProtectionMfaSecretProtector.CurrentProtectionScheme, protector.ProtectionScheme);
    }

    [Fact]
    public void TenantCapabilityUsesLocatorOnlyAndDigestsTheCompleteToken()
    {
        var digestService = new HmacSecureTokenService(Enumerable.Repeat((byte)0xA5, 32).ToArray());
        var capabilityService = new TenantCapabilityTokenService(digestService);
        var tenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        var capability = capabilityService.Generate(tenantId);

        Assert.StartsWith("v1.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.", capability.Value, StringComparison.Ordinal);
        Assert.True(capabilityService.TryReadTenantId(capability.Value, out var parsedTenantId));
        Assert.Equal(tenantId, parsedTenantId);
        Assert.True(digestService.VerifyDigest(capability.Value, capability.Digest));
        Assert.Equal(capability.Digest, capabilityService.ComputeDigest(capability.Value));
        Assert.False(capabilityService.TryReadTenantId("v1.not-a-tenant.secret", out _));
    }

    [Fact]
    public void IdentityNotificationPayloadUsesAnIndependentProtectionPurpose()
    {
        var provider = new EphemeralDataProtectionProvider();
        var notificationProtector = new IdentityNotificationPayloadProtector(provider);
        var mfaProtector = new DataProtectionMfaSecretProtector(provider);
        const string plaintext = "{\"token\":\"reset-secret\"}";

        var protectedPayload = notificationProtector.Protect(plaintext);

        Assert.Equal(plaintext, notificationProtector.Unprotect(protectedPayload));
        Assert.ThrowsAny<CryptographicException>(() => mfaProtector.Unprotect(protectedPayload));
        Assert.Equal(
            IdentityNotificationPayloadProtector.CurrentProtectionScheme,
            notificationProtector.ProtectionScheme);
    }

    [Fact]
    public void DeviceCertificateIssuerVerifiesCsrAndRebuildsRestrictedClientCertificate()
    {
        var nowUtc = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        using var issuer = DeviceCertificateIssuerFactory.CreateEphemeralForTesting(
            nowUtc,
            TimeSpan.FromDays(30));
        using var deviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var incoming = new CertificateRequest("CN=attacker-controlled", deviceKey, HashAlgorithmName.SHA256);
        incoming.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 5, true));

        var issued = issuer.Issue(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            incoming.CreateSigningRequestPem(),
            nowUtc);

        using var leaf = X509Certificate2.CreateFromPem(issued.CertificatePem);
        using var authority = X509Certificate2.CreateFromPem(issued.CertificateAuthorityPem);
        using var leafPublicKey = leaf.GetECDsaPublicKey();
        Assert.NotNull(leafPublicKey);
        Assert.False(leaf.HasPrivateKey);
        Assert.Contains("CN=device-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", leaf.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain("attacker-controlled", leaf.Subject, StringComparison.Ordinal);
        var constraints = Assert.Single(leaf.Extensions.OfType<X509BasicConstraintsExtension>());
        Assert.False(constraints.CertificateAuthority);
        Assert.Equal(issued.SubjectPublicKeyInfoSha256, SHA256.HashData(leafPublicKey.ExportSubjectPublicKeyInfo()));
        Assert.Equal(issued.ThumbprintSha256, SHA256.HashData(leaf.RawData));
        Assert.True(issuer.TryValidateClientCertificate(leaf, nowUtc, out var identity));
        Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), identity.TenantId);
        Assert.Equal(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), identity.DeviceId);

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(authority);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        Assert.True(chain.Build(leaf), string.Join(", ", chain.ChainStatus.Select(value => value.StatusInformation)));
    }

    [Fact]
    public void DeviceCertificateIssuerRejectsRsaDeviceCsr()
    {
        var nowUtc = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        using var issuer = DeviceCertificateIssuerFactory.CreateEphemeralForTesting(nowUtc, TimeSpan.FromDays(30));
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=device", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);

        Assert.Throws<CryptographicException>(() => issuer.Issue(
            Guid.NewGuid(),
            Guid.NewGuid(),
            request.CreateSigningRequestPem(),
            nowUtc));
    }

    [Fact]
    public void SignedLeaseIsBoundCappedAndRejectsTampering()
    {
        var nowUtc = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        var tenantId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var certificateId = Guid.NewGuid();
        var license = new DeviceLicense(
            Guid.NewGuid(),
            tenantId,
            deviceId,
            nowUtc.AddDays(-1),
            nowUtc.AddDays(10),
            nowUtc.AddDays(-1));
        using var signer = LicenseLeaseSignerFactory.CreateEphemeralForTesting();

        var lease = signer.Issue(
            tenantId,
            deviceId,
            certificateId,
            license,
            nowUtc,
            TimeSpan.FromHours(24),
            null,
            null,
            null,
            nowUtc.AddHours(12));

        Assert.Equal(nowUtc.AddHours(12), lease.ExpiresAtUtc);
        Assert.True(LicenseLeaseTokenCodec.TryValidate(
            lease.Token,
            signer.VerificationKey,
            tenantId,
            deviceId,
            certificateId,
            nowUtc,
            out var payload));
        Assert.Equal(license.Id, payload?.LicenseId);
        Assert.False(LicenseLeaseTokenCodec.TryValidate(
            lease.Token + "x",
            signer.VerificationKey,
            tenantId,
            deviceId,
            certificateId,
            nowUtc,
            out _));
        Assert.False(LicenseLeaseTokenCodec.TryValidate(
            lease.Token,
            signer.VerificationKey,
            tenantId,
            Guid.NewGuid(),
            certificateId,
            nowUtc,
            out _));
        Assert.False(LicenseLeaseTokenCodec.TryValidate(
            lease.Token,
            signer.VerificationKey,
            tenantId,
            deviceId,
            certificateId,
            lease.ExpiresAtUtc,
            out _));
    }
}
