using System.Security.Cryptography;
using System.Text.Json;

namespace DisplayControl.Application.Security;

public static class LicenseLeaseTokenCodec
{
    private const string TokenVersion = "v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string CreateSignedToken(LicenseLeasePayload payload, ECDsa privateKey)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(privateKey);
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var signature = privateKey.SignData(
            payloadBytes,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{TokenVersion}.{Base64UrlEncode(payloadBytes)}.{Base64UrlEncode(signature)}";
    }

    public static bool TryValidate(
        string token,
        LicenseLeaseVerificationKey verificationKey,
        Guid expectedTenantId,
        Guid expectedDeviceId,
        Guid expectedCertificateId,
        DateTimeOffset trustedNowUtc,
        out LicenseLeasePayload? payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 8192 || trustedNowUtc.Offset != TimeSpan.Zero)
        {
            return false;
        }

        var segments = token.Split('.');
        if (segments.Length != 3 || !string.Equals(segments[0], TokenVersion, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var payloadBytes = Base64UrlDecode(segments[1]);
            var signature = Base64UrlDecode(segments[2]);
            using var publicKey = ECDsa.Create();
            publicKey.ImportFromPem(verificationKey.SubjectPublicKeyInfoPem);
            if (!publicKey.VerifyData(
                    payloadBytes,
                    signature,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                return false;
            }

            var candidate = JsonSerializer.Deserialize<LicenseLeasePayload>(payloadBytes, JsonOptions);
            if (candidate is null ||
                candidate.Version != 1 ||
                !string.Equals(candidate.KeyId, verificationKey.KeyId, StringComparison.Ordinal) ||
                !string.Equals(verificationKey.Algorithm, "ES256", StringComparison.Ordinal) ||
                candidate.LeaseId == Guid.Empty ||
                candidate.TenantId != expectedTenantId ||
                candidate.DeviceId != expectedDeviceId ||
                candidate.CertificateId != expectedCertificateId ||
                candidate.LicenseId == Guid.Empty ||
                candidate.IssuedAtUtc.Offset != TimeSpan.Zero ||
                candidate.NotBeforeUtc.Offset != TimeSpan.Zero ||
                candidate.ExpiresAtUtc.Offset != TimeSpan.Zero ||
                candidate.NotBeforeUtc > candidate.IssuedAtUtc ||
                candidate.ExpiresAtUtc <= candidate.IssuedAtUtc ||
                trustedNowUtc < candidate.NotBeforeUtc ||
                trustedNowUtc >= candidate.ExpiresAtUtc ||
                (candidate.DesiredStateId is null) != (candidate.DesiredStateVersion is null) ||
                (candidate.DesiredStateId is null) != (candidate.DesiredStateManifestSha256 is null) ||
                candidate.DesiredStateId == Guid.Empty ||
                candidate.DesiredStateVersion is <= 0 ||
                candidate.DesiredStateManifestSha256 is not null &&
                !IsSha256Hex(candidate.DesiredStateManifestSha256))
            {
                return false;
            }

            payload = candidate;
            return true;
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException or JsonException)
        {
            return false;
        }
    }

    private static bool IsSha256Hex(string value) => value.Length == 64 && value.All(
        character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string Base64UrlEncode(ReadOnlySpan<byte> value) => Convert.ToBase64String(value)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        if (value.Length == 0 || value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
        {
            throw new FormatException("The value is not Base64url.");
        }

        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            0 => string.Empty,
            2 => "==",
            3 => "=",
            _ => throw new FormatException("The Base64url length is invalid.")
        };
        return Convert.FromBase64String(padded);
    }
}
