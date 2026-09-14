using DisplayControl.Domain.Tenancy;

namespace DisplayControl.Domain.Scheduling;

public sealed class DeviceAssignment : TenantOwnedEntity
{
    private DeviceAssignment()
    {
    }

    public DeviceAssignment(
        Guid id,
        Guid tenantId,
        Guid deviceId,
        Guid playlistVersionId,
        int priority,
        DateTimeOffset? startsAtUtc,
        DateTimeOffset? endsAtUtc,
        string presentationTimeZone,
        Guid publishedByUserId,
        DateTimeOffset publishedAtUtc)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || deviceId == Guid.Empty ||
            playlistVersionId == Guid.Empty || publishedByUserId == Guid.Empty)
        {
            throw new ArgumentException("Assignment identifiers cannot be empty.");
        }

        EnsureUtc(startsAtUtc, nameof(startsAtUtc));
        EnsureUtc(endsAtUtc, nameof(endsAtUtc));
        EnsureUtc(publishedAtUtc, nameof(publishedAtUtc));
        if (startsAtUtc.HasValue && endsAtUtc.HasValue && endsAtUtc <= startsAtUtc)
        {
            throw new ArgumentException("Assignment end must be after its start.", nameof(endsAtUtc));
        }

        if (priority is < -1000 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(priority));
        }

        AssignmentSchedule.EnsureSupportedTimeZone(presentationTimeZone);

        Id = id;
        TenantId = tenantId;
        DeviceId = deviceId;
        PlaylistVersionId = playlistVersionId;
        IsEnabled = true;
        Priority = priority;
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
        PresentationTimeZone = presentationTimeZone.Trim();
        PublishedByUserId = publishedByUserId;
        PublishedAtUtc = publishedAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    public bool IsActiveAt(DateTimeOffset nowUtc) =>
        IsEnabled && AssignmentSchedule.IsActiveAt(StartsAtUtc, EndsAtUtc, nowUtc);

    public Guid DeviceId { get; private set; }

    public Guid PlaylistVersionId { get; private set; }

    public bool IsEnabled { get; private set; }

    public int Priority { get; private set; }

    public DateTimeOffset? StartsAtUtc { get; private set; }

    public DateTimeOffset? EndsAtUtc { get; private set; }

    public string PresentationTimeZone { get; private set; } = string.Empty;

    public Guid PublishedByUserId { get; private set; }

    public DateTimeOffset PublishedAtUtc { get; private set; }

    public Guid ConcurrencyToken { get; private set; }

    private static void EnsureUtc(DateTimeOffset? value, string parameterName)
    {
        if (value is { Offset: var offset } && offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must have a UTC offset.", parameterName);
        }
    }
}
