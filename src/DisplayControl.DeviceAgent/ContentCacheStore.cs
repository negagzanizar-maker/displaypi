using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace DisplayControl.DeviceAgent;

public sealed class ContentCacheStore : IDisposable
{
    private const int MaximumManifestBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _root;
    private readonly string _objectRoot;
    private readonly string _manifestRoot;
    private readonly string _stagingRoot;
    private readonly long _maximumCacheBytes;
    private readonly long _minimumFreeDiskBytes;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ContentCacheStore(IOptions<AgentRuntimeOptions> options)
    {
        _root = Path.Combine(Path.GetFullPath(options.Value.StateDirectory), "content-cache");
        _objectRoot = Path.Combine(_root, "objects");
        _manifestRoot = Path.Combine(_root, "manifests");
        _stagingRoot = Path.Combine(_root, "staging");
        _maximumCacheBytes = options.Value.MaximumCacheBytes;
        _minimumFreeDiskBytes = options.Value.MinimumFreeDiskBytes;
        CreateSafeDirectory(_root);
        CreateSafeDirectory(_objectRoot);
        CreateSafeDirectory(_manifestRoot);
        CreateSafeDirectory(_stagingRoot);
    }

    public Task<ContentCacheActivation> SynchronizeAsync(
        HttpClient client,
        Guid desiredStateId,
        long desiredStateVersion,
        string expectedManifestSha256,
        CancellationToken cancellationToken) => SynchronizeAsync(
            client,
            desiredStateId,
            desiredStateVersion,
            expectedManifestSha256,
            new HashSet<string>(StringComparer.Ordinal),
            cancellationToken);

