namespace DisplayControl.Domain.Identity;

public sealed class UserRecoveryCode
{
    private UserRecoveryCode()
    {
    }

    public UserRecoveryCode(Guid id, Guid userId, byte[] codeDigest, DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty || userId == Guid.Empty)
        {
            throw new ArgumentException("Recovery code and user identifiers cannot be empty.");
        }

        ArgumentNullException.ThrowIfNull(codeDigest);
        if (codeDigest.Length != 32)
        {
            throw new ArgumentException("Recovery code digest must contain exactly 32 bytes.", nameof(codeDigest));
        }

        if (createdAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must have a UTC offset.", nameof(createdAtUtc));
        }

        Id = id;
        UserId = userId;
        CodeDigest = codeDigest.ToArray();
        CreatedAtUtc = createdAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    public Guid Id { get; private init; }

    public Guid UserId { get; private set; }

    public byte[] CodeDigest { get; private set; } = [];

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? UsedAtUtc { get; private set; }

    public Guid ConcurrencyToken { get; private set; }

    public void MarkUsed(DateTimeOffset usedAtUtc)
    {
        if (usedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must have a UTC offset.", nameof(usedAtUtc));
        }

        if (UsedAtUtc is not null)
        {
            throw new InvalidOperationException("Recovery code is already used.");
        }

        if (usedAtUtc < CreatedAtUtc)
        {
            throw new ArgumentException("Use cannot predate recovery code creation.", nameof(usedAtUtc));
        }

        UsedAtUtc = usedAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }
}
