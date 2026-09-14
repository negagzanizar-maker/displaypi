using System.Security.Cryptography;
using System.Text.Json;

using DisplayControl.Domain.Content;

namespace DisplayControl.Application.Content;

public static class DesiredStateManifestCodec
{
    public static byte[] SerializeCanonical(
        Guid desiredStateId,
        long version,
        DateTimeOffset publishedAtUtc,
        IReadOnlyList<DesiredStateManifestAsset> assets) =>
        SerializeCanonical(desiredStateId, version, null, null, publishedAtUtc, assets);

    public static byte[] SerializeCanonical(
        Guid desiredStateId,
        long version,
        DateTimeOffset? startsAtUtc,
        DateTimeOffset? endsAtUtc,
        DateTimeOffset publishedAtUtc,
        IReadOnlyList<DesiredStateManifestAsset> assets)
    {
        if (desiredStateId == Guid.Empty || version <= 0 || publishedAtUtc.Offset != TimeSpan.Zero ||
            startsAtUtc is { Offset: var startOffset } && startOffset != TimeSpan.Zero ||
            endsAtUtc is { Offset: var endOffset } && endOffset != TimeSpan.Zero ||
            startsAtUtc.HasValue && endsAtUtc.HasValue && endsAtUtc <= startsAtUtc || assets.Count == 0)
        {
            throw new ArgumentException("Desired-state manifest bindings are invalid.");
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("desiredStateId", desiredStateId);
            writer.WriteNumber("version", version);
            writer.WriteString("publishedAtUtc", publishedAtUtc);
            WriteOptionalTimestamp(writer, "startsAtUtc", startsAtUtc);
            WriteOptionalTimestamp(writer, "endsAtUtc", endsAtUtc);
            writer.WriteStartArray("assets");
            foreach (var asset in assets.OrderBy(value => value.Position))
            {
                writer.WriteStartObject();
                writer.WriteString("contentVersionId", asset.ContentVersionId);
                writer.WriteNumber("position", asset.Position);
                writer.WriteString("mediaKind", ToWireName(asset.MediaKind));
                writer.WriteNumber("byteLength", asset.ByteLength);
                writer.WriteString("sha256", Convert.ToHexString(asset.Sha256).ToLowerInvariant());
                if (asset.DurationMilliseconds.HasValue)
                {
                    writer.WriteNumber("durationMilliseconds", asset.DurationMilliseconds.Value);
                }
                else
                {
                    writer.WriteNull("durationMilliseconds");
                }

                writer.WriteBoolean("loopVideo", asset.LoopVideo);
                writer.WritePropertyName("playback");
                using (var playback = JsonDocument.Parse(asset.PlaybackJson))
                {
                    playback.RootElement.WriteTo(writer);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    public static byte[] CalculateSha256(
        Guid desiredStateId,
        long version,
        DateTimeOffset publishedAtUtc,
        IReadOnlyList<DesiredStateManifestAsset> assets) =>
        SHA256.HashData(SerializeCanonical(desiredStateId, version, publishedAtUtc, assets));

    public static byte[] CalculateSha256(
        Guid desiredStateId,
        long version,
        DateTimeOffset? startsAtUtc,
        DateTimeOffset? endsAtUtc,
        DateTimeOffset publishedAtUtc,
        IReadOnlyList<DesiredStateManifestAsset> assets) =>
        SHA256.HashData(SerializeCanonical(
            desiredStateId,
            version,
            startsAtUtc,
            endsAtUtc,
            publishedAtUtc,
            assets));

    private static void WriteOptionalTimestamp(
        Utf8JsonWriter writer,
        string propertyName,
        DateTimeOffset? value)
    {
        if (value.HasValue)
        {
            writer.WriteString(propertyName, value.Value);
        }
        else
        {
            writer.WriteNull(propertyName);
        }
    }

    private static string ToWireName(MediaKind mediaKind) => mediaKind switch
    {
        MediaKind.PlainText => "plainText",
        MediaKind.Jpeg => "jpeg",
        MediaKind.Png => "png",
        MediaKind.WebP => "webP",
        MediaKind.Mp4 => "mp4",
        _ => throw new ArgumentOutOfRangeException(nameof(mediaKind))
    };
}

public sealed record DesiredStateManifestAsset(
    Guid ContentVersionId,
    int Position,
    MediaKind MediaKind,
    long ByteLength,
    byte[] Sha256,
    int? DurationMilliseconds,
    bool LoopVideo,
    string PlaybackJson);
