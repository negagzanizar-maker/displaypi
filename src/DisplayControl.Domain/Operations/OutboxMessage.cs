using DisplayControl.Domain.Tenancy;

namespace DisplayControl.Domain.Operations;

public sealed class OutboxMessage : TenantOwnedEntity
{
    private OutboxMessage()
    {
    }

    public string MessageType { get; private set; } = string.Empty;

    public string PayloadJson { get; private set; } = "{}";

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public DateTimeOffset? ProcessedAtUtc { get; private set; }

    public int AttemptCount { get; private set; }

    public string? LastSafeError { get; private set; }
}
