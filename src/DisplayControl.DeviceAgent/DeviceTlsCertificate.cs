using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace DisplayControl.DeviceAgent;

internal static class DeviceTlsCertificate
{
    public static X509Certificate2 Create(X509Certificate2 certificate, ECDsa privateKey)
    {
        var combined = certificate.CopyWithPrivateKey(privateKey);
        if (!OperatingSystem.IsWindows())
        {
            return combined;
        }

        try
        {
            return X509CertificateLoader.LoadPkcs12(
                combined.Export(X509ContentType.Pkcs12),
                password: null,
                X509KeyStorageFlags.Exportable);
        }
        finally
        {
            combined.Dispose();
        }
    }
}
