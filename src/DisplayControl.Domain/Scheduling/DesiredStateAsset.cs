using DisplayControl.Domain.Content;
using DisplayControl.Domain.Tenancy;

namespace DisplayControl.Domain.Scheduling;

public sealed class DesiredStateAsset : TenantOwnedEntity
{
    private DesiredStateAsset()
    {
    }

    public DesiredStateAsset(
        Guid id,
        Guid tenantId,
        Guid desiredStateId,
        Guid contentVersionId,
        int position,
        MediaKind mediaKind,
        long byteLength,
        byte[] sha256,
        int? durationMilliseconds,
        bool loopVideo,
        string playbackJson)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || desiredStateId == Guid.Empty || contentVersionId == Guid.Empty)
        {
            throw new ArgumentException("Desired-state asset identifiers cannot be empty.");
        }

        if (position < 0 || byteLength <= 0 || durationMilliseconds is <= 0 || sha256 is not { Length: 32 })
        {
            throw new ArgumentException("Desired-state asset values are invalid.");
        }

        if (string.IsNullOrWhiteSpace(playbackJson) || playbackJson.Length > 4096)
        {
            throw new ArgumentException("Playback JSON is required and limited to 4096 characters.", nameof(playbackJson));
        }

        Id = id;
        TenantId = tenantId;
        DesiredStateId = desiredStateId;
        ContentVersionId = contentVersionId;
        Position = position;
        MediaKind = mediaKind;
        ByteLength = byteLength;
        Sha256 = [.. sha256];
        DurationMilliseconds = durationMilliseconds;
        LoopVideo = loopVideo;
        PlaybackJson = playbackJson;
    }

    public Guid DesiredStateId { get; private set; }

    public Guid ContentVersionId { get; private set; }

    public int Position { get; private set; }

    public MediaKind MediaKind { get; private set; }

    public long ByteLength { get; private set; }

    public byte[] Sha256 { get; private set; } = [];

    public int? DurationMilliseconds { get; private set; }

    public bool LoopVideo { get; private set; }

    public string PlaybackJson { get; private set; } = "{}";
}
