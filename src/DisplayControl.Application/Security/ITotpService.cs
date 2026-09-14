namespace DisplayControl.Application.Security;

public interface ITotpService
{
    public byte[] GenerateSecret();

    public string EncodeBase32(ReadOnlySpan<byte> secret);

    public string CreateOtpAuthUri(string issuer, string accountName, ReadOnlySpan<byte> secret);

    public TotpVerificationResult Verify(
        ReadOnlySpan<byte> secret,
        string code,
        DateTimeOffset nowUtc,
        int allowedAdjacentSteps = 1);
}
