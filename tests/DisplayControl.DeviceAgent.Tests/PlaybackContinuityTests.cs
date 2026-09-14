namespace DisplayControl.DeviceAgent.Tests;

public sealed class PlaybackContinuityTests
{
    [Fact]
    public void ReplacementDoesNotBlankOrExtendOldAuthorization()
    {
        var now = DateTimeOffset.UtcNow;
        var clock = new ManualTimeProvider(now);
        var store = new PlayerStateStore(clock);
        var device = Guid.NewGuid();
        var manifest = new ActivePlayerManifest(Guid.NewGuid(), 1, []);
        store.SetReady(device, manifest, new Dictionary<Guid, PlayerAssetFile>(), now.AddSeconds(5), now, new string('a', 64));
        store.SetSynchronizing(device, 2, now.AddHours(1), now);
        Assert.Equal(manifest.DesiredStateId, store.ManifestSnapshot()!.DesiredStateId);
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Null(store.ManifestSnapshot());
        Assert.Equal("notLicensed", store.Snapshot().Status);
    }

    [Fact]
    public void RenewalRequiresFullManifestIdentityAndPreservesPresentation()
    {
        var now = DateTimeOffset.UtcNow;
        var clock = new ManualTimeProvider(now);
        var store = new PlayerStateStore(clock);
        var device = Guid.NewGuid();
        var manifest = new ActivePlayerManifest(Guid.NewGuid(), 1, []);
        var hash = new string('a', 64);
        store.SetReady(device, manifest, new Dictionary<Guid, PlayerAssetFile>(), now.AddSeconds(5), now, hash);
        Assert.False(store.RenewReady(device, Guid.NewGuid(), 1, hash, now.AddHours(1), now));
        Assert.False(store.RenewReady(device, manifest.DesiredStateId, 1, new string('b', 64), now.AddHours(1), now));
        Assert.True(store.RenewReady(device, manifest.DesiredStateId, 1, hash, now.AddMinutes(1), now));
        clock.Advance(TimeSpan.FromSeconds(6));
        Assert.Equal("ready", store.Snapshot().Status);
        Assert.Equal(manifest.DesiredStateId, store.ManifestSnapshot()!.DesiredStateId);
    }

    [Fact]
    public void PlaybackReportsMustMatchAuthorizedManifestAndKnownAsset()
    {
        var now = DateTimeOffset.UtcNow;
        var clock = new ManualTimeProvider(now);
        var store = new PlayerStateStore(clock);
        var content = Guid.NewGuid();
        var manifest = new ActivePlayerManifest(Guid.NewGuid(), 1, [new ActivePlayerAsset(content, 0, "image", 1000, false)]);
        store.SetReady(Guid.NewGuid(), manifest,
            new Dictionary<Guid, PlayerAssetFile> { [content] = new("image.png", "image/png", 1, new string('a', 64)) },
            now.AddSeconds(5), now);
        var report = new PlaybackReport(manifest.DesiredStateId, 1, content, "error", "media_error");
        Assert.False(store.ReportPlayback(report with { Version = 2 }));
        Assert.False(store.ReportPlayback(report with { ContentVersionId = Guid.NewGuid() }));
        Assert.False(store.ReportPlayback(report with { ErrorCode = "arbitrary" }));
        Assert.True(store.ReportPlayback(report));
        Assert.Equal("media_error", store.Snapshot().SafeReasonCode);
        Assert.Equal(content, store.Snapshot().CurrentContentVersionId);
        Assert.True(store.ReportPlayback(report with { Status = "playing", ErrorCode = null }));
        Assert.Null(store.Snapshot().SafeReasonCode);
        Assert.Equal(content, store.Snapshot().CurrentContentVersionId);
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.False(store.ReportPlayback(report));
        Assert.Null(store.Snapshot().CurrentContentVersionId);
    }

    [Fact]
    public void HealthExpiresWhenWorkerStopsMakingProgress()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var health = new AgentHealthStore(clock);
        Assert.False(health.IsFresh(30));
        health.RecordCycle(true);
        Assert.True(health.IsFresh(30));
        clock.Advance(TimeSpan.FromMinutes(3));
        Assert.False(health.IsFresh(30));
    }
}
