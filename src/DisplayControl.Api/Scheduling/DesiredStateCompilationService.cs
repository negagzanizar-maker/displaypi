using DisplayControl.Api.Security;
using DisplayControl.Application.Content;
using DisplayControl.Domain.Content;
using DisplayControl.Domain.Scheduling;
using DisplayControl.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DisplayControl.Api.Scheduling;

public sealed class DesiredStateCompilationService(DisplayControlDbContext dbContext)
{
    public async Task<IReadOnlyList<DesiredStateManifestAsset>> LoadApprovedPlaylistAsync(
        Guid playlistVersionId,
        CancellationToken cancellationToken)
    {
        var playlistVersion = await dbContext.PlaylistVersions.AsNoTracking().SingleOrDefaultAsync(
            value => value.Id == playlistVersionId,
            cancellationToken);
        if (playlistVersion is null)
        {
            throw new AssignmentPublicationException("playlist_not_found", "Playlist version was not found.");
        }

        if (!string.Equals(playlistVersion.PublicationState, "published", StringComparison.Ordinal))
        {
            throw new AssignmentPublicationException(
                "playlist_not_published",
                "Only an immutable published playlist can be assigned.");
        }

        var items = await dbContext.PlaylistItems.AsNoTracking()
            .Where(value => value.PlaylistVersionId == playlistVersionId)
            .OrderBy(value => value.Position)
            .ToListAsync(cancellationToken);
        var captionIds = new List<Guid>();
        foreach (var item in items)
        {
            if (!PlaylistItemPresentation.TryReadCaptionContentVersionId(item.PresentationJson, out var captionId))
            {
                throw new AssignmentPublicationException(
                    "playlist_presentation_invalid",
                    "Playlist presentation metadata is invalid.");
            }

            if (captionId.HasValue) captionIds.Add(captionId.Value);
        }

        var contentIds = items.Select(value => value.ContentVersionId).Concat(captionIds).Distinct().ToArray();
        var versions = await dbContext.ContentVersions.AsNoTracking()
            .Where(value => contentIds.Contains(value.Id))
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        var assetIds = versions.Values.Select(value => value.ContentAssetId).ToArray();
        var assets = await dbContext.ContentAssets.AsNoTracking()
            .Where(value => assetIds.Contains(value.Id))
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        if (items.Count == 0 || versions.Count != contentIds.Length ||
            versions.Values.Any(value => value.ApprovedAtUtc is null || value.ScanState != "clean" ||
                !assets.TryGetValue(value.ContentAssetId, out var asset) ||
                asset.LifecycleState != ContentLifecycleState.Approved))
        {
            throw new AssignmentPublicationException(
                "playlist_content_unavailable",
                "Playlist content is no longer approved.");
        }

        var manifestAssets = items.Select((item, position) =>
        {
            var version = versions[item.ContentVersionId];
            var asset = assets[version.ContentAssetId];
            return new DesiredStateManifestAsset(
                version.Id,
                position,
                asset.MediaKind,
                version.ByteLength,
                version.Sha256,
                item.DurationMilliseconds,
                item.LoopVideo,
                item.PresentationJson);
        }).ToList();

        foreach (var captionId in captionIds.Distinct())
        {
            var version = versions[captionId];
            var asset = assets[version.ContentAssetId];
            if (asset.MediaKind != MediaKind.PlainText)
            {
                throw new AssignmentPublicationException(
                    "playlist_caption_invalid",
                    "Video captions must reference approved text content.");
            }

            manifestAssets.Add(new DesiredStateManifestAsset(
                version.Id,
                manifestAssets.Count,
                asset.MediaKind,
                version.ByteLength,
                version.Sha256,
                1_000,
                false,
                PlaylistItemPresentation.CaptionAssetJson()));
        }

        return manifestAssets;
    }

