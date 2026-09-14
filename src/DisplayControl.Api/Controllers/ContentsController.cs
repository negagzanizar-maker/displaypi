using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using DisplayControl.Api.Pagination;
using DisplayControl.Api.Security;
using DisplayControl.Application.Content;
using DisplayControl.Application.Storage;
using DisplayControl.Domain.Content;
using DisplayControl.Domain.Operations;
using DisplayControl.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DisplayControl.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId:guid}/contents")]
public sealed class ContentsController(
    DisplayControlDbContext dbContext,
    IPrivateObjectStore objectStore,
    IContentMalwareScanner malwareScanner,
    ContentStorageOptions storageOptions,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.TenantViewer)]
    public async Task<ActionResult<IReadOnlyList<ContentResponse>>> List(
        Guid tenantId,
        [FromQuery, Range(1, 100)] int limit = 50,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        if (!CursorPage.TryReadOffset(cursor, out var offset)) return BadRequest("Invalid pagination cursor.");
        var assets = await dbContext.ContentAssets.AsNoTracking()
            .OrderByDescending(value => value.UpdatedAtUtc)
            .ThenBy(value => value.Id)
            .Skip(offset)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        CursorPage.WriteNext(Response, offset, limit, assets.Count);
        assets = assets.Take(limit).ToList();
        var assetIds = assets.Select(value => value.Id).ToArray();
        var versions = await dbContext.ContentVersions.AsNoTracking()
            .Where(value => assetIds.Contains(value.ContentAssetId))
            .OrderByDescending(value => value.VersionNumber)
            .ToListAsync(cancellationToken);
        var latestVersions = versions
            .GroupBy(value => value.ContentAssetId)
            .ToDictionary(group => group.Key, group => group.First());

        return Ok(assets.Select(asset => ToResponse(
            asset,
            latestVersions.GetValueOrDefault(asset.Id))).ToArray());
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.TenantContentManager)]
    [SkipTenantTransaction]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ContentResponse>> Upload(
        Guid tenantId,
        [FromForm] ContentUploadRequest request,
        CancellationToken cancellationToken)
    {
        if (request.File.Length <= 0 || request.File.Length > storageOptions.MaximumObjectBytes)
        {
            return ValidationProblemResponse(
                "content_size_invalid",
                $"The file must contain between 1 and {storageOptions.MaximumObjectBytes} bytes.");
        }

        ContentFileInspection inspection;
        try
        {
            await using var inspectionStream = request.File.OpenReadStream();
            inspection = await ContentFileInspector.InspectAsync(
                inspectionStream,
                request.File.Length,
                cancellationToken);
        }
        catch (InvalidDataException)
        {
            return ValidationProblemResponse(
                "content_signature_invalid",
                "The file is not a supported image, video, or UTF-8 text document.");
        }

        var actorId = CurrentUserId();
        var nowUtc = timeProvider.GetUtcNow();
        var assetId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var objectKey = new PrivateObjectKey(tenantId, versionId);
        var displayFileName = NormalizeDisplayFileName(request.File.FileName);
        var stored = false;
        try
        {
            await using (var uploadStream = request.File.OpenReadStream())
            {
                await objectStore.PutAsync(objectKey, uploadStream, request.File.Length, cancellationToken);
                stored = true;
            }

            ContentMalwareScanResult scan;
            await using (var storedStream = await objectStore.OpenReadAsync(objectKey, cancellationToken))
            {
                scan = await malwareScanner.ScanAsync(storedStream, cancellationToken);
            }

            var domainOutcome = scan.Verdict switch
            {
                ContentMalwareScanVerdict.Clean => ContentScanOutcome.Clean,
                ContentMalwareScanVerdict.Infected => ContentScanOutcome.Infected,
                _ => ContentScanOutcome.Unavailable
            };
            var asset = new ContentAsset(
                assetId,
                tenantId,
                request.Title,
                inspection.MediaKind,
                actorId,
                nowUtc);
            asset.RecordScanOutcome(domainOutcome, nowUtc);
            var version = new ContentVersion(
                versionId,
                tenantId,
                assetId,
                1,
                objectKey.ToString(),
                request.File.Length,
                inspection.Sha256,
                inspection.MimeType,
                displayFileName,
                inspection.MetadataJson,
                actorId,
                nowUtc);
            version.RecordScanOutcome(domainOutcome, scan.EngineVersion, scan.SafeReasonCode);
            if (domainOutcome == ContentScanOutcome.Clean)
            {
                version.Approve(actorId, nowUtc);
            }

            await using var transaction = await dbContext.BeginTenantTransactionAsync(tenantId, cancellationToken);
            dbContext.ContentAssets.Add(asset);
            dbContext.ContentVersions.Add(version);
            dbContext.AuditEvents.Add(CreateAudit(
                tenantId,
                actorId,
                "content.uploaded",
                asset.Id,
                domainOutcome == ContentScanOutcome.Clean ? "success" : "restricted",
                scan.SafeReasonCode,
                new { versionId, mediaKind = inspection.MediaKind, byteLength = request.File.Length },
                nowUtc));
            if (domainOutcome == ContentScanOutcome.Clean)
            {
                dbContext.AuditEvents.Add(CreateAudit(
                    tenantId,
                    actorId,
                    "content.automatically_approved",
                    asset.Id,
                    "success",
                    null,
                    new { versionId, scanState = version.ScanState },
                    nowUtc));
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return CreatedAtAction(nameof(Get), new { tenantId, contentId = asset.Id }, ToResponse(asset, version));
        }
        catch
        {
            if (stored)
            {
                await objectStore.DeleteAsync(objectKey, CancellationToken.None);
            }

            throw;
        }
    }

    [HttpGet("{contentId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.TenantViewer)]
    public async Task<ActionResult<ContentResponse>> Get(
        Guid tenantId,
        Guid contentId,
        CancellationToken cancellationToken)
    {
        var asset = await dbContext.ContentAssets.AsNoTracking().SingleOrDefaultAsync(
            value => value.Id == contentId,
            cancellationToken);
        if (asset is null)
        {
            return NotFound();
        }

        var version = await dbContext.ContentVersions.AsNoTracking()
            .Where(value => value.ContentAssetId == contentId)
            .OrderByDescending(value => value.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken);
        return Ok(ToResponse(asset, version));
    }

    [HttpGet("{contentId:guid}/versions/{versionId:guid}/preview")]
    [Authorize(Policy = AuthorizationPolicies.TenantViewer)]
    public async Task<IActionResult> Preview(
        Guid tenantId,
        Guid contentId,
        Guid versionId,
        CancellationToken cancellationToken)
    {
        var content = await (
            from asset in dbContext.ContentAssets.AsNoTracking()
            join version in dbContext.ContentVersions.AsNoTracking()
                on asset.Id equals version.ContentAssetId
            where asset.Id == contentId && version.Id == versionId &&
                asset.LifecycleState == ContentLifecycleState.Approved &&
                version.ScanState == "clean" && version.ApprovedAtUtc != null
            select new
            {
                version.StorageKey,
                version.DetectedMimeType,
                version.ByteLength,
                version.Sha256
            }).SingleOrDefaultAsync(cancellationToken);
        if (content is null)
        {
            return NotFound();
        }

        var objectKey = new PrivateObjectKey(tenantId, versionId);
        if (!string.Equals(objectKey.ToString(), content.StorageKey, StringComparison.Ordinal) ||
            !await objectStore.ExistsAsync(objectKey, cancellationToken))
        {
            return NotFound();
        }

        var stream = await objectStore.OpenReadAsync(objectKey, cancellationToken);
        Response.Headers.ETag = $"\"{Convert.ToHexString(content.Sha256).ToLowerInvariant()}\"";
        Response.ContentLength = content.ByteLength;
        return File(stream, content.DetectedMimeType, enableRangeProcessing: true);
    }

    [HttpPost("{contentId:guid}/versions/{versionId:guid}/approve")]
    [Authorize(Policy = AuthorizationPolicies.TenantContentManager)]
    public async Task<ActionResult<ContentResponse>> Approve(
        Guid tenantId,
        Guid contentId,
        Guid versionId,
        ApproveContentRequest request,
        CancellationToken cancellationToken)
    {
        var asset = await dbContext.ContentAssets.SingleOrDefaultAsync(
            value => value.Id == contentId,
            cancellationToken);
        var version = await dbContext.ContentVersions.SingleOrDefaultAsync(
            value => value.Id == versionId && value.ContentAssetId == contentId,
            cancellationToken);
        if (asset is null || version is null)
        {
            return NotFound();
        }

        if (asset.ConcurrencyToken != request.ConcurrencyToken)
        {
            return ConflictProblem("concurrency_conflict", "The content changed. Refresh it before retrying.");
        }

        if (!string.Equals(version.ScanState, "clean", StringComparison.Ordinal) ||
            asset.LifecycleState != ContentLifecycleState.Draft)
        {
            return ConflictProblem("content_not_clean", "Only clean draft content can be approved.");
        }

        var actorId = CurrentUserId();
        var nowUtc = timeProvider.GetUtcNow();
        asset.Approve(nowUtc);
        version.Approve(actorId, nowUtc);
        dbContext.AuditEvents.Add(CreateAudit(
            tenantId,
            actorId,
            "content.approved",
            asset.Id,
            "success",
            null,
            new { versionId },
            nowUtc));
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(asset, version));
    }

    [HttpPost("{contentId:guid}/archive")]
    [Authorize(Policy = AuthorizationPolicies.TenantContentManager)]
    public async Task<IActionResult> Archive(
        Guid tenantId,
        Guid contentId,
        ArchiveContentRequest request,
        CancellationToken cancellationToken)
    {
        var asset = await dbContext.ContentAssets.SingleOrDefaultAsync(
            value => value.Id == contentId,
            cancellationToken);
        if (asset is null)
        {
            return NotFound();
        }

        if (asset.ConcurrencyToken != request.ConcurrencyToken)
        {
            return ConflictProblem("concurrency_conflict", "The content changed. Refresh it before retrying.");
        }

        var actorId = CurrentUserId();
        var nowUtc = timeProvider.GetUtcNow();
        asset.Archive(nowUtc);
        dbContext.AuditEvents.Add(CreateAudit(
            tenantId,
            actorId,
            "content.archived",
            asset.Id,
            "success",
            request.Reason,
            new { },
            nowUtc));
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private TenantAuditEvent CreateAudit(
        Guid tenantId,
        Guid actorId,
        string action,
        Guid targetId,
        string outcome,
        string? reasonCode,
        object details,
        DateTimeOffset occurredAtUtc) => new(
            Guid.NewGuid(),
            tenantId,
            "user",
            actorId,
            action,
            "content",
            targetId,
            outcome,
            reasonCode,
            HttpContext.TraceIdentifierGuid(),
            JsonSerializer.Serialize(details),
            occurredAtUtc);

    private Guid CurrentUserId() => Guid.TryParse(
        User.FindFirstValue(ClaimTypes.NameIdentifier),
        out var userId)
        ? userId
        : throw new InvalidOperationException("Authenticated principal has no valid user identifier.");

    private static ContentResponse ToResponse(ContentAsset asset, ContentVersion? version) => new(
        asset.Id,
        asset.Title,
        asset.MediaKind,
        asset.LifecycleState,
        asset.CreatedAtUtc,
        asset.UpdatedAtUtc,
        asset.ConcurrencyToken,
        version is null
            ? null
            : new ContentVersionResponse(
                version.Id,
                version.VersionNumber,
                version.ByteLength,
                Convert.ToHexString(version.Sha256).ToLowerInvariant(),
                version.DetectedMimeType,
                version.OriginalDisplayFileName,
                version.ScanState,
                version.RejectionCode,
                version.ApprovedAtUtc));

    private static string NormalizeDisplayFileName(string value)
    {
        var leafName = Path.GetFileName((value ?? string.Empty).Replace('\\', '/')).Trim();
        var sanitized = new string(leafName.Where(character => !char.IsControl(character)).ToArray());
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "content";
        }

        return sanitized.Length <= 255 ? sanitized : sanitized[..255];
    }

    private static ObjectResult ValidationProblemResponse(string code, string title) => new(new ProblemDetails
    {
        Type = "https://docs.example.invalid/problems/content-validation",
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

public sealed class ContentUploadRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public string Title { get; init; } = string.Empty;

    [Required]
    public IFormFile File { get; init; } = null!;
}

public sealed record ApproveContentRequest(Guid ConcurrencyToken);

public sealed record ArchiveContentRequest(
    Guid ConcurrencyToken,
    [param: Required, StringLength(64, MinimumLength = 3)] string Reason);

public sealed record ContentResponse(
    Guid Id,
    string Title,
    MediaKind MediaKind,
    ContentLifecycleState LifecycleState,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    Guid ConcurrencyToken,
    ContentVersionResponse? LatestVersion);

public sealed record ContentVersionResponse(
    Guid Id,
    int VersionNumber,
    long ByteLength,
    string Sha256,
    string DetectedMimeType,
    string OriginalDisplayFileName,
    string ScanState,
    string? RejectionCode,
    DateTimeOffset? ApprovedAtUtc);