    public async Task<ContentCacheActivation> SynchronizeAsync(
        HttpClient client,
        Guid desiredStateId,
        long desiredStateVersion,
        string expectedManifestSha256,
        IReadOnlySet<string> protectedObjectHashes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(protectedObjectHashes);
        // Bound an entire synchronization attempt, including ResponseHeadersRead bodies.
        // Verified partial files are resumed on the next heartbeat.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        cancellationToken = deadline.Token;
        if (desiredStateId == Guid.Empty || desiredStateVersion <= 0 || !IsSha256Hex(expectedManifestSha256))
        {
            throw new InvalidDataException("Desired-state cache bindings are invalid.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var manifestResponse = await client.GetAsync(
                $"/device/v1/desired-states/{desiredStateId:D}/manifest",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            manifestResponse.EnsureSuccessStatusCode();
            await using var manifestStream = await manifestResponse.Content.ReadAsStreamAsync(cancellationToken);
            var manifestBytes = await ReadBoundedAsync(manifestStream, MaximumManifestBytes, cancellationToken);
            var manifestDigest = SHA256.HashData(manifestBytes);
            var expectedDigest = Convert.FromHexString(expectedManifestSha256);
            if (!CryptographicOperations.FixedTimeEquals(manifestDigest, expectedDigest))
            {
                throw new CryptographicException("Downloaded manifest does not match the signed licence lease.");
            }

            var manifest = JsonSerializer.Deserialize<DownloadedManifest>(
                manifestBytes,
                JsonOptions)
                ?? throw new InvalidDataException("Desired-state manifest is empty.");
            ValidateManifest(manifest, desiredStateId, desiredStateVersion);
            if (manifest.Assets.Sum(value => value.ByteLength) > _maximumCacheBytes)
            {
                throw new InvalidDataException("Desired-state assets exceed the configured cache ceiling.");
            }

            var targetHashes = manifest.Assets.Select(value => value.Sha256).ToHashSet(StringComparer.Ordinal);
            if (protectedObjectHashes.Any(value => !IsSha256Hex(value)))
            {
                throw new InvalidDataException("Protected cache object bindings are invalid.");
            }

            var retainedHashes = targetHashes
                .Concat(protectedObjectHashes)
                .ToHashSet(StringComparer.Ordinal);
            var stageDirectory = Path.Combine(
                _stagingRoot,
                $"{desiredStateId:N}-{desiredStateVersion}-{expectedManifestSha256}");
            CreateSafeDirectory(stageDirectory);
            RemoveStaleStagingDirectories(stageDirectory);

            var validObjectHashes = new HashSet<string>(StringComparer.Ordinal);
            long requiredObjectBytes = 0;
            long requiredDiskBytes = 0;
            foreach (var asset in manifest.Assets)
            {
                var objectPath = Path.Combine(_objectRoot, asset.Sha256);
                if (await IsValidCachedObjectAsync(objectPath, asset, cancellationToken))
                {
                    validObjectHashes.Add(asset.Sha256);
                    continue;
                }

                DeleteInvalidRegularObject(objectPath);
                requiredObjectBytes = checked(requiredObjectBytes + asset.ByteLength);
                var partialPath = Path.Combine(stageDirectory, $"{asset.Sha256}.partial");
                var partialLength = await ValidatePartialFileAsync(partialPath, asset, cancellationToken);
                requiredDiskBytes = checked(requiredDiskBytes + asset.ByteLength - partialLength);
            }

            EnsureCacheCapacity(requiredObjectBytes, requiredDiskBytes, retainedHashes);
            var completed = false;
            try
            {
                var files = new Dictionary<Guid, PlayerAssetFile>();
                foreach (var asset in manifest.Assets.OrderBy(value => value.Position))
                {
                    var objectPath = Path.Combine(_objectRoot, asset.Sha256);
                    if (!validObjectHashes.Contains(asset.Sha256))
                    {
                        var stagedPath = Path.Combine(stageDirectory, $"{asset.Sha256}.partial");
                        await DownloadAssetAsync(client, manifest.DesiredStateId, asset, stagedPath, cancellationToken);
                        if (File.Exists(objectPath))
                        {
                            File.Delete(objectPath);
                        }

                        File.Move(stagedPath, objectPath, overwrite: false);
                        RestrictFile(objectPath);
                    }

                    files.Add(asset.ContentVersionId, new PlayerAssetFile(
                        objectPath,
                        ContentTypeFor(asset.MediaKind),
                        asset.ByteLength,
                        asset.Sha256));
                }

                var manifestPath = Path.Combine(
                    _manifestRoot,
                    $"{desiredStateId:N}-{desiredStateVersion}.json");
                var temporaryManifestPath = Path.Combine(stageDirectory, "manifest.json");
                await File.WriteAllBytesAsync(temporaryManifestPath, manifestBytes, cancellationToken);
                RestrictFile(temporaryManifestPath);
                File.Move(temporaryManifestPath, manifestPath, overwrite: true);
                RestrictFile(manifestPath);
                RemoveInactiveManifests(manifestPath);

                var activeManifest = new ActivePlayerManifest(
                    manifest.DesiredStateId,
                    manifest.Version,
                    manifest.Assets.OrderBy(value => value.Position).Select(value => new ActivePlayerAsset(
                        value.ContentVersionId,
                        value.Position,
                        value.MediaKind,
                        value.DurationMilliseconds,
                        value.LoopVideo,
                        ReadCaptionContentVersionId(value.Playback),
                        IsCaptionAsset(value.Playback))).ToArray());
                completed = true;
                return new ContentCacheActivation(activeManifest, files);
            }
            finally
            {
                if (completed && Directory.Exists(stageDirectory))
                {
                    Directory.Delete(stageDirectory, recursive: true);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task DownloadAssetAsync(
        HttpClient client,
        Guid desiredStateId,
        DownloadedManifestAsset asset,
        string targetPath,
        CancellationToken cancellationToken)
    {
        var existingLength = await ValidatePartialFileAsync(targetPath, asset, cancellationToken);
        if (existingLength == asset.ByteLength)
        {
            RestrictFile(targetPath);
            return;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/device/v1/desired-states/{desiredStateId:D}/assets/{asset.ContentVersionId:D}");
        if (existingLength > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingLength, null);
        }

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var append = existingLength > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (append)
        {
            var range = response.Content.Headers.ContentRange;
            if (range?.From != existingLength || range.To != asset.ByteLength - 1 || range.Length != asset.ByteLength)
            {
                throw new InvalidDataException("Asset range response does not match the manifest.");
            }
        }
        else if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidDataException("Asset server returned an unsupported range response.");
        }

        var writeOffset = append ? existingLength : 0;
        var expectedResponseLength = asset.ByteLength - writeOffset;
        if (response.Content.Headers.ContentLength is long contentLength && contentLength != expectedResponseLength)
        {
            throw new InvalidDataException("Asset response length does not match the manifest.");
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        if (writeOffset > 0)
        {
            await AppendExistingFileToHashAsync(targetPath, writeOffset, hash, cancellationToken);
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(
            targetPath,
            writeOffset == 0 ? FileMode.Create : FileMode.Open,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        output.Position = writeOffset;
        var buffer = new byte[128 * 1024];
        var total = writeOffset;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            total = checked(total + read);
            if (total > asset.ByteLength)
            {
                throw new InvalidDataException("Asset exceeds the manifest length.");
            }

            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        await output.FlushAsync(cancellationToken);
        var digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (total != asset.ByteLength || !string.Equals(digest, asset.Sha256, StringComparison.Ordinal))
        {
            throw new CryptographicException("Asset integrity verification failed.");
        }

        RestrictFile(targetPath);
    }

    private void EnsureCacheCapacity(
        long requiredObjectBytes,
        long requiredDiskBytes,
        HashSet<string> retainedHashes)
    {
        var objects = EnumerateRegularObjects();
        var currentBytes = objects.Aggregate(0L, (total, value) => checked(total + value.Length));
        foreach (var candidate in objects
                     .Where(value => !retainedHashes.Contains(value.Hash))
                     .OrderBy(value => value.LastWriteTimeUtc))
        {
            if (checked(currentBytes + requiredObjectBytes) <= _maximumCacheBytes)
            {
                break;
            }

            File.Delete(candidate.Path);
            currentBytes -= candidate.Length;
        }

        if (checked(currentBytes + requiredObjectBytes) > _maximumCacheBytes)
        {
            throw new IOException("The cache ceiling cannot fit the active and replacement content safely.");
        }

        var rootPath = Path.GetPathRoot(_root)
            ?? throw new IOException("The content cache filesystem root cannot be resolved.");
        var availableBytes = new DriveInfo(rootPath).AvailableFreeSpace;
        if (availableBytes < checked(requiredDiskBytes + _minimumFreeDiskBytes))
        {
            throw new IOException("The content cache filesystem does not have the configured free-space reserve.");
        }
    }

    private List<CachedObject> EnumerateRegularObjects()
    {
        var result = new List<CachedObject>();
        foreach (var path in Directory.EnumerateFileSystemEntries(_objectRoot, "*", SearchOption.TopDirectoryOnly))
        {
            var info = new FileInfo(path);
            if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                Directory.Exists(path) || !IsSha256Hex(info.Name))
            {
                throw new InvalidOperationException("The content cache contains an unsafe object entry.");
            }

            result.Add(new CachedObject(info.Name, info.FullName, info.Length, info.LastWriteTimeUtc));
        }

        return result;
    }

    private static void DeleteInvalidRegularObject(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var info = new FileInfo(path);
        if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException("A cache object cannot be replaced through a link or reparse point.");
        }

        File.Delete(path);
    }

    private void RemoveStaleStagingDirectories(string retainedDirectory)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(_stagingRoot, "*", SearchOption.TopDirectoryOnly))
        {
            if (string.Equals(path, retainedDirectory, StringComparison.Ordinal))
            {
                continue;
            }

            DeleteSafeStagingDirectory(path);
        }
    }

    private static void DeleteSafeStagingDirectory(string path)
    {
        var directory = new DirectoryInfo(path);
        if (!directory.Exists || directory.LinkTarget is not null ||
            directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException("The content cache contains an unsafe staging entry.");
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.TopDirectoryOnly))
        {
            var file = new FileInfo(entry);
            if (file.LinkTarget is not null || file.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                Directory.Exists(entry))
            {
                throw new InvalidOperationException("The content cache contains an unsafe staged object.");
            }

            File.Delete(entry);
        }

        Directory.Delete(path, recursive: false);
    }

    private void RemoveInactiveManifests(string retainedManifestPath)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(_manifestRoot, "*", SearchOption.TopDirectoryOnly))
        {
            if (string.Equals(path, retainedManifestPath, StringComparison.Ordinal))
            {
                continue;
            }

            var info = new FileInfo(path);
            if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                Directory.Exists(path))
            {
                throw new InvalidOperationException("The content cache contains an unsafe manifest entry.");
            }

            File.Delete(path);
        }
    }

    private static async Task<long> ValidatePartialFileAsync(
        string path,
        DownloadedManifestAsset asset,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (info.LinkTarget is not null)
        {
            throw new InvalidOperationException("Partial content cache file is a link or reparse point.");
        }

        if (!info.Exists)
        {
            return 0;
        }

        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException("Partial content cache file is a link or reparse point.");
        }

        if (info.Length > asset.ByteLength)
        {
            File.Delete(path);
            return 0;
        }

        if (info.Length == asset.ByteLength)
        {
            await using var complete = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var digest = Convert.ToHexString(await SHA256.HashDataAsync(complete, cancellationToken)).ToLowerInvariant();
            if (string.Equals(digest, asset.Sha256, StringComparison.Ordinal))
            {
                return info.Length;
            }

            File.Delete(path);
            return 0;
        }

        return info.Length;
    }

    private static async Task AppendExistingFileToHashAsync(
        string path,
        long expectedLength,
        IncrementalHash hash,
        CancellationToken cancellationToken)
    {
        await using var existing = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (existing.Length != expectedLength)
        {
            throw new IOException("Partial content cache file changed during resume.");
        }

        var buffer = new byte[128 * 1024];
        while (true)
        {
            var read = await existing.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            hash.AppendData(buffer, 0, read);
        }
    }

    private static async Task<bool> IsValidCachedObjectAsync(
        string path,
        DownloadedManifestAsset asset,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var info = new FileInfo(path);
        if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
            info.Length != asset.ByteLength)
        {
            return false;
        }

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
        var digest = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        return string.Equals(digest, asset.Sha256, StringComparison.Ordinal);
    }

    private static void ValidateManifest(DownloadedManifest manifest, Guid expectedId, long expectedVersion)
    {
        if (manifest.DesiredStateId != expectedId || manifest.Version != expectedVersion ||
            manifest.PublishedAtUtc.Offset != TimeSpan.Zero ||
            manifest.StartsAtUtc is { Offset: var startOffset } && startOffset != TimeSpan.Zero ||
            manifest.EndsAtUtc is { Offset: var endOffset } && endOffset != TimeSpan.Zero ||
            manifest.StartsAtUtc.HasValue && manifest.EndsAtUtc.HasValue &&
            manifest.EndsAtUtc <= manifest.StartsAtUtc ||
            manifest.Assets.Count is < 1 or > 200 ||
            manifest.Assets.Select(value => value.ContentVersionId).Distinct().Count() != manifest.Assets.Count)
        {
            throw new InvalidDataException("Desired-state manifest bindings are invalid.");
        }

        var ordered = manifest.Assets.OrderBy(value => value.Position).ToArray();
        var captionIds = ordered
            .Where(value => IsCaptionAsset(value.Playback))
            .Select(value => value.ContentVersionId)
            .ToHashSet();
        for (var index = 0; index < ordered.Length; index++)
        {
            var asset = ordered[index];
            var captionContentVersionId = ReadCaptionContentVersionId(asset.Playback);
            if (asset.Position != index || asset.ContentVersionId == Guid.Empty || asset.ByteLength <= 0 ||
                !IsSha256Hex(asset.Sha256) || asset.MediaKind is not ("plainText" or "jpeg" or "png" or "webP" or "mp4") ||
                asset.DurationMilliseconds is <= 0 || asset.DurationMilliseconds is > 86_400_000 ||
                asset.MediaKind != "mp4" && asset.DurationMilliseconds is null ||
                asset.MediaKind != "mp4" && asset.LoopVideo ||
                IsCaptionAsset(asset.Playback) && asset.MediaKind != "plainText" ||
                captionContentVersionId.HasValue &&
                (asset.MediaKind != "mp4" || !captionIds.Contains(captionContentVersionId.Value)))
            {
                throw new InvalidDataException("Desired-state manifest contains an invalid asset.");
            }
        }
    }

    private static Guid? ReadCaptionContentVersionId(JsonElement playback) =>
        playback.ValueKind == JsonValueKind.Object &&
        playback.TryGetProperty("captionContentVersionId", out var value) &&
        value.ValueKind == JsonValueKind.String && value.TryGetGuid(out var captionId) && captionId != Guid.Empty
            ? captionId
            : null;

    private static bool IsCaptionAsset(JsonElement playback) =>
        playback.ValueKind == JsonValueKind.Object &&
        playback.TryGetProperty("role", out var value) &&
        value.ValueKind == JsonValueKind.String && value.GetString() == "caption";

    private static async Task<byte[]> ReadBoundedAsync(
        Stream input,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[32 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (output.Length + read > maximumBytes)
            {
                throw new InvalidDataException("Desired-state manifest exceeds the safe size limit.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return output.ToArray();
    }

    private static string ContentTypeFor(string mediaKind) => mediaKind switch
    {
        "plainText" => "text/plain; charset=utf-8",
        "jpeg" => "image/jpeg",
        "png" => "image/png",
        "webP" => "image/webp",
        "mp4" => "video/mp4",
        _ => throw new InvalidDataException("Unsupported manifest media kind.")
    };

    private static bool IsSha256Hex(string? value) => value is { Length: 64 } && value.All(
        character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void CreateSafeDirectory(string path)
    {
        Directory.CreateDirectory(path);
        var info = new DirectoryInfo(path);
        if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException("Content cache paths cannot be symbolic links or reparse points.");
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static void RestrictFile(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private sealed record DownloadedManifest(
        Guid DesiredStateId,
        long Version,
        DateTimeOffset PublishedAtUtc,
        DateTimeOffset? StartsAtUtc,
        DateTimeOffset? EndsAtUtc,
        IReadOnlyList<DownloadedManifestAsset> Assets);

    private sealed record DownloadedManifestAsset(
        Guid ContentVersionId,
        int Position,
        string MediaKind,
        long ByteLength,
        string Sha256,
        int? DurationMilliseconds,
        bool LoopVideo,
        JsonElement Playback);

    private sealed record CachedObject(string Hash, string Path, long Length, DateTime LastWriteTimeUtc);

    public void Dispose() => _gate.Dispose();
}

public sealed record ContentCacheActivation(
    ActivePlayerManifest Manifest,
    IReadOnlyDictionary<Guid, PlayerAssetFile> AssetFiles);
