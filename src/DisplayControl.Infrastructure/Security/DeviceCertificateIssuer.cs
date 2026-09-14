using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using DisplayControl.Application.Security;

namespace DisplayControl.Infrastructure.Security;

public sealed class DeviceCertificateIssuer : IDeviceCertificateIssuer, IDisposable
{
    private const string EcPublicKeyOid = "1.2.840.10045.2.1";
    private const string P256CurveOid = "1.2.840.10045.3.1.7";
    private const string ClientAuthenticationOid = "1.3.6.1.5.5.7.3.2";
    private const string DeviceIdentityExtensionOid = "1.3.6.1.4.1.55555.1.1";
    private readonly X509Certificate2 _authority;
    private readonly TimeSpan _certificateLifetime;

    public DeviceCertificateIssuer(X509Certificate2 authority, TimeSpan certificateLifetime)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (!authority.HasPrivateKey)
        {
            throw new ArgumentException("The device certificate authority must contain its private key.", nameof(authority));
        }

        if (certificateLifetime < TimeSpan.FromHours(1) || certificateLifetime > TimeSpan.FromDays(90))
        {
            throw new ArgumentOutOfRangeException(
                nameof(certificateLifetime),
                "Device certificate lifetime must be between one hour and ninety days.");
        }

        var basicConstraints = authority.Extensions.OfType<X509BasicConstraintsExtension>().SingleOrDefault();
        if (basicConstraints is null || !basicConstraints.CertificateAuthority)
        {
            throw new ArgumentException("The configured certificate is not a certificate authority.", nameof(authority));
        }

