namespace DisplayControl.Application.Security;

using System.Security.Cryptography.X509Certificates;

public interface IDeviceCertificateIssuer
{
    public string CertificateAuthorityPem { get; }

    public byte[] GetSigningRequestPublicKeySha256(string certificateSigningRequestPem);

    public IssuedDeviceCertificate Issue(
        Guid tenantId,
        Guid deviceId,
        string certificateSigningRequestPem,
        DateTimeOffset issuedAtUtc);

    public bool TryValidateClientCertificate(
        X509Certificate2 certificate,
        DateTimeOffset nowUtc,
        out DeviceCertificateIdentity identity);
}

public sealed record DeviceCertificateIdentity(Guid TenantId, Guid DeviceId);

public sealed record IssuedDeviceCertificate(
    string CertificatePem,
    string CertificateAuthorityPem,
    byte[] CertificateDer,
    string SerialNumber,
    byte[] ThumbprintSha256,
    byte[] SubjectPublicKeyInfoSha256,
    DateTimeOffset NotBeforeUtc,
    DateTimeOffset NotAfterUtc);
