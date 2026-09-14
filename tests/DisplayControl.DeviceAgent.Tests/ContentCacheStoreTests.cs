using System.Net;
using System.Security.Cryptography;

using DisplayControl.Application.Content;
using DisplayControl.Domain.Content;
using Microsoft.Extensions.Options;

namespace DisplayControl.DeviceAgent.Tests;

public sealed class ContentCacheStoreTests
{
    [Fact]
    public async Task DownloadsVerifiesAndAtomicallyActivatesManifestAssets()
    {
        var root = Path.Combine(Path.GetTempPath(), "display-control-cache-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var stateId = Guid.NewGuid();
            var contentVersionId = Guid.NewGuid();
            var publishedAtUtc = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
            byte[] assetBytes = [0x89, 0x50, 0x4E, 0x47];
            var assetHash = SHA256.HashData(assetBytes);
            var manifestBytes = DesiredStateManifestCodec.SerializeCanonical(
                stateId,
                7,
                publishedAtUtc,
                [new DesiredStateManifestAsset(
                    contentVersionId,
                    0,
                    MediaKind.Png,
                    assetBytes.Length,
                    assetHash,
                    5000,
                    false,
                    "{}")]);
            using var client = new HttpClient(new StaticContentHandler(
                stateId,
                contentVersionId,
                manifestBytes,
                assetBytes))
            {
                BaseAddress = new Uri("https://device.example.test")
            };
            using var cache = new ContentCacheStore(Options.Create(new AgentRuntimeOptions
            {
                ServerBaseAddress = client.BaseAddress,
                StateDirectory = root
            }));

            var activation = await cache.SynchronizeAsync(
                client,
                stateId,
                7,
                Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant(),
                CancellationToken.None);

            var cached = Assert.Single(activation.AssetFiles);
            Assert.Equal(contentVersionId, cached.Key);
            Assert.Equal(assetBytes, await File.ReadAllBytesAsync(cached.Value.Path));
            Assert.Equal(7, activation.Manifest.Version);
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
    public async Task RejectsAssetThatDoesNotMatchManifestDigest()
    {
        var root = Path.Combine(Path.GetTempPath(), "display-control-cache-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var stateId = Guid.NewGuid();
            var contentVersionId = Guid.NewGuid();
            byte[] expectedBytes = [1, 2, 3, 4];
            byte[] corruptBytes = [1, 2, 3, 5];
            var manifestBytes = DesiredStateManifestCodec.SerializeCanonical(
                stateId,
                1,
                new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero),
                [new DesiredStateManifestAsset(
                    contentVersionId,
                    0,
                    MediaKind.Png,
                    expectedBytes.Length,
                    SHA256.HashData(expectedBytes),
                    5000,
                    false,
                    "{}")]);
            using var client = new HttpClient(new StaticContentHandler(
                stateId,
                contentVersionId,
                manifestBytes,
                corruptBytes))
            {
                BaseAddress = new Uri("https://device.example.test")
            };
            using var cache = new ContentCacheStore(Options.Create(new AgentRuntimeOptions
            {
                ServerBaseAddress = client.BaseAddress,
                StateDirectory = root
            }));

            await Assert.ThrowsAsync<CryptographicException>(() => cache.SynchronizeAsync(
                client,
                stateId,
                1,
                Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant(),
                CancellationToken.None));
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
    public async Task ResumesAnInterruptedAssetAndStillVerifiesTheCompleteDigest()
    {
        var root = Path.Combine(Path.GetTempPath(), "display-control-cache-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var stateId = Guid.NewGuid();
            var contentVersionId = Guid.NewGuid();
            var assetBytes = Enumerable.Range(0, 300_000).Select(value => (byte)(value % 251)).ToArray();
            var manifestBytes = DesiredStateManifestCodec.SerializeCanonical(
                stateId,
                9,
                new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero),
                [new DesiredStateManifestAsset(
                    contentVersionId,
                    0,
                    MediaKind.Mp4,
                    assetBytes.Length,
                    SHA256.HashData(assetBytes),
                    null,
                    true,
                    "{}")]);
            const int interruptedAfter = 100_000;
            var handler = new InterruptThenResumeHandler(
                stateId,
                contentVersionId,
                manifestBytes,
                assetBytes,
                interruptedAfter);
            using var client = new HttpClient(handler) { BaseAddress = new Uri("https://device.example.test") };
            using var cache = new ContentCacheStore(Options.Create(new AgentRuntimeOptions
            {
                ServerBaseAddress = client.BaseAddress,
                StateDirectory = root
            }));
            var manifestDigest = Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant();

            await Assert.ThrowsAsync<IOException>(() => cache.SynchronizeAsync(
                client,
                stateId,
                9,
                manifestDigest,
                CancellationToken.None));
            var activation = await cache.SynchronizeAsync(
                client,
                stateId,
                9,
                manifestDigest,
                CancellationToken.None);

            Assert.Equal(interruptedAfter, handler.ObservedResumeOffset);
            var cached = Assert.Single(activation.AssetFiles).Value;
            Assert.Equal(assetBytes, await File.ReadAllBytesAsync(cached.Path));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class StaticContentHandler(
        Guid desiredStateId,
        Guid contentVersionId,
        byte[] manifest,
        byte[] asset) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expectedManifestPath = $"/device/v1/desired-states/{desiredStateId:D}/manifest";
            var expectedAssetPath =
                $"/device/v1/desired-states/{desiredStateId:D}/assets/{contentVersionId:D}";
            var bytes = request.RequestUri?.AbsolutePath switch
            {
                var path when path == expectedManifestPath => manifest,
                var path when path == expectedAssetPath => asset,
                _ => null
            };
            return Task.FromResult(bytes is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }

    private sealed class InterruptThenResumeHandler(
        Guid desiredStateId,
        Guid contentVersionId,
        byte[] manifest,
        byte[] asset,
        int interruptAfter) : HttpMessageHandler
    {
        private int _assetRequestCount;

        public long? ObservedResumeOffset { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expectedManifestPath = $"/device/v1/desired-states/{desiredStateId:D}/manifest";
            var expectedAssetPath = $"/device/v1/desired-states/{desiredStateId:D}/assets/{contentVersionId:D}";
            if (request.RequestUri?.AbsolutePath == expectedManifestPath)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(manifest)
                });
            }

            if (request.RequestUri?.AbsolutePath != expectedAssetPath)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            _assetRequestCount++;
            if (_assetRequestCount == 1)
            {
                var interruptedContent = new StreamContent(new InterruptingReadStream(asset, interruptAfter));
                interruptedContent.Headers.ContentLength = asset.Length;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = interruptedContent });
            }

            var range = Assert.Single(request.Headers.Range?.Ranges ?? []);
            ObservedResumeOffset = range.From;
            var offset = checked((int)(range.From ?? throw new InvalidOperationException("Resume range has no start.")));
            var remaining = asset[offset..];
            var content = new ByteArrayContent(remaining);
            content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(
                offset,
                asset.Length - 1,
                asset.Length);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content });
        }
    }

    private sealed class InterruptingReadStream(byte[] bytes, int interruptAfter) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => bytes.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_position >= interruptAfter)
            {
                throw new IOException("Simulated network interruption.");
            }

            var count = Math.Min(buffer.Length, interruptAfter - _position);
            bytes.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return ValueTask.FromResult(count);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
