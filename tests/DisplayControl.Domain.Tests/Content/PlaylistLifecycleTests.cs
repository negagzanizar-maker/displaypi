using DisplayControl.Domain.Playlists;

namespace DisplayControl.Domain.Tests.Content;

public sealed class PlaylistLifecycleTests
{
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 9, 15, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RevisionAndArchiveAdvanceConcurrencyAndBlockLaterRevision()
    {
        var playlist = new Playlist(
            Guid.NewGuid(), Guid.NewGuid(), "Lobby", null, Guid.NewGuid(), CreatedAtUtc);
        var originalToken = playlist.ConcurrencyToken;

        playlist.RecordRevision(CreatedAtUtc.AddMinutes(1));

        Assert.NotEqual(originalToken, playlist.ConcurrencyToken);
        Assert.Equal(CreatedAtUtc.AddMinutes(1), playlist.UpdatedAtUtc);
        var revisedToken = playlist.ConcurrencyToken;

        playlist.Archive(CreatedAtUtc.AddMinutes(2));

        Assert.NotEqual(revisedToken, playlist.ConcurrencyToken);
        Assert.Equal(CreatedAtUtc.AddMinutes(2), playlist.ArchivedAtUtc);
        Assert.Throws<InvalidOperationException>(() => playlist.RecordRevision(CreatedAtUtc.AddMinutes(3)));
    }
}
