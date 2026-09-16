using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using DisplayControl.Api.Pagination;
using DisplayControl.Api.Security;
using DisplayControl.Application.Content;
using DisplayControl.Application.Security;
using DisplayControl.Domain.Content;
using DisplayControl.Domain.Devices;
using DisplayControl.Domain.Licensing;
using DisplayControl.Domain.Operations;
using DisplayControl.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DisplayControl.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId:guid}")]
public sealed class DevicesController(
    DisplayControlDbContext dbContext,
    ITenantCapabilityTokenService capabilityTokenService,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("devices")]
    [Authorize(Policy = AuthorizationPolicies.TenantViewer)]
    public async Task<ActionResult<IReadOnlyList<DeviceSummaryResponse>>> List(
        Guid tenantId,
        [FromQuery, Range(1, 100)] int limit = 50,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        if (!CursorPage.TryReadOffset(cursor, out var offset)) return BadRequest("Invalid cursor.");
        var devices = await dbContext.Devices.AsNoTracking()
            .OrderBy(value => value.DisplayName)
            .ThenBy(value => value.Id)
            .Skip(offset)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        CursorPage.WriteNext(Response, offset, limit, devices.Count);
        devices = devices.Take(limit).ToList();
        var deviceIds = devices.Select(value => value.Id).ToArray();
        var licenses = await dbContext.Licenses.AsNoTracking()
            .Where(value => deviceIds.Contains(value.DeviceId))
            .ToListAsync(cancellationToken);
        var nowUtc = timeProvider.GetUtcNow();
        var latestLicenses = licenses.GroupBy(value => value.DeviceId)
            .ToDictionary(group => group.Key, group => SelectLicenseForDisplay(group, nowUtc)!);
        var networkInterfaces = await dbContext.DeviceNetworkInterfaces.AsNoTracking()
            .Where(value => deviceIds.Contains(value.DeviceId))
            .OrderBy(value => value.DeviceId)
            .ThenBy(value => value.InterfaceName)
            .ToListAsync(cancellationToken);
        var networksByDevice = networkInterfaces
            .GroupBy(value => value.DeviceId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<DeviceNetworkResponse>)group.Select(ToNetworkResponse).ToArray());
        var latestHeartbeatTimes = dbContext.DeviceHeartbeats.AsNoTracking()
            .Where(value => deviceIds.Contains(value.DeviceId))
            .GroupBy(value => value.DeviceId)
            .Select(group => new { DeviceId = group.Key, ReceivedAtUtc = group.Max(value => value.ReceivedAtUtc) });
        var latestHeartbeatRows = await (
            from heartbeat in dbContext.DeviceHeartbeats.AsNoTracking()
            join latest in latestHeartbeatTimes
                on new { heartbeat.DeviceId, heartbeat.ReceivedAtUtc }
                equals new { latest.DeviceId, latest.ReceivedAtUtc }
            select new LatestPlaybackHeartbeat(
                heartbeat.DeviceId,
                heartbeat.Sequence,
                heartbeat.ReceivedAtUtc,
                heartbeat.AppliedDesiredStateVersion,
                heartbeat.PlayerStateCode,
                heartbeat.LastErrorCode,
                heartbeat.FreeDiskBytes,
                heartbeat.ServerObservedIp,
                heartbeat.InventoryJson))
            .ToListAsync(cancellationToken);
        var latestHeartbeats = latestHeartbeatRows
            .GroupBy(value => value.DeviceId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(value => value.Sequence).First());
        var currentContentVersionIds = latestHeartbeats.Values
            .Select(value => ReadCurrentContentVersionId(value.InventoryJson))
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .Distinct()
            .ToArray();
        var currentContentRows = await (
            from version in dbContext.ContentVersions.AsNoTracking()
            join content in dbContext.ContentAssets.AsNoTracking()
                on version.ContentAssetId equals content.Id
            where currentContentVersionIds.Contains(version.Id)
            select new CurrentContentDetails(
                version.Id,
                content.Id,
                content.Title,
                content.MediaKind))
            .ToDictionaryAsync(value => value.ContentVersionId, cancellationToken);
        var playbackRows = await (
            from desiredState in dbContext.DesiredStates.AsNoTracking()
            join asset in dbContext.DesiredStateAssets.AsNoTracking()
                on desiredState.Id equals asset.DesiredStateId
            where deviceIds.Contains(desiredState.DeviceId) && currentContentVersionIds.Contains(asset.ContentVersionId)
            select new CurrentPlaybackDetails(
                desiredState.DeviceId,
                desiredState.Version,
                asset.ContentVersionId,
                asset.PlaybackJson))
            .ToListAsync(cancellationToken);
        var currentPlaybackRows = playbackRows
            .Where(value => latestHeartbeats.TryGetValue(value.DeviceId, out var heartbeat) &&
                heartbeat.AppliedDesiredStateVersion == value.DesiredStateVersion)
            .GroupBy(value => value.DeviceId)
            .ToDictionary(group => group.Key, group => group.First());
        return Ok(devices.Select(device => new DeviceSummaryResponse(
            device.Id,
            device.DisplayName,
            device.State,
            CalculateHealth(device.LastSeenUtc, nowUtc),
            device.LastSeenUtc,
            device.SerialNumberNormalized,
            device.Hostname,
            device.OsDescription,
            device.Architecture,
            device.AgentVersion,
            device.PlayerVersion,
            device.DiskCapacityBytes,
            latestHeartbeats.TryGetValue(device.Id, out var latestInventory) ? latestInventory.FreeDiskBytes : null,
            latestInventory?.ServerObservedIp,
            latestLicenses.TryGetValue(device.Id, out var license) ? license.EvaluateAt(nowUtc) : null,
            license?.ExpiresAtUtc,
            device.AppliedManifestVersion,
            device.PlaybackHealthCode,
            BuildPlayback(device.Id, latestHeartbeats, currentContentRows, currentPlaybackRows),
            networksByDevice.GetValueOrDefault(device.Id, []),
            device.ConcurrencyToken)).ToArray());
    }

    [HttpGet("devices/{deviceId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.TenantViewer)]
    public async Task<ActionResult<DeviceDetailResponse>> Get(
        Guid tenantId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var device = await dbContext.Devices.AsNoTracking().SingleOrDefaultAsync(
            value => value.Id == deviceId,
            cancellationToken);
        if (device is null)
        {
            return NotFound();
        }

        var networkEntities = await dbContext.DeviceNetworkInterfaces.AsNoTracking()
            .Where(value => value.DeviceId == deviceId)
            .OrderBy(value => value.InterfaceName)
            .ToListAsync(cancellationToken);
        var networks = networkEntities.Select(ToNetworkResponse).ToArray();
        var licenses = await dbContext.Licenses.AsNoTracking()
            .Where(value => value.DeviceId == deviceId)
            .ToListAsync(cancellationToken);
        var nowUtc = timeProvider.GetUtcNow();
        var license = SelectLicenseForDisplay(licenses, nowUtc);

        return Ok(new DeviceDetailResponse(
            device.Id,
            device.DisplayName,
            device.State,
            CalculateHealth(device.LastSeenUtc, nowUtc),
            device.SerialNumberNormalized,
            device.Hostname,
            device.OsDescription,
            device.Architecture,
            device.AgentVersion,
            device.PlayerVersion,
            device.DiskCapacityBytes,
            device.LastSeenUtc,
            device.AppliedManifestVersion,
            device.PlaybackHealthCode,
            license is null ? null : new DeviceLicenseSummaryResponse(
                license.Id,
                license.EvaluateAt(nowUtc),
                license.ValidFromUtc,
                license.ExpiresAtUtc),
            networks,
            device.ConcurrencyToken));
    }

    [HttpPost("enrollment-codes")]
    [Authorize(Policy = AuthorizationPolicies.TenantAdministrator)]
    public async Task<ActionResult<EnrollmentCodeCreatedResponse>> CreateEnrollmentCode(
        Guid tenantId,
        CreateEnrollmentCodeRequest request,
        CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow();
        var actorId = CurrentUserId();
        var device = new Device(Guid.NewGuid(), tenantId, request.DisplayName, nowUtc);
        var capability = capabilityTokenService.Generate(tenantId);
        var enrollment = new EnrollmentToken(
            Guid.NewGuid(),
            tenantId,
            capability.Digest,
            nowUtc.AddMinutes(request.ExpiresInMinutes),
            actorId,
            nowUtc,
            device.Id,
            request.ExpectedSerialNumber);
        dbContext.Devices.Add(device);
        dbContext.EnrollmentTokens.Add(enrollment);
        AddAudit(tenantId, actorId, "device.enrollment-code.created", "Device", device.Id, null, nowUtc);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Created(
            $"/api/v1/tenants/{tenantId}/devices/{device.Id}",
            new EnrollmentCodeCreatedResponse(
                device.Id,
                device.DisplayName,
                capability.Value,
                enrollment.ExpiresAtUtc));
    }

    [HttpPatch("devices/{deviceId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.TenantAdministrator)]
    public async Task<IActionResult> Rename(
        Guid tenantId,
        Guid deviceId,
        RenameDeviceRequest request,
        CancellationToken cancellationToken)
    {
        var device = await dbContext.Devices.SingleOrDefaultAsync(value => value.Id == deviceId, cancellationToken);
        if (device is null)
        {
            return NotFound();
        }

        if (device.ConcurrencyToken != request.ConcurrencyToken)
        {
            return ConcurrencyConflict();
        }

        var nowUtc = timeProvider.GetUtcNow();
        device.Rename(request.DisplayName, nowUtc);
        AddAudit(tenantId, CurrentUserId(), "device.renamed", "Device", deviceId, null, nowUtc);
        return await SaveMutationAsync(cancellationToken);
    }

    [HttpPost("devices/{deviceId:guid}/suspend")]
    [Authorize(Policy = AuthorizationPolicies.TenantAdministrator)]
    public Task<IActionResult> Suspend(
        Guid tenantId,
        Guid deviceId,
        DeviceStateChangeRequest request,
        CancellationToken cancellationToken) => ChangeStateAsync(
            tenantId,
            deviceId,
            request,
            "device.suspended",
            (device, nowUtc) => device.Suspend(nowUtc),
            cancellationToken);

    [HttpPost("devices/{deviceId:guid}/reactivate")]
    [Authorize(Policy = AuthorizationPolicies.TenantAdministrator)]
    public Task<IActionResult> Reactivate(
        Guid tenantId,
        Guid deviceId,
        DeviceStateChangeRequest request,
        CancellationToken cancellationToken) => ChangeStateAsync(
            tenantId,
            deviceId,
            request,
            "device.reactivated",
            (device, nowUtc) => device.Reactivate(nowUtc),
            cancellationToken);

    [HttpPost("devices/{deviceId:guid}/retire")]
    [Authorize(Policy = AuthorizationPolicies.TenantAdministrator)]
    public async Task<IActionResult> Retire(
        Guid tenantId,
        Guid deviceId,
        DeviceStateChangeRequest request,
        CancellationToken cancellationToken)
    {
        var device = await dbContext.Devices.SingleOrDefaultAsync(value => value.Id == deviceId, cancellationToken);
        if (device is null)
        {
            return NotFound();
        }

        if (device.ConcurrencyToken != request.ConcurrencyToken)
        {
            return ConcurrencyConflict();
        }

        var nowUtc = timeProvider.GetUtcNow();
        device.Retire(nowUtc);
        var certificates = await dbContext.DeviceCertificates
            .Where(value => value.DeviceId == deviceId && value.State == DeviceCertificateState.Active)
            .ToListAsync(cancellationToken);
        foreach (var certificate in certificates)
        {
            certificate.Revoke("device_retired", nowUtc);
        }

        var licenses = await dbContext.Licenses
            .Where(value => value.DeviceId == deviceId && value.ControlState != LicenseControlState.Revoked)
            .ToListAsync(cancellationToken);
        foreach (var license in licenses)
        {
            license.Revoke(nowUtc);
        }

        AddAudit(tenantId, CurrentUserId(), "device.retired", "Device", deviceId, request.Reason, nowUtc);
        return await SaveMutationAsync(cancellationToken);
    }

    private async Task<IActionResult> ChangeStateAsync(
        Guid tenantId,
        Guid deviceId,
        DeviceStateChangeRequest request,
        string action,
        Action<Device, DateTimeOffset> change,
        CancellationToken cancellationToken)
    {
        var device = await dbContext.Devices.SingleOrDefaultAsync(value => value.Id == deviceId, cancellationToken);
        if (device is null)
        {
            return NotFound();
        }

        if (device.ConcurrencyToken != request.ConcurrencyToken)
        {
            return ConcurrencyConflict();
        }

        var nowUtc = timeProvider.GetUtcNow();
        try
        {
            change(device, nowUtc);
        }
        catch (InvalidOperationException exception)
        {
            return ConflictProblem("invalid_device_state", exception.Message);
        }

        AddAudit(tenantId, CurrentUserId(), action, "Device", deviceId, request.Reason, nowUtc);
        return await SaveMutationAsync(cancellationToken);
    }

    private void AddAudit(
        Guid tenantId,
        Guid actorId,
        string action,
        string targetType,
        Guid targetId,
        string? reason,
        DateTimeOffset nowUtc) => dbContext.AuditEvents.Add(new TenantAuditEvent(
            Guid.NewGuid(),
            tenantId,
            "HumanUser",
            actorId,
            action,
            targetType,
            targetId,
            "Succeeded",
            reason,
            HttpContext.TraceIdentifierGuid(),
            JsonSerializer.Serialize(new { reason }),
            nowUtc));

    private async Task<IActionResult> SaveMutationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return NoContent();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyConflict();
        }
    }

    private Guid CurrentUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
        ? userId
        : throw new InvalidOperationException("Authenticated principal has no valid user identifier.");

    private static DeviceNetworkResponse ToNetworkResponse(DeviceNetworkInterface value) => new(
        value.InterfaceName,
        value.MacAddressNormalized,
        JsonSerializer.Deserialize<string[]>(value.LocalAddressesJson) ?? [],
        value.ObservedAtUtc);

    private static DevicePlaybackResponse? BuildPlayback(
        Guid deviceId,
        IReadOnlyDictionary<Guid, LatestPlaybackHeartbeat> latestHeartbeats,
        IReadOnlyDictionary<Guid, CurrentContentDetails> currentContentRows,
        IReadOnlyDictionary<Guid, CurrentPlaybackDetails> currentPlaybackRows)
    {
        if (!latestHeartbeats.TryGetValue(deviceId, out var heartbeat)) return null;
        var contentVersionId = ReadCurrentContentVersionId(heartbeat.InventoryJson);
        currentContentRows.TryGetValue(contentVersionId ?? Guid.Empty, out var content);
        currentPlaybackRows.TryGetValue(deviceId, out var playback);
        PlaylistItemPresentation.TryRead(playback?.PlaybackJson ?? "{}", out _, out var captionText);
        return new DevicePlaybackResponse(
            heartbeat.PlayerStateCode,
            contentVersionId,
            content?.ContentId,
            content?.Title,
            content?.MediaKind,
            heartbeat.AppliedDesiredStateVersion,
            heartbeat.ReceivedAtUtc,
            heartbeat.LastErrorCode,
            captionText);
    }

    private static Guid? ReadCurrentContentVersionId(string inventoryJson)
    {
        try
        {
            using var document = JsonDocument.Parse(inventoryJson);
            return document.RootElement.TryGetProperty("CurrentContentVersionId", out var value) ||
                document.RootElement.TryGetProperty("currentContentVersionId", out value)
                ? value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var id) && id != Guid.Empty
                    ? id
                    : null
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string CalculateHealth(DateTimeOffset? lastSeenUtc, DateTimeOffset nowUtc) => lastSeenUtc switch
    {
        null => "offline",
        _ when nowUtc - lastSeenUtc <= TimeSpan.FromMinutes(2) => "online",
        _ when nowUtc - lastSeenUtc <= TimeSpan.FromMinutes(10) => "degraded",
        _ => "offline"
    };

    private static DeviceLicense? SelectLicenseForDisplay(
        IEnumerable<DeviceLicense> licenses,
        DateTimeOffset nowUtc) => licenses
            .OrderByDescending(value => value.EvaluateAt(nowUtc) == LicenseEffectiveState.Active)
            .ThenByDescending(value => value.UpdatedAtUtc)
            .ThenByDescending(value => value.CreatedAtUtc)
            .ThenByDescending(value => value.ExpiresAtUtc)
            .FirstOrDefault();

    private static ObjectResult ConcurrencyConflict() => ConflictProblem(
        "concurrency_conflict",
        "The resource changed. Refresh it before retrying.");

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

    private sealed record LatestPlaybackHeartbeat(
        Guid DeviceId,
        long Sequence,
        DateTimeOffset ReceivedAtUtc,
        long? AppliedDesiredStateVersion,
        string PlayerStateCode,
        string? LastErrorCode,
        long? FreeDiskBytes,
        string? ServerObservedIp,
        string InventoryJson);

    private sealed record CurrentContentDetails(
        Guid ContentVersionId,
        Guid ContentId,
        string Title,
        MediaKind MediaKind);

    private sealed record CurrentPlaybackDetails(
        Guid DeviceId,
        long DesiredStateVersion,
        Guid ContentVersionId,
        string PlaybackJson);
}

internal static class HttpContextAuditExtensions
{
    public static Guid TraceIdentifierGuid(this HttpContext context)
    {
        if (Guid.TryParse(context.TraceIdentifier, out var correlationId))
        {
            return correlationId;
        }

        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(context.TraceIdentifier));
        return new Guid(bytes.AsSpan(0, 16));
    }
}

public sealed record CreateEnrollmentCodeRequest(
    [param: Required, StringLength(160, MinimumLength = 1)] string DisplayName,
    [param: Range(5, 60)] int ExpiresInMinutes = 15,
    [param: StringLength(32, MinimumLength = 1)] string? ExpectedSerialNumber = null);

public sealed record RenameDeviceRequest(
    [param: Required, StringLength(160, MinimumLength = 1)] string DisplayName,
    Guid ConcurrencyToken);

public sealed record DeviceStateChangeRequest(
    Guid ConcurrencyToken,
    [param: Required, StringLength(64, MinimumLength = 3)] string Reason);

public sealed record EnrollmentCodeCreatedResponse(
    Guid DeviceId,
    string DisplayName,
    string EnrollmentCode,
    DateTimeOffset ExpiresAtUtc);

public sealed record DeviceSummaryResponse(
    Guid Id,
    string DisplayName,
    DeviceLifecycleState State,
    string Health,
    DateTimeOffset? LastSeenUtc,
    string? SerialNumber,
    string? Hostname,
    string? OsDescription,
    string? Architecture,
    string? AgentVersion,
    string? PlayerVersion,
    long? DiskCapacityBytes,
    long? FreeDiskBytes,
    string? ServerObservedIp,
    LicenseEffectiveState? LicenseState,
    DateTimeOffset? LicenseExpiresAtUtc,
    long? AppliedManifestVersion,
    string? PlaybackHealthCode,
    DevicePlaybackResponse? Playback,
    IReadOnlyList<DeviceNetworkResponse> NetworkInterfaces,
    Guid ConcurrencyToken);

public sealed record DevicePlaybackResponse(
    string PlayerState,
    Guid? ContentVersionId,
    Guid? ContentId,
    string? Title,
    MediaKind? MediaKind,
    long? DesiredStateVersion,
    DateTimeOffset ReportedAtUtc,
    string? ErrorCode,
    string? CaptionText);

public sealed record DeviceDetailResponse(
    Guid Id,
    string DisplayName,
    DeviceLifecycleState State,
    string Health,
    string? SerialNumber,
    string? Hostname,
    string? OsDescription,
    string? Architecture,
    string? AgentVersion,
    string? PlayerVersion,
    long? DiskCapacityBytes,
    DateTimeOffset? LastSeenUtc,
    long? AppliedManifestVersion,
    string? PlaybackHealthCode,
    DeviceLicenseSummaryResponse? License,
    IReadOnlyList<DeviceNetworkResponse> NetworkInterfaces,
    Guid ConcurrencyToken);

public sealed record DeviceLicenseSummaryResponse(
    Guid Id,
    LicenseEffectiveState State,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset ExpiresAtUtc);

public sealed record DeviceNetworkResponse(
    string InterfaceName,
    string? MacAddress,
    IReadOnlyList<string> LocalAddresses,
    DateTimeOffset ObservedAtUtc);
