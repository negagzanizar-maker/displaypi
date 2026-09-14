using DisplayControl.Domain.Tenancy;

namespace DisplayControl.Domain.Playlists;

public sealed class PlaylistVersion : TenantOwnedEntity
{
    private PlaylistVersion()
    {
    }

    public PlaylistVersion(
        Guid id,
        Guid tenantId,
        Guid playlistId,
        int versionNumber,
        string nameSnapshot,
        string? descriptionSnapshot,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || playlistId == Guid.Empty || createdByUserId == Guid.Empty)
        {
            throw new ArgumentException("Playlist version identifiers cannot be empty.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(versionNumber);

        EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        Id = id;
        TenantId = tenantId;
        PlaylistId = playlistId;
        VersionNumber = versionNumber;
        NameSnapshot = Normalize(nameSnapshot, 200, nameof(nameSnapshot));
        DescriptionSnapshot = NormalizeOptional(descriptionSnapshot, 2000, nameof(descriptionSnapshot));
        PublicationState = "draft";
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid PlaylistId { get; private set; }

    public int VersionNumber { get; private set; }

    public string NameSnapshot { get; private set; } = string.Empty;

    public string? DescriptionSnapshot { get; private set; }

    public string PublicationState { get; private set; } = string.Empty;

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid? PublishedByUserId { get; private set; }

    public DateTimeOffset? PublishedAtUtc { get; private set; }

    public void Publish(Guid publishedByUserId, DateTimeOffset publishedAtUtc)
    {
        if (publishedByUserId == Guid.Empty)
        {
            throw new ArgumentException("Publisher identifier cannot be empty.", nameof(publishedByUserId));
        }

        EnsureUtc(publishedAtUtc, nameof(publishedAtUtc));
        if (!string.Equals(PublicationState, "draft", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only a draft playlist version can be published.");
        }

        PublicationState = "published";
        PublishedByUserId = publishedByUserId;
        PublishedAtUtc = publishedAtUtc;
    }

    private static string Normalize(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"Value cannot exceed {maximumLength} characters.", parameterName);
    }

    private static string? NormalizeOptional(string? value, int maximumLength, string parameterName) =>
        string.IsNullOrWhiteSpace(value) ? null : Normalize(value, maximumLength, parameterName);

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must have a UTC offset.", parameterName);
        }
    }
}
