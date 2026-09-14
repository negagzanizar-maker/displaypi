namespace DisplayControl.Domain.Operations;

public sealed class SystemKeyMetadata
{
    private SystemKeyMetadata()
    {
    }

    public string KeyId { get; private set; } = string.Empty;

    public string Algorithm { get; private set; } = string.Empty;

    public string Purpose { get; private set; } = string.Empty;

    public byte[] PublicKeyDer { get; private set; } = [];

    public DateTimeOffset ActivatesAtUtc { get; private set; }

    public DateTimeOffset? RetiresAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
