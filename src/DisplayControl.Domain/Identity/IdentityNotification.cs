namespace DisplayControl.Domain.Identity;

public sealed class IdentityNotification
{
    public const int MaximumProtectedPayloadBytes = 16 * 1024;

    private IdentityNotification()
    {
    }

    public IdentityNotification(
        Guid id,
        Guid? userId,
        Guid? tenantId,
        string notificationType,
        string normalizedRecipientEmail,
        string protectionScheme,
        byte[] protectedPayload,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty || userId == Guid.Empty || tenantId == Guid.Empty)
        {
            throw new ArgumentException("Notification, user, and optional tenant identifiers must be non-empty.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(notificationType);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedRecipientEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(protectionScheme);
        ArgumentNullException.ThrowIfNull(protectedPayload);
        if (notificationType.Length > 64 || normalizedRecipientEmail.Length > 320 || protectionScheme.Length > 128)
        {
            throw new ArgumentException("Notification metadata exceeds its allowed length.");
        }

        if (protectedPayload.Length is 0 or > MaximumProtectedPayloadBytes)
        {
            throw new ArgumentException("Protected notification payload has an invalid size.", nameof(protectedPayload));
        }

        if (createdAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must have a UTC offset.", nameof(createdAtUtc));
        }

        Id = id;
        UserId = userId;
        TenantId = tenantId;
        NotificationType = notificationType;
        NormalizedRecipientEmail = normalizedRecipientEmail;
        ProtectionScheme = protectionScheme;
        ProtectedPayload = protectedPayload.ToArray();
        CreatedAtUtc = createdAtUtc;
        NextAttemptAtUtc = createdAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    public Guid Id { get; private init; }

    public Guid? UserId { get; private set; }

    public Guid? TenantId { get; private set; }

    public string NotificationType { get; private set; } = string.Empty;

    public string NormalizedRecipientEmail { get; private set; } = string.Empty;

    public string ProtectionScheme { get; private set; } = string.Empty;

    public byte[] ProtectedPayload { get; private set; } = [];

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset NextAttemptAtUtc { get; private set; }

    public DateTimeOffset? ProcessedAtUtc { get; private set; }

    public DateTimeOffset? FailedAtUtc { get; private set; }

    public int AttemptCount { get; private set; }

    public string? LastSafeErrorCode { get; private set; }

    public Guid ConcurrencyToken { get; private set; }
}
