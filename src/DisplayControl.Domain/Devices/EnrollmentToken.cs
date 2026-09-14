using DisplayControl.Domain.Tenancy;

namespace DisplayControl.Domain.Devices;

public sealed class EnrollmentToken : TenantOwnedEntity
{
    public const int MaximumFailedAttempts = 10;

    private EnrollmentToken()
    {
    }

    public EnrollmentToken(
        Guid id,
        Guid tenantId,
        byte[] tokenDigest,
        DateTimeOffset expiresAtUtc,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc,
        Guid? expectedDeviceId = null,
        string? expectedSerialNumberNormalized = null)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || createdByUserId == Guid.Empty)
        {
            throw new ArgumentException("Enrollment, tenant, and creator identifiers cannot be empty.");
        }

        if (expectedDeviceId == Guid.Empty)
        {
            throw new ArgumentException("An expected device identifier cannot be empty.", nameof(expectedDeviceId));
        }

        ArgumentNullException.ThrowIfNull(tokenDigest);
        if (tokenDigest.Length != 32)
        {
            throw new ArgumentException("Enrollment token digest must contain exactly 32 bytes.", nameof(tokenDigest));
        }

        EnsureUtc(expiresAtUtc, nameof(expiresAtUtc));
        EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        if (expiresAtUtc <= createdAtUtc)
        {
            throw new ArgumentException("Enrollment expiry must be after creation.", nameof(expiresAtUtc));
        }

        var normalizedSerial = expectedSerialNumberNormalized?.Trim().ToUpperInvariant();
        if (normalizedSerial is not null && (normalizedSerial.Length is 0 or > 64))
        {
            throw new ArgumentException("Expected serial must contain 1-64 characters.", nameof(expectedSerialNumberNormalized));
        }

        Id = id;
        TenantId = tenantId;
        TokenDigest = tokenDigest.ToArray();
        ExpectedDeviceId = expectedDeviceId;
        ExpectedSerialNumberNormalized = normalizedSerial;
        ExpiresAtUtc = expiresAtUtc;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    public byte[] TokenDigest { get; private set; } = [];

    public Guid? ExpectedDeviceId { get; private set; }

    public string? ExpectedSerialNumberNormalized { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? ConsumedAtUtc { get; private set; }

    public Guid? ConsumedByDeviceId { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public int FailedAttemptCount { get; private set; }

    public DateTimeOffset? LastFailedAttemptAtUtc { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid ConcurrencyToken { get; private set; }

    public bool CanBeConsumedAt(DateTimeOffset nowUtc) =>
        nowUtc.Offset == TimeSpan.Zero &&
        nowUtc >= CreatedAtUtc &&
        nowUtc < ExpiresAtUtc &&
        ConsumedAtUtc is null &&
        RevokedAtUtc is null &&
        FailedAttemptCount < MaximumFailedAttempts;

    public void RecordFailedAttempt(DateTimeOffset occurredAtUtc)
    {
        EnsureUtc(occurredAtUtc, nameof(occurredAtUtc));
        if (ConsumedAtUtc is not null || RevokedAtUtc is not null)
        {
            throw new InvalidOperationException("A consumed or revoked enrollment token cannot record attempts.");
        }

        FailedAttemptCount = Math.Min(FailedAttemptCount + 1, MaximumFailedAttempts);
        LastFailedAttemptAtUtc = occurredAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    public void Consume(Guid deviceId, DateTimeOffset consumedAtUtc)
    {
        if (deviceId == Guid.Empty)
        {
            throw new ArgumentException("Device identifier cannot be empty.", nameof(deviceId));
        }

        EnsureUtc(consumedAtUtc, nameof(consumedAtUtc));
        if (!CanBeConsumedAt(consumedAtUtc))
        {
            throw new InvalidOperationException("Enrollment token is not consumable.");
        }

        if (ExpectedDeviceId is not null && ExpectedDeviceId != deviceId)
        {
            throw new InvalidOperationException("Enrollment token is bound to a different device.");
        }

        ConsumedByDeviceId = deviceId;
        ConsumedAtUtc = consumedAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    public void Revoke(DateTimeOffset revokedAtUtc)
    {
        EnsureUtc(revokedAtUtc, nameof(revokedAtUtc));
        if (ConsumedAtUtc is not null)
        {
            throw new InvalidOperationException("A consumed enrollment token cannot be revoked.");
        }

        if (RevokedAtUtc is null)
        {
            RevokedAtUtc = revokedAtUtc;
            ConcurrencyToken = Guid.NewGuid();
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