        _authority = authority;
        _certificateLifetime = certificateLifetime;
    }

    public string CertificateAuthorityPem => _authority.ExportCertificatePem();

    public byte[] GetSigningRequestPublicKeySha256(string certificateSigningRequestPem)
    {
        var incomingRequest = LoadAndValidateSigningRequest(certificateSigningRequestPem);
        return SHA256.HashData(incomingRequest.PublicKey.ExportSubjectPublicKeyInfo());
    }

    public IssuedDeviceCertificate Issue(
        Guid tenantId,
        Guid deviceId,
        string certificateSigningRequestPem,
        DateTimeOffset issuedAtUtc)
    {
        if (tenantId == Guid.Empty || deviceId == Guid.Empty)
        {
            throw new ArgumentException("Tenant and device identifiers cannot be empty.");
        }

        if (issuedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Certificate issuance time must be UTC.", nameof(issuedAtUtc));
        }

        var incomingRequest = LoadAndValidateSigningRequest(certificateSigningRequestPem);

        var subject = new X500DistinguishedName(
            $"CN=device-{deviceId:N}, OU={tenantId:N}, O=DisplayControl Devices");
        var sanitizedRequest = new CertificateRequest(subject, incomingRequest.PublicKey, HashAlgorithmName.SHA256);
        sanitizedRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        sanitizedRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature,
            critical: true));
        sanitizedRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new(ClientAuthenticationOid, "TLS Web Client Authentication") },
            critical: true));
        sanitizedRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(
            sanitizedRequest.PublicKey,
            critical: false));
        var alternativeNames = new SubjectAlternativeNameBuilder();
        alternativeNames.AddUri(new Uri($"urn:display-control:device:{tenantId:N}:{deviceId:N}"));
        sanitizedRequest.CertificateExtensions.Add(alternativeNames.Build(critical: false));
        sanitizedRequest.CertificateExtensions.Add(CreateDeviceIdentityExtension(tenantId, deviceId));

        var notBeforeUtc = issuedAtUtc.AddMinutes(-2);
        var requestedNotAfter = issuedAtUtc.Add(_certificateLifetime);
        var authorityNotAfter = new DateTimeOffset(_authority.NotAfter.ToUniversalTime(), TimeSpan.Zero).AddMinutes(-1);
        var notAfterUtc = requestedNotAfter <= authorityNotAfter ? requestedNotAfter : authorityNotAfter;
        if (notAfterUtc <= issuedAtUtc)
        {
            throw new CryptographicException("The device certificate authority expires too soon.");
        }

        var serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7F;
        if (serial.All(value => value == 0))
        {
            serial[^1] = 1;
        }

        using var certificate = sanitizedRequest.Create(_authority, notBeforeUtc, notAfterUtc, serial);
        using var issuedPublicKey = certificate.GetECDsaPublicKey()
            ?? throw new CryptographicException("Issued certificate does not contain an ECDSA public key.");
        return new IssuedDeviceCertificate(
            certificate.ExportCertificatePem(),
            CertificateAuthorityPem,
            certificate.RawData,
            certificate.SerialNumber,
            SHA256.HashData(certificate.RawData),
            SHA256.HashData(issuedPublicKey.ExportSubjectPublicKeyInfo()),
            notBeforeUtc,
            notAfterUtc);
    }

    public bool TryValidateClientCertificate(
        X509Certificate2 certificate,
        DateTimeOffset nowUtc,
        out DeviceCertificateIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        identity = new DeviceCertificateIdentity(Guid.Empty, Guid.Empty);
        if (nowUtc.Offset != TimeSpan.Zero || certificate.HasPrivateKey)
        {
            return false;
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(_authority);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.DisableCertificateDownloads = true;
        chain.ChainPolicy.VerificationTime = nowUtc.UtcDateTime;
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid(ClientAuthenticationOid));
        if (!chain.Build(certificate))
        {
            return false;
        }

        var identityExtensions = certificate.Extensions
            .Where(extension => string.Equals(extension.Oid?.Value, DeviceIdentityExtensionOid, StringComparison.Ordinal))
            .ToArray();
        if (identityExtensions.Length != 1)
        {
            return false;
        }

        try
        {
            var reader = new AsnReader(identityExtensions[0].RawData, AsnEncodingRules.DER);
            var sequence = reader.ReadSequence();
            var tenantBytes = sequence.ReadOctetString();
            var deviceBytes = sequence.ReadOctetString();
            sequence.ThrowIfNotEmpty();
            reader.ThrowIfNotEmpty();
            if (tenantBytes.Length != 16 || deviceBytes.Length != 16)
            {
                return false;
            }

            var tenantId = new Guid(tenantBytes);
            var deviceId = new Guid(deviceBytes);
            if (tenantId == Guid.Empty || deviceId == Guid.Empty)
            {
                return false;
            }

            identity = new DeviceCertificateIdentity(tenantId, deviceId);
            return true;
        }
        catch (AsnContentException)
        {
            return false;
        }
    }

    public void Dispose() => _authority.Dispose();

    private static void EnsureP256PublicKey(PublicKey publicKey)
    {
        if (!string.Equals(publicKey.Oid?.Value, EcPublicKeyOid, StringComparison.Ordinal))
        {
            throw new CryptographicException("Only ECDSA P-256 device keys are accepted.");
        }

        var encodedParameters = publicKey.EncodedParameters?.RawData;
        if (encodedParameters is null)
        {
            throw new CryptographicException("The ECDSA curve parameters are missing.");
        }

        var reader = new AsnReader(encodedParameters, AsnEncodingRules.DER);
        var curveOid = reader.ReadObjectIdentifier();
        reader.ThrowIfNotEmpty();
        if (!string.Equals(curveOid, P256CurveOid, StringComparison.Ordinal))
        {
            throw new CryptographicException("Only ECDSA P-256 device keys are accepted.");
        }
    }

    private static CertificateRequest LoadAndValidateSigningRequest(string certificateSigningRequestPem)
    {
        if (string.IsNullOrWhiteSpace(certificateSigningRequestPem) || certificateSigningRequestPem.Length > 16_384)
        {
            throw new CryptographicException("The certificate signing request is missing or too large.");
        }

        var request = CertificateRequest.LoadSigningRequestPem(
            certificateSigningRequestPem,
            HashAlgorithmName.SHA256,
            CertificateRequestLoadOptions.Default);
        EnsureP256PublicKey(request.PublicKey);
        return request;
    }

    private static X509Extension CreateDeviceIdentityExtension(Guid tenantId, Guid deviceId)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            writer.WriteOctetString(tenantId.ToByteArray());
            writer.WriteOctetString(deviceId.ToByteArray());
        }

        return new X509Extension(DeviceIdentityExtensionOid, writer.Encode(), critical: false);
    }
}
