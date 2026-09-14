using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DisplayControl.Application.Security;
using Microsoft.Extensions.Options;

namespace DisplayControl.DeviceAgent.Tests;

public sealed class AgentStateStoreTests
{
    [Fact]
    public async Task StateIsAtomicAndPrivateKeyRemainsInDedicatedFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "display-control-agent-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var store = new AgentStateStore(Options.Create(new AgentRuntimeOptions
            {
                ServerBaseAddress = new Uri("https://device.example.test"),
                StateDirectory = root
            }));
            Assert.Null(await store.LoadAsync(CancellationToken.None));

            byte[] firstPublicKey;
            using (var key = await store.LoadOrCreatePrivateKeyAsync(CancellationToken.None))
            {
                firstPublicKey = key.ExportSubjectPublicKeyInfo();
            }

            using (var reloadedKey = await store.LoadOrCreatePrivateKeyAsync(CancellationToken.None))
            {
                Assert.Equal(firstPublicKey, reloadedKey.ExportSubjectPublicKeyInfo());
            }

            var nowUtc = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
            var state = new AgentPersistentState(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "certificate",
                "authority",
                nowUtc.AddDays(30),
                new LicenseLeaseVerificationKey("kid", "ES256", "public-key"),
                7,
                nowUtc,
                null,
                null,
                "notLicensed",
                null,
                null,
                null,
                null);
            await store.SaveAsync(state, CancellationToken.None);

            Assert.Equal(state, await store.LoadAsync(CancellationToken.None));
            var serializedState = await File.ReadAllTextAsync(
                Path.Combine(root, "agent-state.json"),
                CancellationToken.None);
            Assert.DoesNotContain("PRIVATE KEY", serializedState, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(root, "device-private-key.pem")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void PlayerStateStartsFailClosedAndDistinguishesNoContent()
    {
        var store = new PlayerStateStore(TimeProvider.System);

        Assert.Equal("notLicensed", store.Snapshot().Status);

        var nowUtc = DateTimeOffset.UtcNow;
        store.SetLicensedNoContent(Guid.NewGuid(), nowUtc.AddMinutes(1), nowUtc);

        Assert.Equal("noContent", store.Snapshot().Status);
        Assert.Equal("No content assigned", store.Snapshot().Message);
    }

    [Fact]
    public void PlayerStateRevokesManifestAndAssetsAtLeaseExpiry()
    {
        var nowUtc = new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new ManualTimeProvider(nowUtc);
        var store = new PlayerStateStore(timeProvider);
        var contentVersionId = Guid.NewGuid();
        store.SetReady(
            Guid.NewGuid(),
            new ActivePlayerManifest(
                Guid.NewGuid(),
                1,
                [new ActivePlayerAsset(contentVersionId, 0, "png", 5_000, false)]),
            new Dictionary<Guid, PlayerAssetFile>
            {
                [contentVersionId] = new("asset.png", "image/png", 4, new string('a', 64))
            },
            nowUtc.AddSeconds(5),
            nowUtc);

        Assert.Equal("ready", store.Snapshot().Status);
        Assert.NotNull(store.ManifestSnapshot());
        Assert.True(store.TryResolveAsset(contentVersionId, out _));

        timeProvider.RollbackUtc(TimeSpan.FromDays(1));
        timeProvider.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal("notLicensed", store.Snapshot().Status);
        Assert.Equal("lease_expired", store.Snapshot().SafeReasonCode);
        Assert.Null(store.ManifestSnapshot());
        Assert.False(store.TryResolveAsset(contentVersionId, out _));
    }

    [Fact]
    public async Task RecoversRotationWhenStateWasSavedBeforePendingKeyPromotion()
    {
        var root = Path.Combine(Path.GetTempPath(), "display-control-agent-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var store = new AgentStateStore(Options.Create(new AgentRuntimeOptions
            {
                ServerBaseAddress = new Uri("https://device.example.test"),
                StateDirectory = root
            }));
            var nowUtc = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
            using var currentKey = await store.LoadOrCreatePrivateKeyAsync(CancellationToken.None);
            using var currentCertificate = CreateCertificate(currentKey, nowUtc);
            var currentState = CreateState(currentCertificate.ExportCertificatePem(), nowUtc);
            await store.SaveAsync(currentState, CancellationToken.None);

            using var rotationKey = await store.LoadOrCreateRotationPrivateKeyAsync(CancellationToken.None);
            using var replacementCertificate = CreateCertificate(rotationKey, nowUtc.AddMinutes(1));
            var replacementState = currentState with
            {
                CertificateId = Guid.NewGuid(),
                CertificatePem = replacementCertificate.ExportCertificatePem()
            };
            await store.SaveAsync(replacementState, CancellationToken.None);

            using var recoveredKey = await store.LoadOrCreatePrivateKeyAsync(CancellationToken.None);

            Assert.Equal(rotationKey.ExportSubjectPublicKeyInfo(), recoveredKey.ExportSubjectPublicKeyInfo());
            Assert.False(File.Exists(Path.Combine(root, "device-private-key.next.pem")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static X509Certificate2 CreateCertificate(ECDsa key, DateTimeOffset nowUtc)
    {
        var request = new CertificateRequest("CN=test-device", key, HashAlgorithmName.SHA256);
        return request.CreateSelfSigned(nowUtc.AddMinutes(-1), nowUtc.AddDays(90));
    }

    private static AgentPersistentState CreateState(string certificatePem, DateTimeOffset nowUtc) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        certificatePem,
        certificatePem,
        nowUtc.AddDays(90),
        new LicenseLeaseVerificationKey("kid", "ES256", "public-key"),
        0,
        nowUtc,
        null,
        null,
        "notLicensed",
        null,
        null,
        null,
        null);
}

internal sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset _utcNow = utcNow;
    private long _timestamp = utcNow.UtcTicks;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public override long GetTimestamp() => _timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public void Advance(TimeSpan duration)
    {
        _utcNow = _utcNow.Add(duration);
        _timestamp += duration.Ticks;
    }

    public void RollbackUtc(TimeSpan duration) => _utcNow = _utcNow.Subtract(duration);
}
