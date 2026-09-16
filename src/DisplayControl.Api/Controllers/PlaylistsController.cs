using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using DisplayControl.Api.Pagination;
using DisplayControl.Api.Security;
using DisplayControl.Application.Content;
using DisplayControl.Domain.Content;
using DisplayControl.Domain.Operations;
using DisplayControl.Domain.Playlists;
using DisplayControl.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DisplayControl.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId:guid}/playlists")]
public sealed class PlaylistsController(
    DisplayControlDbContext dbContext,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.TenantViewer)]
    public async Task<ActionResult<IReadOnlyList<PlaylistResponse>>> List(
        Guid tenantId,
        [FromQuery, Range(1, 100)] int limit = 50,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        if (!CursorPage.TryReadOffset(cursor, out var offset)) return BadRequest("Invalid pagination cursor.");
        var playlists = await dbContext.Playlists.AsNoTracking()
            .Where(value => value.ArchivedAtUtc == null)
            .OrderByDescending(value => value.UpdatedAtUtc)
            .ThenBy(value => value.Id)
            .Skip(offset)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        CursorPage.WriteNext(Response, offset, limit, playlists.Count);
        playlists = playlists.Take(limit).ToList();
        var ids = playlists.Select(value => value.Id).ToArray();
        var versions = await dbContext.PlaylistVersions.AsNoTracking()
            .Where(value => ids.Contains(value.PlaylistId))
            .OrderByDescending(value => value.VersionNumber)
            .ToListAsync(cancellationToken);
        var latest = versions.GroupBy(value => value.PlaylistId)
            .ToDictionary(group => group.Key, group => group.First());
        var latestIds = latest.Values.Select(value => value.Id).ToArray();
        var counts = await dbContext.PlaylistItems.AsNoTracking()
            .Where(value => latestIds.Contains(value.PlaylistVersionId))
            .GroupBy(value => value.PlaylistVersionId)
            .Select(group => new { Id = group.Key, Count = group.Count() })
            .ToDictionaryAsync(value => value.Id, value => value.Count, cancellationToken);
        return Ok(playlists.Select(value => ToResponse(value, latest.GetValueOrDefault(value.Id),
            latest.TryGetValue(value.Id, out var version) ? counts.GetValueOrDefault(version.Id) : 0)).ToArray());
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.TenantContentManager)]
    public async Task<ActionResult<PlaylistResponse>> Create(
        Guid tenantId,
        CreatePlaylistRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items.Count is < 1 or > 100 ||
            request.Items.Select(value => value.ContentVersionId).Distinct().Count() != request.Items.Count)
        {
            return InvalidPlaylist("playlist_items_invalid", "A playlist requires 1–100 unique content versions.");
        }

        var versionIds = request.Items.Select(value => value.ContentVersionId).ToArray();
        var captionVersionIds = request.Items
            .Where(value => value.CaptionContentVersionId.HasValue)
            .Select(value => value.CaptionContentVersionId!.Value)
            .Distinct()
            .ToArray();
        if (captionVersionIds.Any(versionIds.Contains))
        {
            return InvalidPlaylist("playlist_caption_invalid", "A caption must be a separate text content item.");
        }

        var referencedVersionIds = versionIds.Concat(captionVersionIds).Distinct().ToArray();
        var contentVersions = await dbContext.ContentVersions
            .Where(value => referencedVersionIds.Contains(value.Id))
            .ToListAsync(cancellationToken);
        var assetIds = contentVersions.Select(value => value.ContentAssetId).ToArray();
        var assets = await dbContext.ContentAssets
            .Where(value => assetIds.Contains(value.Id))
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        if (contentVersions.Count != referencedVersionIds.Length ||
            contentVersions.Any(value => value.ApprovedAtUtc is null ||
                !string.Equals(value.ScanState, "clean", StringComparison.Ordinal) ||
                !assets.TryGetValue(value.ContentAssetId, out var asset) ||
                asset.LifecycleState != ContentLifecycleState.Approved))
        {
            return ConflictProblem("content_not_approved", "Every playlist item must reference approved clean content.");
        }

        var contentById = contentVersions.ToDictionary(value => value.Id);
        for (var index = 0; index < request.Items.Count; index++)
        {
            var item = request.Items[index];
            var mediaKind = assets[contentById[item.ContentVersionId].ContentAssetId].MediaKind;
            var hasInlineCaption = !string.IsNullOrWhiteSpace(item.CaptionText);
            var supportsCaption = mediaKind is MediaKind.Jpeg or MediaKind.Png or MediaKind.WebP or MediaKind.Mp4;
            if ((mediaKind != MediaKind.Mp4 && item.DurationMilliseconds is null) ||
                item.DurationMilliseconds is < 1000 or > 86_400_000 ||
                (mediaKind != MediaKind.Mp4 && item.LoopVideo) ||
                item.CaptionContentVersionId.HasValue && hasInlineCaption ||
                hasInlineCaption && !supportsCaption ||
                item.CaptionContentVersionId is Guid captionId &&
                (!supportsCaption ||
                    assets[contentById[captionId].ContentAssetId].MediaKind != MediaKind.PlainText))
            {
                return InvalidPlaylist(
                    "playlist_presentation_invalid",
                    "Images and text require a 1-second to 24-hour duration; only videos can loop, and images or videos can have one caption.");
            }
        }

        var actorId = CurrentUserId();
        var nowUtc = timeProvider.GetUtcNow();
        var playlist = new Playlist(
            Guid.NewGuid(),
            tenantId,
            request.Name,
            request.Description,
            actorId,
            nowUtc);
        var version = new PlaylistVersion(
            Guid.NewGuid(),
            tenantId,
            playlist.Id,
            1,
            playlist.Name,
            playlist.Description,
            actorId,
            nowUtc);
        dbContext.Playlists.Add(playlist);
        dbContext.PlaylistVersions.Add(version);
        dbContext.PlaylistItems.AddRange(request.Items.Select((item, position) => new PlaylistItem(
            Guid.NewGuid(),
            tenantId,
            version.Id,
            item.ContentVersionId,
            position,
            item.DurationMilliseconds,
            item.LoopVideo,
            PlaylistItemPresentation.Serialize(item.CaptionContentVersionId, item.CaptionText))));
        AddAudit(tenantId, actorId, "playlist.created", playlist.Id, new { versionId = version.Id }, nowUtc);
        if (request.PublishImmediately)
        {
            version.Publish(actorId, nowUtc);
            AddAudit(tenantId, actorId, "playlist.published", playlist.Id, new { versionId = version.Id }, nowUtc);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { tenantId, playlistId = playlist.Id }, ToResponse(
            playlist,
            version,
            request.Items.Count));
    }

    [HttpGet("{playlistId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.TenantViewer)]
    public async Task<ActionResult<PlaylistResponse>> Get(
        Guid tenantId,
        Guid playlistId,
        CancellationToken cancellationToken)
    {
        var playlist = await dbContext.Playlists.AsNoTracking().SingleOrDefaultAsync(
            value => value.Id == playlistId && value.ArchivedAtUtc == null,
            cancellationToken);
        if (playlist is null)
        {
            return NotFound();
        }

        var version = await dbContext.PlaylistVersions.AsNoTracking()
            .Where(value => value.PlaylistId == playlistId)
            .OrderByDescending(value => value.VersionNumber)
            .FirstAsync(cancellationToken);
        var itemCount = await dbContext.PlaylistItems.CountAsync(
            value => value.PlaylistVersionId == version.Id,
            cancellationToken);
        return Ok(ToResponse(playlist, version, itemCount));
    }

    [HttpGet("{playlistId:guid}/items")]
    [Authorize(Policy = AuthorizationPolicies.TenantViewer)]
    public async Task<ActionResult<IReadOnlyList<PlaylistItemResponse>>> ListItems(
        Guid tenantId,
        Guid playlistId,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.Playlists.AsNoTracking().AnyAsync(
                value => value.Id == playlistId && value.ArchivedAtUtc == null,
                cancellationToken))
        {
            return NotFound();
        }

        var versionId = await dbContext.PlaylistVersions.AsNoTracking()
            .Where(value => value.PlaylistId == playlistId)
            .OrderByDescending(value => value.VersionNumber)
            .Select(value => value.Id)
            .FirstAsync(cancellationToken);
        var items = await dbContext.PlaylistItems.AsNoTracking()
            .Where(value => value.PlaylistVersionId == versionId)
            .OrderBy(value => value.Position)
            .ToListAsync(cancellationToken);
        var versionIds = items.Select(value => value.ContentVersionId).Distinct().ToArray();
        var content = await (
            from version in dbContext.ContentVersions.AsNoTracking()
            join asset in dbContext.ContentAssets.AsNoTracking()
                on version.ContentAssetId equals asset.Id
            where versionIds.Contains(version.Id)
            select new { VersionId = version.Id, asset.Title, asset.MediaKind })
            .ToDictionaryAsync(value => value.VersionId, cancellationToken);

        return Ok(items.Select(value => new PlaylistItemResponse(
            value.Id,
            value.ContentVersionId,
            content[value.ContentVersionId].Title,
            content[value.ContentVersionId].MediaKind.ToString(),
            value.Position,
            value.DurationMilliseconds,
            value.LoopVideo)).ToArray());
    }

    [HttpPost("{playlistId:guid}/items/{itemId:guid}/remove")]
    [Authorize(Policy = AuthorizationPolicies.TenantContentManager)]
    public async Task<ActionResult<PlaylistResponse>> RemoveItem(
        Guid tenantId,
        Guid playlistId,
        Guid itemId,
        ChangePlaylistRequest request,
        CancellationToken cancellationToken)
    {
        var playlist = await dbContext.Playlists.SingleOrDefaultAsync(
            value => value.Id == playlistId && value.ArchivedAtUtc == null,
            cancellationToken);
        if (playlist is null) return NotFound();
        if (playlist.ConcurrencyToken != request.ConcurrencyToken)
        {
            return ConflictProblem("concurrency_conflict", "The playlist changed. Refresh it before retrying.");
        }

        var previousVersion = await dbContext.PlaylistVersions
            .Where(value => value.PlaylistId == playlistId)
            .OrderByDescending(value => value.VersionNumber)
            .FirstAsync(cancellationToken);
        var previousItems = await dbContext.PlaylistItems.AsNoTracking()
            .Where(value => value.PlaylistVersionId == previousVersion.Id)
            .OrderBy(value => value.Position)
            .ToListAsync(cancellationToken);
        var removed = previousItems.SingleOrDefault(value => value.Id == itemId);
        if (removed is null) return NotFound();
        if (previousItems.Count == 1)
        {
            return ConflictProblem("playlist_last_item", "A playlist cannot be empty. Delete the playlist instead.");
        }

        var actorId = CurrentUserId();
        var nowUtc = timeProvider.GetUtcNow();
        var nextVersion = new PlaylistVersion(
            Guid.NewGuid(), tenantId, playlist.Id, previousVersion.VersionNumber + 1,
            playlist.Name, playlist.Description, actorId, nowUtc);
        nextVersion.Publish(actorId, nowUtc);
        dbContext.PlaylistVersions.Add(nextVersion);
        dbContext.PlaylistItems.AddRange(previousItems
            .Where(value => value.Id != itemId)
            .Select((value, position) => new PlaylistItem(
                Guid.NewGuid(), tenantId, nextVersion.Id, value.ContentVersionId, position,
                value.DurationMilliseconds, value.LoopVideo, value.PresentationJson)));
        playlist.RecordRevision(nowUtc);
        AddAudit(tenantId, actorId, "playlist.item_removed", playlist.Id, new
        {
            previousVersionId = previousVersion.Id,
            versionId = nextVersion.Id,
            removed.ContentVersionId
        }, nowUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(playlist, nextVersion, previousItems.Count - 1));
    }

    [HttpPost("{playlistId:guid}/archive")]
    [Authorize(Policy = AuthorizationPolicies.TenantContentManager)]
    public async Task<IActionResult> Archive(
        Guid tenantId,
        Guid playlistId,
        ChangePlaylistRequest request,
        CancellationToken cancellationToken)
    {
        var playlist = await dbContext.Playlists.SingleOrDefaultAsync(
            value => value.Id == playlistId && value.ArchivedAtUtc == null,
            cancellationToken);
        if (playlist is null) return NotFound();
        if (playlist.ConcurrencyToken != request.ConcurrencyToken)
        {
            return ConflictProblem("concurrency_conflict", "The playlist changed. Refresh it before retrying.");
        }

        var actorId = CurrentUserId();
        var nowUtc = timeProvider.GetUtcNow();
        playlist.Archive(nowUtc);
        AddAudit(tenantId, actorId, "playlist.archived", playlist.Id, new { request.Reason }, nowUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{playlistId:guid}/versions/{versionId:guid}/publish")]
    [Authorize(Policy = AuthorizationPolicies.TenantContentManager)]
    public async Task<ActionResult<PlaylistResponse>> Publish(
        Guid tenantId,
        Guid playlistId,
        Guid versionId,
        CancellationToken cancellationToken)
    {
        var playlist = await dbContext.Playlists.SingleOrDefaultAsync(
            value => value.Id == playlistId,
            cancellationToken);
        var version = await dbContext.PlaylistVersions.SingleOrDefaultAsync(
            value => value.Id == versionId && value.PlaylistId == playlistId,
            cancellationToken);
        if (playlist is null || version is null)
        {
            return NotFound();
        }

        var items = await dbContext.PlaylistItems
            .Where(value => value.PlaylistVersionId == versionId)
            .ToListAsync(cancellationToken);
        var captionIds = new List<Guid>();
        foreach (var item in items)
        {
            if (!PlaylistItemPresentation.TryReadCaptionContentVersionId(item.PresentationJson, out var captionId))
            {
                return ConflictProblem("playlist_presentation_invalid", "Playlist presentation metadata is invalid.");
            }

            if (captionId.HasValue) captionIds.Add(captionId.Value);
        }

        var contentIds = items.Select(value => value.ContentVersionId).Concat(captionIds).Distinct().ToArray();
        var cleanCount = await dbContext.ContentVersions.CountAsync(
            value => contentIds.Contains(value.Id) && value.ApprovedAtUtc != null && value.ScanState == "clean",
            cancellationToken);
        if (items.Count == 0 || cleanCount != contentIds.Length)
        {
            return ConflictProblem("playlist_content_unavailable", "Playlist content is no longer publishable.");
        }

        try
        {
            var actorId = CurrentUserId();
            var nowUtc = timeProvider.GetUtcNow();
            version.Publish(actorId, nowUtc);
            AddAudit(tenantId, actorId, "playlist.published", playlist.Id, new { versionId }, nowUtc);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Ok(ToResponse(playlist, version, items.Count));
        }
        catch (InvalidOperationException)
        {
            return ConflictProblem("playlist_already_published", "The playlist version is already immutable.");
        }
    }

    private void AddAudit(Guid tenantId, Guid actorId, string action, Guid targetId, object details, DateTimeOffset atUtc) =>
        dbContext.AuditEvents.Add(new TenantAuditEvent(
            Guid.NewGuid(),
            tenantId,
            "user",
            actorId,
            action,
            "playlist",
            targetId,
            "success",
            null,
            HttpContext.TraceIdentifierGuid(),
            JsonSerializer.Serialize(details),
            atUtc));

    private Guid CurrentUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
        ? userId
        : throw new InvalidOperationException("Authenticated principal has no valid user identifier.");

    private static PlaylistResponse ToResponse(Playlist playlist, PlaylistVersion? version, int itemCount) => new(
        playlist.Id,
        playlist.Name,
        playlist.Description,
        playlist.UpdatedAtUtc,
        playlist.ConcurrencyToken,
        version is null
            ? null
            : new PlaylistVersionResponse(
                version.Id,
                version.VersionNumber,
                version.PublicationState,
                version.CreatedAtUtc,
                version.PublishedAtUtc,
                itemCount));

    private static ObjectResult InvalidPlaylist(string code, string title) => new(new ProblemDetails
    {
        Type = "https://docs.example.invalid/problems/playlist-validation",
        Title = title,
        Status = StatusCodes.Status400BadRequest,
        Extensions = { ["code"] = code }
    })
    {
        StatusCode = StatusCodes.Status400BadRequest
    };

    private static ObjectResult ConflictProblem(string code, string title) => new(new ProblemDetails
    {
        Type = "https://docs.example.invalid/problems/resource-state",
        Title = title,
        Status = StatusCodes.Status409Conflict,
        Extensions = { ["code"] = code }
    })
    {
        StatusCode = StatusCodes.Status409Conflict
    };
}

public sealed record CreatePlaylistRequest(
    [param: Required, StringLength(200, MinimumLength = 1)] string Name,
    [param: StringLength(2000)] string? Description,
    [param: Required] IReadOnlyList<CreatePlaylistItemRequest> Items,
    bool PublishImmediately = false);

public sealed record CreatePlaylistItemRequest(
    Guid ContentVersionId,
    [param: Range(1000, 86_400_000)] int? DurationMilliseconds,
    bool LoopVideo = false,
    Guid? CaptionContentVersionId = null,
    [param: StringLength(PlaylistItemPresentation.MaximumCaptionTextLength)] string? CaptionText = null);

public sealed record ChangePlaylistRequest(
    Guid ConcurrencyToken,
    [param: StringLength(500)] string? Reason = null);

public sealed record PlaylistItemResponse(
    Guid Id,
    Guid ContentVersionId,
    string Title,
    string MediaKind,
    int Position,
    int? DurationMilliseconds,
    bool LoopVideo);

public sealed record PlaylistResponse(
    Guid Id,
    string Name,
    string? Description,
    DateTimeOffset UpdatedAtUtc,
    Guid ConcurrencyToken,
    PlaylistVersionResponse? LatestVersion);

public sealed record PlaylistVersionResponse(
    Guid Id,
    int VersionNumber,
    string PublicationState,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? PublishedAtUtc,
    int ItemCount);
