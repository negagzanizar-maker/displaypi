namespace DisplayControl.Application.Security;

public interface ISensitivePayloadProtector
{
    public string ProtectionScheme { get; }

    public byte[] Protect(string plaintext);

    public string Unprotect(ReadOnlySpan<byte> protectedPayload);
}
