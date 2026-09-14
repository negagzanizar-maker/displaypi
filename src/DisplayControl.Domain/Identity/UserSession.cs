namespace DisplayControl.Domain.Identity;

public sealed class UserSession
{
    private UserSession()
    {
    }

    public UserSession(
        Guid id,
        Guid userId,
        byte[] sessionKeyDigest,
        byte[] securityStampDigest,
        DateTimeOffset createdAtUtc,
        TimeSpan idleLifetime,
        TimeSpan absoluteLifetime,
        Guid? selectedTenantId = null,
        byte[]? userAgentDigest = null,
        byte[]? sourceAddressDigest = null)
    {
        if (id == Guid.Empty || userId == Guid.Empty || selectedTenantId == Guid.Empty)
        {
            throw new ArgumentException("Session, user, and optional tenant identifiers must be non-empty.");
        }

        EnsureDigest(sessionKeyDigest, nameof(sessionKeyDigest));
        EnsureDigest(securityStampDigest, nameof(securityStampDigest));
        EnsureOptionalDigest(userAgentDigest, nameof(userAgentDigest));
        EnsureOptionalDigest(sourceAddressDigest, nameof(sourceAddressDigest));
        EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        if (idleLifetime <= TimeSpan.Zero || absoluteLifetime < idleLifetime)
        {
            throw new ArgumentException("Session lifetimes must be positive and the absolute lifetime must bound idle lifetime.");
        }

        Id = id;
        UserId = userId;
        SessionKeyDigest = sessionKeyDigest.ToArray();
        SecurityStampDigest = securityStampDigest.ToArray();
        SelectedTenantId = selectedTenantId;
        CreatedAtUtc = createdAtUtc;
        LastSeenAtUtc = createdAtUtc;
        IdleExpiresAtUtc = createdAtUtc.Add(idleLifetime);
        AbsoluteExpiresAtUtc = createdAtUtc.Add(absoluteLifetime);
        UserAgentDigest = userAgentDigest?.ToArray();
        SourceAddressDigest = sourceAddressDigest?.ToArray();
        ConcurrencyToken = Guid.NewGuid();
    }

    public Guid Id { get; private init; }

    public Guid UserId { get; private set; }

    public byte[] SessionKeyDigest { get; private set; } = [];

    public Guid? SelectedTenantId { get; private set; }

    public byte[] SecurityStampDigest { get; private set; } = [];

    public bool MfaSatisfied { get; private set; }

    public DateTimeOffset? MfaSatisfiedAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset LastSeenAtUtc { get; private set; }

    public DateTimeOffset IdleExpiresAtUtc { get; private set; }

    public DateTimeOffset AbsoluteExpiresAtUtc { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public string? RevocationReasonCode { get; private set; }

    public byte[]? UserAgentDigest { get; private set; }

    public byte[]? SourceAddressDigest { get; private set; }

    public Guid ConcurrencyToken { get; private set; }

    public bool IsValidAt(DateTimeOffset nowUtc) =>
        nowUtc.Offset == TimeSpan.Zero &&
        nowUtc >= CreatedAtUtc &&
        RevokedAtUtc is null &&
        nowUtc < IdleExpiresAtUtc &&
        nowUtc < AbsoluteExpiresAtUtc;

    public void MarkMfaSatisfied(DateTimeOffset satisfiedAtUtc)
    {
        EnsureUtc(satisfiedAtUtc, nameof(satisfiedAtUtc));
        if (!IsValidAt(satisfiedAtUtc))
        {
            throw new InvalidOperationException("Expired or revoked session cannot satisfy MFA.");
        }

        if (MfaSatisfiedAtUtc is not null && satisfiedAtUtc <= MfaSatisfiedAtUtc.Value)
        {
            throw new InvalidOperationException("A new MFA verification must be later than the previous verification.");
        }

        MfaSatisfied = true;
        MfaSatisfiedAtUtc = satisfiedAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    public bool HasRecentMfaAt(DateTimeOffset nowUtc, TimeSpan maximumAge)
    {
        EnsureUtc(nowUtc, nameof(nowUtc));
        if (maximumAge <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAge), "Maximum MFA age must be positive.");
        }

        return MfaSatisfied &&
            MfaSatisfiedAtUtc is DateTimeOffset satisfiedAtUtc &&
            satisfiedAtUtc <= nowUtc &&
            nowUtc - satisfiedAtUtc <= maximumAge;
    }

    public void RotateAfterMfa(byte[] newSessionKeyDigest, DateTimeOffset satisfiedAtUtc)
    {
        EnsureDigest(newSessionKeyDigest, nameof(newSessionKeyDigest));
        if (SessionKeyDigest.AsSpan().SequenceEqual(newSessionKeyDigest))
        {
            throw new ArgumentException("MFA must issue a different session credential.", nameof(newSessionKeyDigest));
        }

        MarkMfaSatisfied(satisfiedAtUtc);
        SessionKeyDigest = newSessionKeyDigest.ToArray();
    }

    public void Touch(DateTimeOffset seenAtUtc, TimeSpan idleLifetime)
    {
        EnsureUtc(seenAtUtc, nameof(seenAtUtc));
        if (idleLifetime <= TimeSpan.Zero || !IsValidAt(seenAtUtc) || seenAtUtc < LastSeenAtUtc)
        {
            throw new InvalidOperationException("Session cannot be extended.");
        }

        LastSeenAtUtc = seenAtUtc;
        var proposedIdleExpiry = seenAtUtc.Add(idleLifetime);
        IdleExpiresAtUtc = proposedIdleExpiry < AbsoluteExpiresAtUtc
            ? proposedIdleExpiry
            : AbsoluteExpiresAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    public void Revoke(string reasonCode, DateTimeOffset revokedAtUtc)
    {
        EnsureUtc(revokedAtUtc, nameof(revokedAtUtc));
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        var normalizedReason = reasonCode.Trim();
        if (normalizedReason.Length is < 1 or > 64)
        {
            throw new ArgumentException("Revocation reason code must contain 1-64 characters.", nameof(reasonCode));
        }

        if (RevokedAtUtc is not null)
        {
            throw new InvalidOperationException("Session is already revoked.");
        }

        if (revokedAtUtc < CreatedAtUtc)
        {
            throw new ArgumentException("Revocation cannot predate session creation.", nameof(revokedAtUtc));
        }

        RevokedAtUtc = revokedAtUtc;
        RevocationReasonCode = normalizedReason;
        ConcurrencyToken = Guid.NewGuid();
    }

    private static void EnsureOptionalDigest(byte[]? value, string parameterName)
    {
        if (value is not null)
        {
            EnsureDigest(value, parameterName);
        }
    }

    private static void EnsureDigest(byte[] value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Length != 32)
        {
            throw new ArgumentException("Digest must contain exactly 32 bytes.", parameterName);
        }
    }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must have a UTC offset.", parameterName);
        }
    }
}