    public Task<DesiredState> CompileDeviceAssignmentAsync(
        Guid tenantId,
        Guid deviceId,
        DeviceAssignment assignment,
        IReadOnlyList<DesiredStateManifestAsset> assets,
        CancellationToken cancellationToken) => CompileAsync(
            tenantId,
            deviceId,
            assignment.Id,
            null,
            assignment.StartsAtUtc,
            assignment.EndsAtUtc,
            assignment.PublishedAtUtc,
            assets,
            cancellationToken);

    public Task<DesiredState> CompileGroupAssignmentAsync(
        Guid tenantId,
        Guid deviceId,
        GroupAssignment assignment,
        IReadOnlyList<DesiredStateManifestAsset> assets,
        CancellationToken cancellationToken) => CompileAsync(
            tenantId,
            deviceId,
            null,
            assignment.Id,
            assignment.StartsAtUtc,
            assignment.EndsAtUtc,
            assignment.PublishedAtUtc,
            assets,
            cancellationToken);

    private async Task<DesiredState> CompileAsync(
        Guid tenantId,
        Guid deviceId,
        Guid? sourceDeviceAssignmentId,
        Guid? sourceGroupAssignmentId,
        DateTimeOffset? startsAtUtc,
        DateTimeOffset? endsAtUtc,
        DateTimeOffset publishedAtUtc,
        IReadOnlyList<DesiredStateManifestAsset> assets,
        CancellationToken cancellationToken)
    {
        await MutationLocks.SchedulingAsync(dbContext, tenantId, cancellationToken);
        var existing = dbContext.DesiredStates.Local.FirstOrDefault(value =>
            value.DeviceId == deviceId && value.SourceDeviceAssignmentId == sourceDeviceAssignmentId &&
            value.SourceGroupAssignmentId == sourceGroupAssignmentId);
        existing ??= await dbContext.DesiredStates.Where(value =>
            value.DeviceId == deviceId && value.SourceDeviceAssignmentId == sourceDeviceAssignmentId &&
            value.SourceGroupAssignmentId == sourceGroupAssignmentId)
            .OrderByDescending(value => value.Version).FirstOrDefaultAsync(cancellationToken);
        if (existing is not null) return existing;

        var persistedMaximum = await dbContext.DesiredStates
            .Where(value => value.DeviceId == deviceId)
            .MaxAsync(value => (long?)value.Version, cancellationToken) ?? 0;
        var trackedMaximum = dbContext.ChangeTracker.Entries<DesiredState>()
            .Where(value => value.State == EntityState.Added && value.Entity.DeviceId == deviceId)
            .Select(value => value.Entity.Version)
            .DefaultIfEmpty(0)
            .Max();
        var nextVersion = Math.Max(persistedMaximum, trackedMaximum) + 1;
        var desiredStateId = Guid.NewGuid();
        var manifestSha256 = DesiredStateManifestCodec.CalculateSha256(
            desiredStateId,
            nextVersion,
            startsAtUtc,
            endsAtUtc,
            publishedAtUtc,
            assets);
        var desiredState = sourceDeviceAssignmentId is Guid deviceAssignmentId
            ? new DesiredState(
                desiredStateId,
                tenantId,
                deviceId,
                nextVersion,
                deviceAssignmentId,
                startsAtUtc,
                endsAtUtc,
                manifestSha256,
                publishedAtUtc)
            : DesiredState.FromGroupAssignment(
                desiredStateId,
                tenantId,
                deviceId,
                nextVersion,
                sourceGroupAssignmentId ?? throw new InvalidOperationException("Assignment source is missing."),
                startsAtUtc,
                endsAtUtc,
                manifestSha256,
                publishedAtUtc);

        dbContext.DesiredStates.Add(desiredState);
        dbContext.DesiredStateAssets.AddRange(assets.Select(asset => new DesiredStateAsset(
            Guid.NewGuid(),
            tenantId,
            desiredState.Id,
            asset.ContentVersionId,
            asset.Position,
            asset.MediaKind,
            asset.ByteLength,
            asset.Sha256,
            asset.DurationMilliseconds,
            asset.LoopVideo,
            asset.PlaybackJson)));
        return desiredState;
    }
}

public sealed class AssignmentPublicationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
