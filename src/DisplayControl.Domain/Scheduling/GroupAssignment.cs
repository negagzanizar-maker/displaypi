using DisplayControl.Domain.Tenancy;

namespace DisplayControl.Domain.Scheduling;

public sealed class GroupAssignment : TenantOwnedEntity
{
    private GroupAssignment()
    {
    }

    public GroupAssignment(
        Guid id,
        Guid tenantId,
        Guid deviceGroupId,
        Guid playlistVersionId,
        int priority,
        DateTimeOffset? startsAtUtc,
        DateTimeOffset? endsAtUtc,
        string presentationTimeZone,
        Guid publishedByUserId,
        DateTimeOffset publishedAtUtc)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || deviceGroupId == Guid.Empty ||
            playlistVersionId == Guid.Empty || publishedByUserId == Guid.Empty)
        {
            throw new ArgumentException("Assignment identifiers cannot be empty.");
        }

        AssignmentSchedule.EnsureWindow(startsAtUtc, endsAtUtc);
        AssignmentSchedule.EnsureUtc(publishedAtUtc, nameof(publishedAtUtc));
        if (priority is < -1000 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(priority));
        }

        AssignmentSchedule.EnsureSupportedTimeZone(presentationTimeZone);
        Id = id;
        TenantId = tenantId;
        DeviceGroupId = deviceGroupId;
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

    public Guid DeviceGroupId { get; private set; }

    public Guid PlaylistVersionId { get; private set; }

    public bool IsEnabled { get; private set; }

    public int Priority { get; private set; }

    public DateTimeOffset? StartsAtUtc { get; private set; }

    public DateTimeOffset? EndsAtUtc { get; private set; }

    public string PresentationTimeZone { get; private set; } = string.Empty;

    public Guid PublishedByUserId { get; private set; }

    public DateTimeOffset PublishedAtUtc { get; private set; }

    public Guid ConcurrencyToken { get; private set; }

    public bool IsActiveAt(DateTimeOffset nowUtc) =>
        IsEnabled && AssignmentSchedule.IsActiveAt(StartsAtUtc, EndsAtUtc, nowUtc);
}
