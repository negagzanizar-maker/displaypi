using DisplayControl.Application.Content;

namespace DisplayControl.IntegrationTests.Content;

public sealed class PlaylistItemPresentationTests
{
    [Fact]
    public void InlineCaptionRoundTripsWithoutASeparateContentAsset()
    {
        var json = PlaylistItemPresentation.Serialize(null, "  Welcome under the image  ");

        Assert.True(PlaylistItemPresentation.TryRead(json, out var captionId, out var captionText));
        Assert.Null(captionId);
        Assert.Equal("Welcome under the image", captionText);
    }

    [Fact]
    public void CaptionContentAndInlineTextCannotBeCombined()
    {
        Assert.Throws<ArgumentException>(() =>
            PlaylistItemPresentation.Serialize(Guid.NewGuid(), "Duplicate caption"));
    }
}
