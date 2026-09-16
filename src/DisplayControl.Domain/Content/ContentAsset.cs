using DisplayControl.Domain.Tenancy;

namespace DisplayControl.Domain.Content;

public sealed class ContentAsset : TenantOwnedEntity
{
    private ContentAsset()
    {
    }

    public ContentAsset(
        Guid id,
        Guid tenantId,
        string title,
        MediaKind mediaKind,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || createdByUserId == Guid.Empty)
        {
            throw new ArgumentException("Content, tenant, and creator identifiers cannot be empty.");
        }

        var normalizedTitle = title.Trim();
        if (normalizedTitle.Length is 0 or > 200)
        {
            throw new ArgumentException("Content title must contain 1–200 characters.", nameof(title));
        }

        EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        Id = id;
        TenantId = tenantId;
        Title = normalizedTitle;
        MediaKind = mediaKind;
        LifecycleState = ContentLifecycleState.Draft;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public MediaKind MediaKind { get; private set; }

    public ContentLifecycleState LifecycleState { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public DateTimeOffset? ArchivedAtUtc { get; private set; }

    public Guid ConcurrencyToken { get; private set; }

    public void RecordScanOutcome(ContentScanOutcome outcome, DateTimeOffset occurredAtUtc)
    {
        EnsureUtc(occurredAtUtc, nameof(occurredAtUtc));
        if (LifecycleState is ContentLifecycleState.Approved or ContentLifecycleState.Archived)
        {
            throw new InvalidOperationException("Approved or archived content cannot return to scanning.");
        }

        LifecycleState = outcome switch
        {
            ContentScanOutcome.Clean => ContentLifecycleState.Approved,
            ContentScanOutcome.Infected => ContentLifecycleState.Rejected,
            ContentScanOutcome.Unavailable => ContentLifecycleState.Quarantined,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome))
        };
        UpdatedAtUtc = occurredAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    public void Approve(DateTimeOffset approvedAtUtc)
    {
        EnsureUtc(approvedAtUtc, nameof(approvedAtUtc));
        if (LifecycleState != ContentLifecycleState.Draft)
        {
            throw new InvalidOperationException("Only clean draft content can be approved.");
        }

        LifecycleState = ContentLifecycleState.Approved;
        UpdatedAtUtc = approvedAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    public void Archive(DateTimeOffset archivedAtUtc)
    {
        EnsureUtc(archivedAtUtc, nameof(archivedAtUtc));
        if (LifecycleState == ContentLifecycleState.Archived)
        {
            return;
        }

        LifecycleState = ContentLifecycleState.Archived;
        ArchivedAtUtc = archivedAtUtc;
        UpdatedAtUtc = archivedAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must have a UTC offset.", parameterName);
        }
    }
}
