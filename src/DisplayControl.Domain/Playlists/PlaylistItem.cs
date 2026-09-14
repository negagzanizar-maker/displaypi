using DisplayControl.Domain.Tenancy;

namespace DisplayControl.Domain.Playlists;

public sealed class PlaylistItem : TenantOwnedEntity
{
    private PlaylistItem()
    {
    }

    public PlaylistItem(
        Guid id,
        Guid tenantId,
        Guid playlistVersionId,
        Guid contentVersionId,
        int position,
        int? durationMilliseconds,
        bool loopVideo,
        string presentationJson)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || playlistVersionId == Guid.Empty || contentVersionId == Guid.Empty)
        {
            throw new ArgumentException("Playlist item identifiers cannot be empty.");
        }

        if (position < 0 || durationMilliseconds is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        if (string.IsNullOrWhiteSpace(presentationJson) || presentationJson.Length > 4096)
        {
            throw new ArgumentException("Presentation JSON is required and limited to 4096 characters.", nameof(presentationJson));
        }

        Id = id;
        TenantId = tenantId;
        PlaylistVersionId = playlistVersionId;
        ContentVersionId = contentVersionId;
        Position = position;
        DurationMilliseconds = durationMilliseconds;
        LoopVideo = loopVideo;
        PresentationJson = presentationJson;
    }

    public Guid PlaylistVersionId { get; private set; }

    public Guid ContentVersionId { get; private set; }

    public int Position { get; private set; }

    public int? DurationMilliseconds { get; private set; }

    public bool LoopVideo { get; private set; }

    public string PresentationJson { get; private set; } = "{}";
}
