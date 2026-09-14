namespace DisplayControl.Application.Security;

public interface ISecureTokenService
{
    public GeneratedSecretToken Generate(int entropyBytes = 32);

    public byte[] ComputeDigest(string token);

    public bool VerifyDigest(string token, ReadOnlySpan<byte> expectedDigest);
}
