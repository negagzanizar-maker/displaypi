using DisplayControl.Domain.Tenancy;

namespace DisplayControl.Domain.Devices;

public sealed class DeviceSynchronizationEvent : TenantOwnedEntity
{
    private DeviceSynchronizationEvent()
    {
    }

    public Guid DeviceId { get; private set; }

    public long? DesiredStateVersion { get; private set; }

    public string EventType { get; private set; } = string.Empty;

    public string ResultCode { get; private set; } = string.Empty;

    public string SafeDetailsJson { get; private set; } = "{}";

    public DateTimeOffset? ReportedAtUtc { get; private set; }

    public DateTimeOffset ReceivedAtUtc { get; private set; }

    public Guid CorrelationId { get; private set; }
}
