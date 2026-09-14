using System.Security.Cryptography;
using System.Text;

namespace DisplayControl.Api.Security;

public sealed class PlatformBootstrapCredential
{
    private readonly byte[] _expectedDigest;

    public PlatformBootstrapCredential(string? expectedDigestBase64)
    {
        if (string.IsNullOrWhiteSpace(expectedDigestBase64))
        {
            _expectedDigest = [];
            return;
        }

        try
        {
            _expectedDigest = Convert.FromBase64String(expectedDigestBase64);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "Security:PlatformBootstrapTokenSha256Base64 must be valid Base64.",
                exception);
        }

        if (_expectedDigest.Length != SHA256.HashSizeInBytes)
        {
            throw new InvalidOperationException(
                "Security:PlatformBootstrapTokenSha256Base64 must decode to a 32-byte SHA-256 digest.");
        }
    }

    public bool Enabled => _expectedDigest.Length == SHA256.HashSizeInBytes;

    public bool Verify(string token)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(token) || token.Length > 1024)
        {
            return false;
        }

        var actualDigest = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        try
        {
            return CryptographicOperations.FixedTimeEquals(_expectedDigest, actualDigest);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(actualDigest);
        }
    }
}
