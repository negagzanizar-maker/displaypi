using System.Security.Claims;
using System.Security.Cryptography;
using DisplayControl.Api.Scheduling;
using DisplayControl.Api.Security;
using DisplayControl.Application.Content;
using DisplayControl.Application.Security;
using DisplayControl.Application.Storage;
using DisplayControl.Domain.Content;
using DisplayControl.Domain.Licensing;
using DisplayControl.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DisplayControl.Api.Controllers;

[ApiController]
[Route("device/v1/desired-states/{desiredStateId:guid}")]
public sealed class DeviceContentController(
    DisplayControlDbContext dbContext,
    DesiredStateResolver desiredStateResolver,
    IPrivateObjectStore objectStore,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("manifest")]
    [Authorize(Policy = AuthorizationPolicies.DeviceAuthenticated)]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Manifest(
        Guid desiredStateId,
        CancellationToken cancellationToken)
    {
        var deviceId = RequiredClaimGuid(DeviceClaimTypes.DeviceId);
        var desiredState = await desiredStateResolver.ResolveAsync(
            deviceId,
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (desiredState?.Id != desiredStateId)
        {
            return NotFound();
        }

        if (!await HasActiveLicenseAsync(deviceId, cancellationToken))
        {
            return LicenseRequired();
        }

        var assets = await dbContext.DesiredStateAssets.AsNoTracking()
            .Where(value => value.DesiredStateId == desiredStateId)
            .OrderBy(value => value.Position)
            .Select(value => new DesiredStateManifestAsset(
                value.ContentVersionId,
                value.Position,
                value.MediaKind,
                value.ByteLength,
                value.Sha256,
                value.DurationMilliseconds,
                value.LoopVideo,
                value.PlaybackJson))
            .ToListAsync(cancellationToken);
        var payload = DesiredStateManifestCodec.SerializeCanonical(
            desiredState.Id,
            desiredState.Version,
            desiredState.StartsAtUtc,
            desiredState.EndsAtUtc,
            desiredState.PublishedAtUtc,
            assets);
        var digest = SHA256.HashData(payload);
        if (!CryptographicOperations.FixedTimeEquals(digest, desiredState.ManifestSha256))
        {
            throw new InvalidOperationException("Stored desired-state manifest integrity check failed.");
        }

        Response.Headers.ETag = $"\"{Convert.ToHexString(digest).ToLowerInvariant()}\"";
        return File(payload, "application/json; charset=utf-8");
    }

    [HttpGet("assets/{contentVersionId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.DeviceAuthenticated)]
    [IgnoreAntiforgeryToken]
    [SkipTenantTransaction]
    public async Task<IActionResult> Asset(
        Guid desiredStateId,
        Guid contentVersionId,
        CancellationToken cancellationToken)
    {
        var tenantId = RequiredClaimGuid(DeviceClaimTypes.TenantId);
        var deviceId = RequiredClaimGuid(DeviceClaimTypes.DeviceId);
        DesiredStateAssetProjection? asset;
        await using (var transaction = await dbContext.BeginTenantTransactionAsync(tenantId, cancellationToken))
        {
            var currentState = await desiredStateResolver.ResolveAsync(
                deviceId,
                timeProvider.GetUtcNow(),
                cancellationToken);
            if (currentState?.Id != desiredStateId)
            {
                return NotFound();
            }

            if (!await HasActiveLicenseAsync(deviceId, cancellationToken))
            {
                return LicenseRequired();
            }

            asset = await (
                from snapshot in dbContext.DesiredStateAssets.AsNoTracking()
                join version in dbContext.ContentVersions.AsNoTracking()
                    on snapshot.ContentVersionId equals version.Id
                where snapshot.DesiredStateId == desiredStateId && snapshot.ContentVersionId == contentVersionId &&
                    version.ScanState == "clean" && version.ApprovedAtUtc != null
                select new DesiredStateAssetProjection(
                    snapshot.MediaKind,
                    snapshot.ByteLength,
                    snapshot.Sha256,
                    version.StorageKey,
                    version.DetectedMimeType))
                .SingleOrDefaultAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        if (asset is null)
        {
            return NotFound();
        }

        var objectKey = new PrivateObjectKey(tenantId, contentVersionId);
        if (!string.Equals(objectKey.ToString(), asset.StorageKey, StringComparison.Ordinal) ||
            !await objectStore.ExistsAsync(objectKey, cancellationToken))
        {
            return NotFound();
        }

        var content = await objectStore.OpenReadAsync(objectKey, cancellationToken);
        Response.Headers.ETag = $"\"{Convert.ToHexString(asset.Sha256).ToLowerInvariant()}\"";
        Response.ContentLength = asset.ByteLength;
        return File(content, asset.DetectedMimeType, enableRangeProcessing: true);
    }

    private async Task<bool> HasActiveLicenseAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow();
        return await dbContext.Licenses.AsNoTracking().AnyAsync(
            value => value.DeviceId == deviceId && value.ControlState == LicenseControlState.Enabled &&
                value.ValidFromUtc <= nowUtc && value.ExpiresAtUtc > nowUtc,
            cancellationToken);
    }

    private Guid RequiredClaimGuid(string claimType) => Guid.TryParse(User.FindFirstValue(claimType), out var value)
        ? value
        : throw new InvalidOperationException("The device principal is missing a required binding claim.");

    private static ObjectResult LicenseRequired() => new(new ProblemDetails
    {
        Type = "https://docs.example.invalid/problems/device-license",
        Title = "An active licence is required.",
        Status = StatusCodes.Status403Forbidden,
        Extensions = { ["code"] = "device_not_licensed" }
    })
    {
        StatusCode = StatusCodes.Status403Forbidden
    };

    private sealed record DesiredStateAssetProjection(
        MediaKind MediaKind,
        long ByteLength,
        byte[] Sha256,
        string StorageKey,
        string DetectedMimeType);
}
