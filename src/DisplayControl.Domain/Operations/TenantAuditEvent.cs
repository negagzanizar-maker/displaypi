using DisplayControl.Domain.Tenancy;

namespace DisplayControl.Domain.Operations;

public sealed class TenantAuditEvent : TenantOwnedEntity
{
    private TenantAuditEvent()
    {
    }

    public TenantAuditEvent(
        Guid id,
        Guid tenantId,
        string actorType,
        Guid? actorId,
        string action,
        string targetType,
        Guid? targetId,
        string outcome,
        string? reasonCode,
        Guid correlationId,
        string detailsJson,
        DateTimeOffset occurredAtUtc)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || actorId == Guid.Empty || targetId == Guid.Empty ||
            correlationId == Guid.Empty)
        {
            throw new ArgumentException("Audit identifiers must be non-empty when supplied.");
        }

        var normalizedActorType = Normalize(actorType, 32, nameof(actorType));
        var normalizedAction = Normalize(action, 128, nameof(action));
        var normalizedTargetType = Normalize(targetType, 64, nameof(targetType));
        var normalizedOutcome = Normalize(outcome, 32, nameof(outcome));
        var normalizedReasonCode = NormalizeOptional(reasonCode, 64, nameof(reasonCode));
        if (string.IsNullOrWhiteSpace(detailsJson) || detailsJson.Length > 8192)
        {
            throw new ArgumentException("Audit details JSON is required and limited to 8192 characters.", nameof(detailsJson));
        }

        if (occurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Audit timestamp must be UTC.", nameof(occurredAtUtc));
        }

        Id = id;
        TenantId = tenantId;
        ActorType = normalizedActorType;
        ActorId = actorId;
        Action = normalizedAction;
        TargetType = normalizedTargetType;
        TargetId = targetId;
        Outcome = normalizedOutcome;
        ReasonCode = normalizedReasonCode;
        CorrelationId = correlationId;
        DetailsJson = detailsJson;
        OccurredAtUtc = occurredAtUtc;
    }

    public string ActorType { get; private set; } = string.Empty;

    public Guid? ActorId { get; private set; }

    public string Action { get; private set; } = string.Empty;

    public string TargetType { get; private set; } = string.Empty;

    public Guid? TargetId { get; private set; }

    public string Outcome { get; private set; } = string.Empty;

    public string? ReasonCode { get; private set; }

    public Guid CorrelationId { get; private set; }

    public string DetailsJson { get; private set; } = "{}";

    public DateTimeOffset OccurredAtUtc { get; private set; }

    private static string Normalize(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"Value cannot exceed {maximumLength} characters.", parameterName);
    }

    private static string? NormalizeOptional(string? value, int maximumLength, string parameterName)
    {
        if (value is null)
        {
            return null;
        }

        return Normalize(value, maximumLength, parameterName);
    }
}
