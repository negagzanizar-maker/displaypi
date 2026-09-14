namespace DisplayControl.Application.Security;

public interface IMfaSecretProtector
{
    public string ProtectionScheme { get; }

    public byte[] Protect(ReadOnlySpan<byte> secret);

    public byte[] Unprotect(ReadOnlySpan<byte> protectedSecret);
}
