using DisplayControl.Domain.Tenancy;

namespace DisplayControl.Domain.Identity;

public sealed class Invitation : TenantOwnedEntity
{
    private Invitation()
    {
    }

    public Invitation(
        Guid id,
        Guid tenantId,
        string normalizedEmail,
        TenantRole intendedRole,
        byte[] tokenDigest,
        DateTimeOffset expiresAtUtc,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || createdByUserId == Guid.Empty)
        {
            throw new ArgumentException("Invitation, tenant, and creator identifiers cannot be empty.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedEmail);
        var email = normalizedEmail.Trim();
        if (email.Length is < 3 or > 320 || !email.Contains('@'))
        {
            throw new ArgumentException("Normalized invitation email is invalid.", nameof(normalizedEmail));
        }

        ArgumentNullException.ThrowIfNull(tokenDigest);
        if (tokenDigest.Length != 32)
        {
            throw new ArgumentException("Invitation token digest must contain exactly 32 bytes.", nameof(tokenDigest));
        }

        EnsureUtc(expiresAtUtc, nameof(expiresAtUtc));
        EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        if (expiresAtUtc <= createdAtUtc)
        {
            throw new ArgumentException("Invitation expiry must be after creation.", nameof(expiresAtUtc));
        }

        Id = id;
        TenantId = tenantId;
        NormalizedEmail = email;
        IntendedRole = intendedRole;
        TokenDigest = tokenDigest.ToArray();
        ExpiresAtUtc = expiresAtUtc;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    public string NormalizedEmail { get; private set; } = string.Empty;

    public TenantRole IntendedRole { get; private set; }

    public byte[] TokenDigest { get; private set; } = [];

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? ConsumedAtUtc { get; private set; }

    public Guid? ConsumedByUserId { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid ConcurrencyToken { get; private set; }

    public bool CanBeConsumedAt(DateTimeOffset nowUtc) =>
        nowUtc.Offset == TimeSpan.Zero &&
        nowUtc >= CreatedAtUtc &&
        nowUtc < ExpiresAtUtc &&
        ConsumedAtUtc is null &&
        RevokedAtUtc is null;

    public void Consume(Guid userId, string normalizedEmail, DateTimeOffset consumedAtUtc)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User identifier cannot be empty.", nameof(userId));
        }

        EnsureUtc(consumedAtUtc, nameof(consumedAtUtc));
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedEmail);
        if (!CanBeConsumedAt(consumedAtUtc))
        {
            throw new InvalidOperationException("Invitation is not consumable.");
        }

        if (!string.Equals(NormalizedEmail, normalizedEmail.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invitation email does not match the accepting account.");
        }

        ConsumedByUserId = userId;
        ConsumedAtUtc = consumedAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    public void Revoke(DateTimeOffset revokedAtUtc)
    {
        EnsureUtc(revokedAtUtc, nameof(revokedAtUtc));
        if (revokedAtUtc < CreatedAtUtc)
        {
            throw new ArgumentException("Revocation cannot predate invitation creation.", nameof(revokedAtUtc));
        }

        if (ConsumedAtUtc is not null || RevokedAtUtc is not null)
        {
            throw new InvalidOperationException("Consumed or revoked invitation cannot be revoked.");
        }

        RevokedAtUtc = revokedAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must have a UTC offset.", parameterName);
        }
    }
}
