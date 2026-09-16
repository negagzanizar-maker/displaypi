using DisplayControl.Domain.Content;

namespace DisplayControl.Domain.Tests.Content;

public sealed class ContentLifecycleTests
{
    private static readonly DateTimeOffset NowUtc = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CleanContentBecomesAutomaticallyUsable()
    {
        var creatorId = Guid.NewGuid();
        var asset = new ContentAsset(Guid.NewGuid(), Guid.NewGuid(), "Lobby", MediaKind.Png, creatorId, NowUtc);

        asset.RecordScanOutcome(ContentScanOutcome.Clean, NowUtc.AddSeconds(1));

        Assert.Equal(ContentLifecycleState.Approved, asset.LifecycleState);
    }

    [Theory]
    [InlineData(ContentScanOutcome.Infected, ContentLifecycleState.Rejected)]
    [InlineData(ContentScanOutcome.Unavailable, ContentLifecycleState.Quarantined)]
    public void UnsafeOrUnscannedContentCannotBeApproved(
        ContentScanOutcome outcome,
        ContentLifecycleState expectedState)
    {
        var asset = new ContentAsset(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Lobby",
            MediaKind.Png,
            Guid.NewGuid(),
            NowUtc);

        asset.RecordScanOutcome(outcome, NowUtc.AddSeconds(1));

        Assert.Equal(expectedState, asset.LifecycleState);
        Assert.Throws<InvalidOperationException>(() => asset.Approve(NowUtc.AddSeconds(2)));
    }

    [Fact]
    public void ContentVersionCopiesDigestAndApprovesOnlyCleanScan()
    {
        var digest = Enumerable.Repeat((byte)0x2A, 32).ToArray();
        var version = new ContentVersion(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            "tenants/a/objects/b",
            42,
            digest,
            "image/png",
            "lobby.png",
            "{}",
            Guid.NewGuid(),
            NowUtc);
        digest[0] = 0;

        Assert.Equal(0x2A, version.Sha256[0]);
        Assert.Throws<InvalidOperationException>(() => version.Approve(Guid.NewGuid(), NowUtc));
        version.RecordScanOutcome(ContentScanOutcome.Clean, "clamd", null);
        version.Approve(Guid.NewGuid(), NowUtc.AddSeconds(1));
        Assert.NotNull(version.ApprovedAtUtc);
    }
}
