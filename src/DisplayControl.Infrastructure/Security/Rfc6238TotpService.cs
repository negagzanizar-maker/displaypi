using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using DisplayControl.Application.Security;

namespace DisplayControl.Infrastructure.Security;

public sealed class Rfc6238TotpService : ITotpService
{
    public const int TimeStepSeconds = 30;
    public const int CodeDigits = 6;
    private const int SecretBytes = 20;
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public byte[] GenerateSecret() => RandomNumberGenerator.GetBytes(SecretBytes);

    public string EncodeBase32(ReadOnlySpan<byte> secret)
    {
        if (secret.IsEmpty)
        {
            throw new ArgumentException("TOTP secret cannot be empty.", nameof(secret));
        }

        var output = new StringBuilder((secret.Length * 8 + 4) / 5);
        var buffer = 0;
        var bitsInBuffer = 0;
        foreach (var value in secret)
        {
            buffer = (buffer << 8) | value;
            bitsInBuffer += 8;
            while (bitsInBuffer >= 5)
            {
                bitsInBuffer -= 5;
                output.Append(Base32Alphabet[(buffer >> bitsInBuffer) & 31]);
            }
        }

        if (bitsInBuffer > 0)
        {
            output.Append(Base32Alphabet[(buffer << (5 - bitsInBuffer)) & 31]);
        }

        return output.ToString();
    }

    public string CreateOtpAuthUri(string issuer, string accountName, ReadOnlySpan<byte> secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        var label = Uri.EscapeDataString($"{issuer}:{accountName}");
        var encodedIssuer = Uri.EscapeDataString(issuer);
        return $"otpauth://totp/{label}?secret={EncodeBase32(secret)}&issuer={encodedIssuer}&algorithm=SHA1&digits={CodeDigits}&period={TimeStepSeconds}";
    }

    public TotpVerificationResult Verify(
        ReadOnlySpan<byte> secret,
        string code,
        DateTimeOffset nowUtc,
        int allowedAdjacentSteps = 1)
    {
        if (secret.IsEmpty ||
            nowUtc.Offset != TimeSpan.Zero ||
            allowedAdjacentSteps is < 0 or > 2 ||
            !IsWellFormedCode(code))
        {
            return new TotpVerificationResult(false, null);
        }

        var currentStep = nowUtc.ToUnixTimeSeconds() / TimeStepSeconds;
        for (var distance = 0; distance <= allowedAdjacentSteps; distance++)
        {
            if (distance > 0 && currentStep >= distance && Matches(secret, code, currentStep - distance))
            {
                return new TotpVerificationResult(true, currentStep - distance);
            }

            if (Matches(secret, code, currentStep + distance))
            {
                return new TotpVerificationResult(true, currentStep + distance);
            }
        }

        return new TotpVerificationResult(false, null);
    }

    [SuppressMessage(
        "Security",
        "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "RFC 6238 TOTP uses HMAC-SHA1 for broad authenticator interoperability; this is keyed MAC usage, not collision-sensitive hashing.")]
    private static bool Matches(ReadOnlySpan<byte> secret, string suppliedCode, long timeStep)
    {
        Span<byte> counter = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(counter, timeStep);
        Span<byte> digest = stackalloc byte[20];
        HMACSHA1.HashData(secret, counter, digest);
        var offset = digest[^1] & 0x0F;
        var binaryCode = BinaryPrimitives.ReadInt32BigEndian(digest.Slice(offset, 4)) & 0x7FFFFFFF;
        var expectedCode = (binaryCode % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        Span<byte> expectedBytes = stackalloc byte[CodeDigits];
        Span<byte> suppliedBytes = stackalloc byte[CodeDigits];
        Encoding.ASCII.GetBytes(expectedCode, expectedBytes);
        Encoding.ASCII.GetBytes(suppliedCode, suppliedBytes);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }

    private static bool IsWellFormedCode(string? code)
    {
        if (code is null || code.Length != CodeDigits)
        {
            return false;
        }

        foreach (var character in code)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
