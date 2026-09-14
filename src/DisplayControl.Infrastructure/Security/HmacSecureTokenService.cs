using System.Security.Cryptography;
using System.Text;

using DisplayControl.Application.Security;

namespace DisplayControl.Infrastructure.Security;

public sealed class HmacSecureTokenService : ISecureTokenService
{
    private const int MinimumEntropyBytes = 16;
    private const int MaximumEntropyBytes = 64;
    private readonly byte[] _pepper;

    public HmacSecureTokenService(byte[] pepper)
    {
        ArgumentNullException.ThrowIfNull(pepper);
        if (pepper.Length < 32)
        {
            throw new ArgumentException("Token digest pepper must contain at least 32 random bytes.", nameof(pepper));
        }

        _pepper = pepper.ToArray();
    }

    public GeneratedSecretToken Generate(int entropyBytes = 32)
    {
        if (entropyBytes is < MinimumEntropyBytes or > MaximumEntropyBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(entropyBytes),
                $"Token entropy must contain {MinimumEntropyBytes}-{MaximumEntropyBytes} bytes.");
        }

        var randomBytes = RandomNumberGenerator.GetBytes(entropyBytes);
        try
        {
            var value = ToBase64Url(randomBytes);
            return new GeneratedSecretToken(value, ComputeDigest(value));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(randomBytes);
        }
    }

    public byte[] ComputeDigest(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        var tokenBytes = Encoding.UTF8.GetBytes(token);
        try
        {
            return HMACSHA256.HashData(_pepper, tokenBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tokenBytes);
        }
    }

    public bool VerifyDigest(string token, ReadOnlySpan<byte> expectedDigest)
    {
        if (expectedDigest.Length != 32 || string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var actualDigest = ComputeDigest(token);
        try
        {
            return CryptographicOperations.FixedTimeEquals(actualDigest, expectedDigest);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(actualDigest);
        }
    }

    private static string ToBase64Url(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
