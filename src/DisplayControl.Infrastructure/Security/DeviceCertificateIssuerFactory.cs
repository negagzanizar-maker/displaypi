using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace DisplayControl.Infrastructure.Security;

public static class DeviceCertificateIssuerFactory
{
    public static DeviceCertificateIssuer LoadFileBacked(
        string certificatePath,
        string certificatePassword,
        TimeSpan certificateLifetime)
    {
        if (string.IsNullOrWhiteSpace(certificatePath) || !Path.IsPathFullyQualified(certificatePath))
        {
            throw new InvalidOperationException("The device CA path must be an absolute path.");
        }

        if (string.IsNullOrEmpty(certificatePassword))
        {
            throw new InvalidOperationException("The device CA password is required.");
        }

        var fullPath = Path.GetFullPath(certificatePath);
        var file = new FileInfo(fullPath);
        if (!file.Exists || file.LinkTarget is not null || file.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException("The device CA file is missing or is a reparse point.");
        }

        var authority = X509CertificateLoader.LoadPkcs12FromFile(
            fullPath,
            certificatePassword,
            X509KeyStorageFlags.EphemeralKeySet);
        try
        {
            return new DeviceCertificateIssuer(authority, certificateLifetime);
        }
        catch
        {
            authority.Dispose();
            throw;
        }
    }

    public static DeviceCertificateIssuer CreateEphemeralForTesting(
        DateTimeOffset nowUtc,
        TimeSpan certificateLifetime)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(
            "CN=DisplayControl Ephemeral Device CA, O=DisplayControl Tests",
            key,
            HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
            critical: true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        var authority = request.CreateSelfSigned(nowUtc.AddDays(-1), nowUtc.AddYears(1));
        return new DeviceCertificateIssuer(authority, certificateLifetime);
    }
}
