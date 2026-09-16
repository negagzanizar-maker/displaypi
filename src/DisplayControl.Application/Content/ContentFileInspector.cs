using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

using DisplayControl.Domain.Content;

namespace DisplayControl.Application.Content;

public static class ContentFileInspector
{
    private const int InspectionPrefixLimit = 64 * 1024;
    private const uint MaximumImageDimension = 16_384;
    private const ulong MaximumImagePixels = 67_108_864;

    public static async Task<ContentFileInspection> InspectAsync(
        Stream content,
        long expectedLength,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanRead || expectedLength <= 0)
        {
            throw new InvalidDataException("Content must be a non-empty readable stream.");
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var prefix = new byte[(int)Math.Min(expectedLength, InspectionPrefixLimit)];
        var buffer = new byte[InspectionPrefixLimit];
        var prefixLength = 0;
        long totalLength = 0;
        Decoder? textDecoder = null;
        char[]? characters = null;
        var mediaSignatureChecked = false;

        while (true)
        {
            var read = await content.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            totalLength = checked(totalLength + read);
            if (totalLength > expectedLength)
            {
                throw new InvalidDataException("Content exceeds its declared length.");
            }

            hash.AppendData(buffer, 0, read);
            var prefixCopyLength = Math.Min(read, prefix.Length - prefixLength);
            if (prefixCopyLength > 0)
            {
                buffer.AsSpan(0, prefixCopyLength).CopyTo(prefix.AsSpan(prefixLength));
                prefixLength += prefixCopyLength;
            }

            var textValidationStartedThisRead = false;
            if (!mediaSignatureChecked && (prefixLength >= 12 || totalLength == expectedLength))
            {
                mediaSignatureChecked = true;
                if (!HasSupportedBinarySignature(prefix.AsSpan(0, prefixLength)))
                {
                    textDecoder = new UTF8Encoding(false, true).GetDecoder();
                    characters = new char[buffer.Length];
                    ValidateTextChunk(textDecoder, prefix.AsSpan(0, prefixLength), characters, flush: false);
                    textValidationStartedThisRead = true;
                }
            }

            if (textDecoder is not null && !textValidationStartedThisRead)
            {
                ValidateTextChunk(textDecoder, buffer.AsSpan(0, read), characters!, flush: false);
            }
        }

        if (totalLength != expectedLength)
        {
            throw new InvalidDataException("Content length does not match its declared length.");
        }

        if (textDecoder is not null)
        {
            ValidateTextChunk(textDecoder, ReadOnlySpan<byte>.Empty, characters!, flush: true);
        }

        var inspectedPrefix = prefix.AsSpan(0, prefixLength);
        var (detectedKind, mimeType, metadataJson) = Detect(inspectedPrefix, textDecoder is not null);

        return new ContentFileInspection(detectedKind, mimeType, hash.GetHashAndReset(), metadataJson);
    }

    private static (MediaKind Kind, string MimeType, string MetadataJson) Detect(
        ReadOnlySpan<byte> prefix,
        bool isValidText)
    {
        if (prefix.Length >= 24 && prefix[..8].SequenceEqual(
                new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            var width = BinaryPrimitives.ReadUInt32BigEndian(prefix.Slice(16, 4));
            var height = BinaryPrimitives.ReadUInt32BigEndian(prefix.Slice(20, 4));
            ValidateImageDimensions(width, height);
            return (MediaKind.Png, "image/png", $"{{\"width\":{width},\"height\":{height}}}");
        }

        if (prefix.Length >= 12 && prefix[..4].SequenceEqual("RIFF"u8) &&
            prefix.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return (MediaKind.WebP, "image/webp", "{}");
        }

        if (prefix.Length >= 12 && prefix.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            return (MediaKind.Mp4, "video/mp4", "{}");
        }

        if (prefix.Length >= 4 && prefix[0] == 0xFF && prefix[1] == 0xD8 && prefix[2] == 0xFF)
        {
            var dimensions = ReadJpegDimensions(prefix);
            ValidateImageDimensions(dimensions.Width, dimensions.Height);
            return (
                MediaKind.Jpeg,
                "image/jpeg",
                $"{{\"width\":{dimensions.Width},\"height\":{dimensions.Height}}}");
        }

        if (isValidText)
        {
            return (MediaKind.PlainText, "text/plain; charset=utf-8", "{}");
        }

        throw new InvalidDataException("The uploaded file is not an allowed media format.");
    }

    private static bool HasSupportedBinarySignature(ReadOnlySpan<byte> prefix) =>
        prefix.Length >= 8 && prefix[..8].SequenceEqual(
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }) ||
        prefix.Length >= 12 && prefix[..4].SequenceEqual("RIFF"u8) &&
            prefix.Slice(8, 4).SequenceEqual("WEBP"u8) ||
        prefix.Length >= 12 && prefix.Slice(4, 4).SequenceEqual("ftyp"u8) ||
        prefix.Length >= 3 && prefix[0] == 0xFF && prefix[1] == 0xD8 && prefix[2] == 0xFF;

    private static (uint Width, uint Height) ReadJpegDimensions(ReadOnlySpan<byte> data)
    {
        var offset = 2;
        while (offset + 9 < data.Length)
        {
            if (data[offset] != 0xFF)
            {
                offset++;
                continue;
            }

            while (offset < data.Length && data[offset] == 0xFF)
            {
                offset++;
            }

            if (offset >= data.Length)
            {
                break;
            }

            var marker = data[offset++];
            if (marker is 0xD8 or 0xD9 || marker is >= 0xD0 and <= 0xD7)
            {
                continue;
            }

            if (offset + 2 > data.Length)
            {
                break;
            }

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
            if (segmentLength < 2 || offset + segmentLength > data.Length)
            {
                break;
            }

            if (marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF)
            {
                if (segmentLength < 7)
                {
                    break;
                }

                var height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 3, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 5, 2));
                return (width, height);
            }

            offset += segmentLength;
        }

        throw new InvalidDataException("JPEG dimensions were not found in the bounded inspection prefix.");
    }

    private static void ValidateImageDimensions(uint width, uint height)
    {
        if (width == 0 || height == 0 || width > MaximumImageDimension || height > MaximumImageDimension ||
            (ulong)width * height > MaximumImagePixels)
        {
            throw new InvalidDataException("Image dimensions exceed the safe playback limits.");
        }
    }

    private static void ValidateTextChunk(
        Decoder decoder,
        ReadOnlySpan<byte> bytes,
        Span<char> characterBuffer,
        bool flush)
    {
        try
        {
            while (!bytes.IsEmpty || flush)
            {
                decoder.Convert(bytes, characterBuffer, flush, out var bytesUsed, out var charactersUsed, out var completed);
                foreach (var character in characterBuffer[..charactersUsed])
                {
                    if (character == '\0' || (char.IsControl(character) && character is not '\r' and not '\n' and not '\t'))
                    {
                        throw new InvalidDataException("Plain text contains unsupported control characters.");
                    }
                }

                bytes = bytes[bytesUsed..];
                if (completed)
                {
                    break;
                }
            }
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Plain text must be valid UTF-8.", exception);
        }
    }
}

public sealed record ContentFileInspection(
    MediaKind MediaKind,
    string MimeType,
    byte[] Sha256,
    string MetadataJson);
