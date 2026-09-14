using DisplayControl.Domain.Tenancy;

namespace DisplayControl.Domain.Scheduling;

public sealed class DesiredState : TenantOwnedEntity
{
    private DesiredState()
    {
    }

    public DesiredState(
        Guid id,
        Guid tenantId,
        Guid deviceId,
        long version,
        Guid sourceDeviceAssignmentId,
        DateTimeOffset? startsAtUtc,
        DateTimeOffset? endsAtUtc,
        byte[] manifestSha256,
        DateTimeOffset publishedAtUtc)
        : this(
            id,
            tenantId,
            deviceId,
            version,
            sourceDeviceAssignmentId,
            null,
            startsAtUtc,
            endsAtUtc,
            manifestSha256,
            publishedAtUtc)
    {
    }

    private DesiredState(
        Guid id,
        Guid tenantId,
        Guid deviceId,
        long version,
        Guid? sourceDeviceAssignmentId,
        Guid? sourceGroupAssignmentId,
        DateTimeOffset? startsAtUtc,
        DateTimeOffset? endsAtUtc,
        byte[] manifestSha256,
        DateTimeOffset publishedAtUtc)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || deviceId == Guid.Empty ||
            (sourceDeviceAssignmentId is null) == (sourceGroupAssignmentId is null) ||
            sourceDeviceAssignmentId == Guid.Empty || sourceGroupAssignmentId == Guid.Empty)
        {
            throw new ArgumentException("Desired-state identifiers are invalid.");
        }

        if (version <= 0 || manifestSha256 is not { Length: 32 })
        {
            throw new ArgumentException("Desired-state version and SHA-256 are invalid.");
        }

        AssignmentSchedule.EnsureWindow(startsAtUtc, endsAtUtc);
        AssignmentSchedule.EnsureUtc(publishedAtUtc, nameof(publishedAtUtc));
        Id = id;
        TenantId = tenantId;
        DeviceId = deviceId;
        Version = version;
        SourceDeviceAssignmentId = sourceDeviceAssignmentId;
        SourceGroupAssignmentId = sourceGroupAssignmentId;
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
        ManifestSha256 = [.. manifestSha256];
        CreatedAtUtc = publishedAtUtc;
        PublishedAtUtc = publishedAtUtc;
    }

    public Guid DeviceId { get; private set; }

    public long Version { get; private set; }

    public Guid? SourceDeviceAssignmentId { get; private set; }

    public Guid? SourceGroupAssignmentId { get; private set; }

    public DateTimeOffset? StartsAtUtc { get; private set; }

    public DateTimeOffset? EndsAtUtc { get; private set; }

    public byte[] ManifestSha256 { get; private set; } = [];

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset PublishedAtUtc { get; private set; }

    public Guid? SupersededByDesiredStateId { get; private set; }

    public static DesiredState FromGroupAssignment(
        Guid id,
        Guid tenantId,
        Guid deviceId,
        long version,
        Guid sourceGroupAssignmentId,
        DateTimeOffset? startsAtUtc,
        DateTimeOffset? endsAtUtc,
        byte[] manifestSha256,
        DateTimeOffset publishedAtUtc) => new(
            id,
            tenantId,
            deviceId,
            version,
            null,
            sourceGroupAssignmentId,
            startsAtUtc,
            endsAtUtc,
            manifestSha256,
            publishedAtUtc);

    public void Supersede(Guid supersededByDesiredStateId)
    {
        if (supersededByDesiredStateId == Guid.Empty || supersededByDesiredStateId == Id)
        {
            throw new ArgumentException("Superseding desired-state identifier is invalid.", nameof(supersededByDesiredStateId));
        }

        if (SupersededByDesiredStateId.HasValue)
        {
            throw new InvalidOperationException("Desired state has already been superseded.");
        }

        SupersededByDesiredStateId = supersededByDesiredStateId;
    }
}
