using System.Text.Json;

namespace DisplayControl.Application.Content;

public static class PlaylistItemPresentation
{
    public static string Serialize(Guid? captionContentVersionId) => captionContentVersionId is null
        ? "{}"
        : JsonSerializer.Serialize(new { captionContentVersionId });

    public static bool TryReadCaptionContentVersionId(string json, out Guid? captionContentVersionId)
    {
        captionContentVersionId = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!document.RootElement.TryGetProperty("captionContentVersionId", out var property))
            {
                return true;
            }

            if (property.ValueKind != JsonValueKind.String || !property.TryGetGuid(out var value) || value == Guid.Empty)
            {
                return false;
            }

            captionContentVersionId = value;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string CaptionAssetJson() => "{\"role\":\"caption\"}";
}
