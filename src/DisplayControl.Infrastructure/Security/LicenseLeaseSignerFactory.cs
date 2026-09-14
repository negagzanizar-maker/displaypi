using System.Security.Cryptography;

namespace DisplayControl.Infrastructure.Security;

public static class LicenseLeaseSignerFactory
{
    public static EcdsaLicenseLeaseSigner LoadFileBacked(string privateKeyPath, string password,
        IReadOnlyList<string>? verificationPublicKeyPaths = null)
    {
        if (string.IsNullOrWhiteSpace(privateKeyPath) || !Path.IsPathFullyQualified(privateKeyPath))
        {
            throw new InvalidOperationException("The licence signing key path must be absolute.");
        }

        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException("The licence signing key password is required.");
        }

        var fullPath = Path.GetFullPath(privateKeyPath);
        var file = new FileInfo(fullPath);
        if (!file.Exists || file.LinkTarget is not null || file.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException("The licence signing key file is missing or is a reparse point.");
        }

        var privateKey = ECDsa.Create();
        try
        {
            privateKey.ImportFromEncryptedPem(File.ReadAllText(fullPath), password);
            if (verificationPublicKeyPaths is { Count: > 3 })
            {
                throw new InvalidOperationException("At most three additional verification keys are supported.");
            }

            var publicKeys = new List<string>();
            foreach (var path in verificationPublicKeyPaths ?? [])
            {
                if (!Path.IsPathFullyQualified(path))
                {
                    throw new InvalidOperationException("Verification key paths must be absolute.");
                }

                var publicFile = new FileInfo(path);
                if (!publicFile.Exists || publicFile.Length > 4096 || publicFile.LinkTarget is not null ||
                    publicFile.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new InvalidOperationException("Verification key file is missing, too large, or a reparse point.");
                }

                publicKeys.Add(File.ReadAllText(path));
            }

            return new EcdsaLicenseLeaseSigner(privateKey, publicKeys);
        }
        catch
        {
            privateKey.Dispose();
            throw;
        }
    }

    public static EcdsaLicenseLeaseSigner CreateEphemeralForTesting() =>
        new(ECDsa.Create(ECCurve.NamedCurves.nistP256));
}
