using DisplayControl.Domain.Tenancy;

namespace DisplayControl.Domain.Licensing;

public sealed class LicenseEvent : TenantOwnedEntity
{
    private LicenseEvent()
    {
    }

    public LicenseEvent(
        Guid id,
        Guid tenantId,
        Guid licenseId,
        string eventType,
        Guid actorId,
        string reason,
        string beforeJson,
        string afterJson,
        DateTimeOffset occurredAtUtc)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || licenseId == Guid.Empty || actorId == Guid.Empty)
        {
            throw new ArgumentException("Licence event identifiers cannot be empty.");
        }

        if (occurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Licence event timestamp must be UTC.", nameof(occurredAtUtc));
        }

        EventType = Normalize(eventType, 32, nameof(eventType));
        Reason = Normalize(reason, 1000, nameof(reason));
        ValidateJsonSnapshot(beforeJson, nameof(beforeJson));
        ValidateJsonSnapshot(afterJson, nameof(afterJson));
        Id = id;
        TenantId = tenantId;
        LicenseId = licenseId;
        ActorType = "HumanUser";
        ActorId = actorId;
        BeforeJson = beforeJson;
        AfterJson = afterJson;
        OccurredAtUtc = occurredAtUtc;
    }

    public Guid LicenseId { get; private set; }

    public string EventType { get; private set; } = string.Empty;

    public string ActorType { get; private set; } = string.Empty;

    public Guid? ActorId { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public string BeforeJson { get; private set; } = "{}";

    public string AfterJson { get; private set; } = "{}";

    public Guid? SourceDeviceId { get; private set; }

    public Guid? DestinationDeviceId { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    private static string Normalize(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"Value cannot exceed {maximumLength} characters.", parameterName);
    }

    private static void ValidateJsonSnapshot(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 8192)
        {
            throw new ArgumentException("Licence event snapshots must be bounded JSON.", parameterName);
        }
    }
}
