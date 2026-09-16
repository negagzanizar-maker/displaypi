using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using DisplayControl.Api.Pagination;
using DisplayControl.Api.Realtime;
using DisplayControl.Api.Security;
using DisplayControl.Domain.Devices;
using DisplayControl.Domain.Licensing;
using DisplayControl.Domain.Operations;
using DisplayControl.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DisplayControl.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId:guid}/licenses")]
public sealed class LicensesController(
    DisplayControlDbContext dbContext,
    DeviceStateChangeNotifications notifications,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.TenantViewer)]
    public async Task<ActionResult<IReadOnlyList<LicenseResponse>>> List(
        Guid tenantId,
        [FromQuery, Range(1, 100)] int limit = 50,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        if (!CursorPage.TryReadOffset(cursor, out var offset)) return BadRequest("Invalid pagination cursor.");
        var rows = await dbContext.Licenses.AsNoTracking()
            .OrderByDescending(value => value.ExpiresAtUtc)
            .ThenBy(value => value.Id)
            .Skip(offset)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        CursorPage.WriteNext(Response, offset, limit, rows.Count);
        var nowUtc = timeProvider.GetUtcNow();
        return Ok(rows.Take(limit).Select(value => ToResponse(value, nowUtc)).ToArray());
    }

    [HttpGet("{licenseId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.TenantViewer)]
    public async Task<ActionResult<LicenseDetailResponse>> Get(
        Guid tenantId,
        Guid licenseId,
        CancellationToken cancellationToken)
    {
        var license = await dbContext.Licenses.AsNoTracking().SingleOrDefaultAsync(
            value => value.Id == licenseId,
            cancellationToken);
        if (license is null)
        {
            return NotFound();
        }

        var history = await dbContext.LicenseEvents.AsNoTracking()
            .Where(value => value.LicenseId == licenseId)
            .OrderByDescending(value => value.OccurredAtUtc)
            .Select(value => new LicenseEventResponse(
                value.Id,
                value.EventType,
                value.ActorType,
                value.ActorId,
                value.Reason,
                value.OccurredAtUtc))
            .ToListAsync(cancellationToken);
        return Ok(new LicenseDetailResponse(ToResponse(license, timeProvider.GetUtcNow()), history));
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.TenantAdministrator)]
    public async Task<ActionResult<LicenseResponse>> Create(
        Guid tenantId,
        CreateLicenseRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsUtcRange(request.ValidFromUtc, request.ExpiresAtUtc))
        {
            return InvalidWindow();
        }

        await MutationLocks.DeviceAsync(dbContext, tenantId, request.DeviceId, cancellationToken);
        var device = await dbContext.Devices.AsNoTracking().SingleOrDefaultAsync(
            value => value.Id == request.DeviceId,
            cancellationToken);
        if (device is null)
        {
            return NotFound();
        }

        if (device.State != DeviceLifecycleState.Active)
        {
            return ConflictProblem("device_not_active", "A licence can be created only for an active enrolled device.");
        }

        var overlaps = await dbContext.Licenses.AnyAsync(
            value => value.DeviceId == request.DeviceId &&
                value.ControlState != LicenseControlState.Revoked &&
                request.ValidFromUtc < value.ExpiresAtUtc &&
                request.ExpiresAtUtc > value.ValidFromUtc,
            cancellationToken);
        if (overlaps)
        {
            return ConflictProblem("license_window_overlap", "The device already has an overlapping licence interval.");
        }

        var nowUtc = timeProvider.GetUtcNow();
        var actorId = CurrentUserId();
        var license = new DeviceLicense(
            Guid.NewGuid(),
            tenantId,
            request.DeviceId,
            request.ValidFromUtc,
            request.ExpiresAtUtc,
            nowUtc);
        dbContext.Licenses.Add(license);
        AddHistory(license, actorId, "Created", request.Reason, "{}", Snapshot(license), nowUtc);
        AddAudit(tenantId, actorId, "license.created", license.Id, request.Reason, nowUtc);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return ConflictProblem("license_conflict", "The licence conflicts with another operation.");
        }

        notifications.Enqueue(tenantId, request.DeviceId);
        return Created(
            $"/api/v1/tenants/{tenantId}/licenses/{license.Id}",
            ToResponse(license, nowUtc));
    }

    [HttpPost("{licenseId:guid}/renew")]
    [Authorize(Policy = AuthorizationPolicies.TenantAdministrator)]
    public Task<IActionResult> Renew(
        Guid tenantId,
        Guid licenseId,
        RenewLicenseRequest request,
        CancellationToken cancellationToken) => MutateAsync(
            tenantId,
            licenseId,
            request.ConcurrencyToken,
            request.Reason,
            "Renewed",
            "license.renewed",
            (license, nowUtc) => license.Renew(request.ExpiresAtUtc, nowUtc),
            cancellationToken);

    [HttpPost("{licenseId:guid}/suspend")]
    [Authorize(Policy = AuthorizationPolicies.TenantAdministrator)]
    public Task<IActionResult> Suspend(
        Guid tenantId,
        Guid licenseId,
        LicenseStateChangeRequest request,
        CancellationToken cancellationToken) => MutateAsync(
            tenantId,
            licenseId,
            request.ConcurrencyToken,
            request.Reason,
            "Suspended",
            "license.suspended",
            static (license, nowUtc) => license.Suspend(nowUtc),
            cancellationToken);

    [HttpPost("{licenseId:guid}/reactivate")]
    [Authorize(Policy = AuthorizationPolicies.TenantAdministrator)]
    public Task<IActionResult> Reactivate(
        Guid tenantId,
        Guid licenseId,
        LicenseStateChangeRequest request,
        CancellationToken cancellationToken) => MutateAsync(
            tenantId,
            licenseId,
            request.ConcurrencyToken,
            request.Reason,
            "Reactivated",
            "license.reactivated",
            static (license, nowUtc) => license.Reactivate(nowUtc),
            cancellationToken);

    [HttpPost("{licenseId:guid}/revoke")]
    [Authorize(Policy = AuthorizationPolicies.TenantAdministrator)]
    public Task<IActionResult> Revoke(
        Guid tenantId,
        Guid licenseId,
        LicenseStateChangeRequest request,
        CancellationToken cancellationToken) => MutateAsync(
            tenantId,
            licenseId,
            request.ConcurrencyToken,
            request.Reason,
            "Revoked",
            "license.revoked",
            static (license, nowUtc) => license.Revoke(nowUtc),
            cancellationToken);

    [HttpPost("{licenseId:guid}/transfer")]
    [Authorize(Policy = AuthorizationPolicies.TenantAdministratorRecentMfa)]
    public async Task<ActionResult<LicenseTransferResponse>> Transfer(
        Guid tenantId,
        Guid licenseId,
        TransferLicenseRequest request,
        CancellationToken cancellationToken)
    {
        var sourceDeviceId = await dbContext.Licenses.AsNoTracking()
            .Where(value => value.Id == licenseId).Select(value => (Guid?)value.DeviceId)
            .SingleOrDefaultAsync(cancellationToken);
        if (sourceDeviceId is null) return NotFound();
        foreach (var deviceId in new[] { sourceDeviceId.Value, request.DestinationDeviceId }.Distinct().Order())
            await MutationLocks.DeviceAsync(dbContext, tenantId, deviceId, cancellationToken);
        var source = await dbContext.Licenses.SingleOrDefaultAsync(
            value => value.Id == licenseId,
            cancellationToken);
        if (source is null)
        {
            return NotFound();
        }

        if (source.ConcurrencyToken != request.ConcurrencyToken)
        {
            return ConflictProblem("concurrency_conflict", "The licence changed. Refresh it before retrying.");
        }

        var destination = await dbContext.Devices.AsNoTracking().SingleOrDefaultAsync(
            value => value.Id == request.DestinationDeviceId,
            cancellationToken);
        if (destination is null)
        {
            return NotFound();
        }

        if (destination.State != DeviceLifecycleState.Active)
        {
            return ConflictProblem("destination_device_not_active", "The transfer destination must be active.");
        }

        var destinationHasLicense = await dbContext.Licenses.AnyAsync(
            value => value.DeviceId == request.DestinationDeviceId &&
                value.ControlState != LicenseControlState.Revoked &&
                value.ExpiresAtUtc > timeProvider.GetUtcNow(),
            cancellationToken);
        if (destinationHasLicense)
        {
            return ConflictProblem("destination_already_licensed", "The destination already has a current or future licence.");
        }

        var actorId = CurrentUserId();
        var nowUtc = timeProvider.GetUtcNow();
        var before = Snapshot(source);
        DateTimeOffset destinationValidFromUtc;
        try
        {
            destinationValidFromUtc = source.BeginTransfer(request.DestinationDeviceId, nowUtc);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return ConflictProblem("license_transfer_invalid", exception.Message);
        }

        var replacement = new DeviceLicense(
            Guid.NewGuid(),
            tenantId,
            request.DestinationDeviceId,
            destinationValidFromUtc,
            source.ExpiresAtUtc,
            nowUtc);
        dbContext.Licenses.Add(replacement);
        AddHistory(source, actorId, "TransferStarted", request.Reason, before, Snapshot(source), nowUtc);
        AddHistory(replacement, actorId, "TransferDestinationCreated", request.Reason, "{}", Snapshot(replacement), nowUtc);
        AddAudit(tenantId, actorId, "license.transfer_started", source.Id, request.Reason, nowUtc);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConflictProblem("concurrency_conflict", "The licence changed. Refresh it before retrying.");
        }

        notifications.Enqueue(tenantId, [source.DeviceId, replacement.DeviceId]);
        return Ok(new LicenseTransferResponse(
            ToResponse(source, nowUtc),
            ToResponse(replacement, nowUtc),
            destinationValidFromUtc));
    }

    private async Task<IActionResult> MutateAsync(
        Guid tenantId,
        Guid licenseId,
        Guid concurrencyToken,
        string reason,
        string eventType,
        string auditAction,
        Action<DeviceLicense, DateTimeOffset> mutation,
        CancellationToken cancellationToken)
    {
        var deviceId = await dbContext.Licenses.AsNoTracking()
            .Where(value => value.Id == licenseId).Select(value => (Guid?)value.DeviceId)
            .SingleOrDefaultAsync(cancellationToken);
        if (deviceId is null) return NotFound();
        await MutationLocks.DeviceAsync(dbContext, tenantId, deviceId.Value, cancellationToken);
        var license = await dbContext.Licenses.SingleOrDefaultAsync(value => value.Id == licenseId, cancellationToken);
        if (license is null)
        {
            return NotFound();
        }

        if (license.ConcurrencyToken != concurrencyToken)
        {
            return ConflictProblem("concurrency_conflict", "The licence changed. Refresh it before retrying.");
        }

        var before = Snapshot(license);
        var nowUtc = timeProvider.GetUtcNow();
        try
        {
            mutation(license, nowUtc);
        }
        catch (ArgumentException)
        {
            return InvalidWindow();
        }
        catch (InvalidOperationException exception)
        {
            return ConflictProblem("invalid_license_state", exception.Message);
        }

        if (license.ControlState != LicenseControlState.Revoked && await dbContext.Licenses.AnyAsync(
            value => value.Id != license.Id && value.DeviceId == license.DeviceId &&
                value.ControlState != LicenseControlState.Revoked &&
                license.ValidFromUtc < value.ExpiresAtUtc && license.ExpiresAtUtc > value.ValidFromUtc,
            cancellationToken))
        {
            await dbContext.Entry(license).ReloadAsync(cancellationToken);
            return ConflictProblem("license_window_overlap", "The device already has an overlapping licence interval.");
        }

        var actorId = CurrentUserId();
        AddHistory(license, actorId, eventType, reason, before, Snapshot(license), nowUtc);
        AddAudit(tenantId, actorId, auditAction, license.Id, reason, nowUtc);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            notifications.Enqueue(tenantId, license.DeviceId);
            return NoContent();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConflictProblem("concurrency_conflict", "The licence changed. Refresh it before retrying.");
        }
    }

    private void AddHistory(
        DeviceLicense license,
        Guid actorId,
        string eventType,
        string reason,
        string beforeJson,
        string afterJson,
        DateTimeOffset nowUtc) => dbContext.LicenseEvents.Add(new LicenseEvent(
            Guid.NewGuid(),
            license.TenantId,
            license.Id,
            eventType,
            actorId,
            reason,
            beforeJson,
            afterJson,
            nowUtc));

    private void AddAudit(
        Guid tenantId,
        Guid actorId,
        string action,
        Guid licenseId,
        string reason,
        DateTimeOffset nowUtc) => dbContext.AuditEvents.Add(new TenantAuditEvent(
            Guid.NewGuid(),
            tenantId,
            "HumanUser",
            actorId,
            action,
            "License",
            licenseId,
            "Succeeded",
            null,
            HttpContext.TraceIdentifierGuid(),
            JsonSerializer.Serialize(new { reason }),
            nowUtc));

    private Guid CurrentUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
        ? userId
        : throw new InvalidOperationException("Authenticated principal has no valid user identifier.");

    private static bool IsUtcRange(DateTimeOffset start, DateTimeOffset end) =>
        start.Offset == TimeSpan.Zero && end.Offset == TimeSpan.Zero && end > start;

    private static string Snapshot(DeviceLicense value) => JsonSerializer.Serialize(new
    {
        value.DeviceId,
        value.ValidFromUtc,
        value.ExpiresAtUtc,
        value.ControlState,
        value.TransferDestinationDeviceId
    });

    private static LicenseResponse ToResponse(DeviceLicense value, DateTimeOffset nowUtc) => new(
        value.Id,
        value.DeviceId,
        value.ValidFromUtc,
        value.ExpiresAtUtc,
        value.ControlState,
        value.EvaluateAt(nowUtc),
        value.LatestIssuedLeaseExpiryUtc,
        value.TransferDestinationDeviceId,
        value.ConcurrencyToken);

    private static BadRequestObjectResult InvalidWindow() => new(new ValidationProblemDetails(
        new Dictionary<string, string[]>
        {
            ["validity"] = ["Licence timestamps must be UTC and expiry must be after start/current expiry."]
        })
    {
        Type = "https://docs.example.invalid/problems/validation",
        Title = "The licence interval is invalid.",
        Status = StatusCodes.Status400BadRequest,
        Extensions = { ["code"] = "invalid_license_window" }
    });

    private static ObjectResult ConflictProblem(string code, string title) => new(new ProblemDetails
    {
        Type = "https://docs.example.invalid/problems/license-state",
        Title = title,
        Status = StatusCodes.Status409Conflict,
        Extensions = { ["code"] = code }
    })
    {
        StatusCode = StatusCodes.Status409Conflict
    };
}

