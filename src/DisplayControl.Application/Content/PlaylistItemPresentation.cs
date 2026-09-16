using System.Text.Json;

namespace DisplayControl.Application.Content;

public static class PlaylistItemPresentation
{
    public const int MaximumCaptionTextLength = 1000;

    public static string Serialize(Guid? captionContentVersionId, string? captionText = null)
    {
        var normalizedText = string.IsNullOrWhiteSpace(captionText) ? null : captionText.Trim();
        if (captionContentVersionId.HasValue && normalizedText is not null)
        {
            throw new ArgumentException("Choose either inline caption text or caption content, not both.");
        }
        if (normalizedText?.Length > MaximumCaptionTextLength)
        {
            throw new ArgumentException($"Caption text cannot exceed {MaximumCaptionTextLength} characters.", nameof(captionText));
        }

        if (captionContentVersionId.HasValue)
        {
            return JsonSerializer.Serialize(new { captionContentVersionId });
        }

        return normalizedText is null ? "{}" : JsonSerializer.Serialize(new { captionText = normalizedText });
    }

    public static bool TryReadCaptionContentVersionId(string json, out Guid? captionContentVersionId)
    {
        return TryRead(json, out captionContentVersionId, out _);
    }

    public static bool TryRead(string json, out Guid? captionContentVersionId, out string? captionText)
    {
        captionContentVersionId = null;
        captionText = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (document.RootElement.TryGetProperty("captionContentVersionId", out var property))
            {
                if (property.ValueKind != JsonValueKind.String || !property.TryGetGuid(out var value) || value == Guid.Empty)
                {
                    return false;
                }
                captionContentVersionId = value;
            }

            if (document.RootElement.TryGetProperty("captionText", out var textProperty))
            {
                if (textProperty.ValueKind != JsonValueKind.String)
                {
                    return false;
                }
                var value = textProperty.GetString()?.Trim();
                if (string.IsNullOrEmpty(value) || value.Length > MaximumCaptionTextLength)
                {
                    return false;
                }
                captionText = value;
            }

            return !(captionContentVersionId.HasValue && captionText is not null);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string CaptionAssetJson() => "{\"role\":\"caption\"}";
}
