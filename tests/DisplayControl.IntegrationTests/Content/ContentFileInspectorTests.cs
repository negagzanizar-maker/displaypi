using System.Buffers.Binary;
using System.Text;

using DisplayControl.Application.Content;
using DisplayControl.Domain.Content;

namespace DisplayControl.IntegrationTests.Content;

public sealed class ContentFileInspectorTests
{
    [Fact]
    public async Task ValidatesPngSignatureAndDimensionsAndHashesBytes()
    {
        var png = new byte[32];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(png, 0);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16, 4), 1920);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20, 4), 1080);
        await using var stream = new MemoryStream(png);

        var result = await ContentFileInspector.InspectAsync(stream, png.Length);

        Assert.Equal(MediaKind.Png, result.MediaKind);
        Assert.Equal("image/png", result.MimeType);
        Assert.Contains("1920", result.MetadataJson, StringComparison.Ordinal);
        Assert.Equal(32, result.Sha256.Length);
    }

    [Fact]
    public async Task DetectsValidUtf8TextWithoutADeclaredKind()
    {
        var bytes = Encoding.UTF8.GetBytes("ordinary UTF-8 text");
        await using var stream = new MemoryStream(bytes);

        var result = await ContentFileInspector.InspectAsync(stream, bytes.Length);

        Assert.Equal(MediaKind.PlainText, result.MediaKind);
        Assert.Equal("text/plain; charset=utf-8", result.MimeType);
    }

    [Fact]
    public async Task RejectsInvalidUtf8PlainText()
    {
        byte[] bytes = [0xC3, 0x28];
        await using var stream = new MemoryStream(bytes);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            ContentFileInspector.InspectAsync(stream, bytes.Length));
    }
}