public sealed record CreateLicenseRequest(
    Guid DeviceId,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset ExpiresAtUtc,
    [param: Required, StringLength(1000, MinimumLength = 3)] string Reason);

public sealed record RenewLicenseRequest(
    DateTimeOffset ExpiresAtUtc,
    Guid ConcurrencyToken,
    [param: Required, StringLength(1000, MinimumLength = 3)] string Reason);

public sealed record LicenseStateChangeRequest(
    Guid ConcurrencyToken,
    [param: Required, StringLength(1000, MinimumLength = 3)] string Reason);

public sealed record TransferLicenseRequest(
    Guid DestinationDeviceId,
    Guid ConcurrencyToken,
    [param: Required, StringLength(1000, MinimumLength = 3)] string Reason);

public sealed record LicenseTransferResponse(
    LicenseResponse Source,
    LicenseResponse Destination,
    DateTimeOffset DestinationActivatesAtUtc);

public sealed record LicenseResponse(
    Guid Id,
    Guid DeviceId,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset ExpiresAtUtc,
    LicenseControlState ControlState,
    LicenseEffectiveState EffectiveState,
    DateTimeOffset? LatestIssuedLeaseExpiryUtc,
    Guid? TransferDestinationDeviceId,
    Guid ConcurrencyToken);

public sealed record LicenseEventResponse(
    Guid Id,
    string EventType,
    string ActorType,
    Guid? ActorId,
    string Reason,
    DateTimeOffset OccurredAtUtc);

public sealed record LicenseDetailResponse(
    LicenseResponse License,
    IReadOnlyList<LicenseEventResponse> History);
