using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using DisplayControl.Api.Pagination;
using DisplayControl.Api.Realtime;
using DisplayControl.Api.Scheduling;
using DisplayControl.Api.Security;
using DisplayControl.Application.Content;
using DisplayControl.Domain.Devices;
using DisplayControl.Domain.Operations;
using DisplayControl.Domain.Scheduling;
using DisplayControl.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DisplayControl.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId:guid}/devices/{deviceId:guid}/assignments")]
public sealed class DeviceAssignmentsController(
    DisplayControlDbContext dbContext,
    DesiredStateCompilationService compilationService,
    DeviceStateChangeNotifications notifications,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.TenantViewer)]
    public async Task<ActionResult<IReadOnlyList<DeviceAssignmentResponse>>> List(
        Guid tenantId,
        Guid deviceId,
        [FromQuery, Range(1, 100)] int limit = 50,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        if (!CursorPage.TryReadOffset(cursor, out var offset)) return BadRequest("Invalid pagination cursor.");
        var assignments = await dbContext.DeviceAssignments.AsNoTracking()
            .Where(value => value.DeviceId == deviceId)
            .OrderByDescending(value => value.PublishedAtUtc)
            .ThenBy(value => value.Id)
            .Skip(offset)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        CursorPage.WriteNext(Response, offset, limit, assignments.Count);
        return Ok(assignments.Take(limit).Select(ToResponse).ToArray());
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.TenantContentManager)]
    public async Task<ActionResult<AssignmentPublishedResponse>> Publish(
        Guid tenantId,
        Guid deviceId,
        PublishDeviceAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        await MutationLocks.SchedulingAsync(dbContext, tenantId, cancellationToken);
        var device = await dbContext.Devices.AsNoTracking().SingleOrDefaultAsync(
            value => value.Id == deviceId,
            cancellationToken);
        if (device is null)
        {
            return NotFound();
        }

        if (device.State != DeviceLifecycleState.Active)
        {
            return ConflictProblem("device_not_active", "Content can be assigned only to an active enrolled device.");
        }

        IReadOnlyList<DesiredStateManifestAsset> manifestAssets;
        try
        {
            AssignmentSchedule.EnsureWindow(request.StartsAtUtc, request.EndsAtUtc);
            AssignmentSchedule.EnsureSupportedTimeZone(request.PresentationTimeZone);
            manifestAssets = await compilationService.LoadApprovedPlaylistAsync(
                request.PlaylistVersionId,
                cancellationToken);
        }
        catch (AssignmentPublicationException exception)
        {
            return exception.Code == "playlist_not_found"
                ? NotFound()
                : ConflictProblem(exception.Code, exception.Message);
        }
        catch (ArgumentException exception)
        {
            return InvalidSchedule(exception.Message);
        }

        var existingAssignments = await dbContext.DeviceAssignments.AsNoTracking()
            .Where(value => value.DeviceId == deviceId && value.IsEnabled && value.Priority == request.Priority)
            .ToListAsync(cancellationToken);
        if (existingAssignments.Any(value => AssignmentSchedule.Overlaps(
                value.StartsAtUtc,
                value.EndsAtUtc,
                request.StartsAtUtc,
                request.EndsAtUtc)))
        {
            return ConflictProblem(
                "assignment_priority_collision",
                "The requested schedule overlaps an equal-priority device assignment.");
        }

        var actorId = CurrentUserId();
        var nowUtc = TruncateToMilliseconds(timeProvider.GetUtcNow());
        var assignment = new DeviceAssignment(
            Guid.NewGuid(),
            tenantId,
            deviceId,
            request.PlaylistVersionId,
            request.Priority,
            request.StartsAtUtc,
            request.EndsAtUtc,
            request.PresentationTimeZone,
            actorId,
            nowUtc);
        dbContext.DeviceAssignments.Add(assignment);
        var desiredState = await compilationService.CompileDeviceAssignmentAsync(
            tenantId,
            deviceId,
            assignment,
            manifestAssets,
            cancellationToken);
        dbContext.AuditEvents.Add(new TenantAuditEvent(
            Guid.NewGuid(),
            tenantId,
            "user",
            actorId,
            "assignment.published",
            "device",
            deviceId,
            "success",
            null,
            HttpContext.TraceIdentifierGuid(),
            JsonSerializer.Serialize(new
            {
                assignmentId = assignment.Id,
                playlistVersionId = request.PlaylistVersionId,
                desiredStateId = desiredState.Id,
                desiredStateVersion = desiredState.Version,
                request.StartsAtUtc,
                request.EndsAtUtc,
                request.Priority,
                request.OverrideEqualPriority
            }),
            nowUtc));
        await dbContext.SaveChangesAsync(cancellationToken);
        notifications.Enqueue(tenantId, deviceId);

        return CreatedAtAction(
            nameof(List),
            new { tenantId, deviceId },
            new AssignmentPublishedResponse(
                ToResponse(assignment),
                desiredState.Id,
                desiredState.Version,
                Convert.ToHexString(desiredState.ManifestSha256).ToLowerInvariant()));
    }

    private Guid CurrentUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
        ? userId
        : throw new InvalidOperationException("Authenticated principal has no valid user identifier.");

    private static DateTimeOffset TruncateToMilliseconds(DateTimeOffset value) =>
        value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMillisecond));

    internal static DeviceAssignmentResponse ToResponse(DeviceAssignment value) => new(
        value.Id,
        value.DeviceId,
        value.PlaylistVersionId,
        value.IsEnabled,
        value.Priority,
        value.StartsAtUtc,
        value.EndsAtUtc,
        value.PresentationTimeZone,
        value.PublishedAtUtc,
        value.ConcurrencyToken);

    internal static ObjectResult ConflictProblem(string code, string title) => new(new ProblemDetails
    {
        Type = "https://docs.example.invalid/problems/resource-state",
        Title = title,
        Status = StatusCodes.Status409Conflict,
        Extensions = { ["code"] = code }
    })
    {
        StatusCode = StatusCodes.Status409Conflict
    };

    private static BadRequestObjectResult InvalidSchedule(string detail) => new(new ValidationProblemDetails(
        new Dictionary<string, string[]> { ["schedule"] = [detail] })
    {
        Type = "https://docs.example.invalid/problems/assignment-schedule",
        Title = "The assignment schedule is invalid.",
        Status = StatusCodes.Status400BadRequest,
        Extensions = { ["code"] = "assignment_schedule_invalid" }
    });
}

public sealed record PublishDeviceAssignmentRequest(
    Guid PlaylistVersionId,
    [param: Range(-1000, 1000)] int Priority = 0,
    DateTimeOffset? StartsAtUtc = null,
    DateTimeOffset? EndsAtUtc = null,
    [param: Required, StringLength(80, MinimumLength = 1)] string PresentationTimeZone = "UTC",
    bool OverrideEqualPriority = false);

public sealed record DeviceAssignmentResponse(
    Guid Id,
    Guid DeviceId,
    Guid PlaylistVersionId,
    bool IsEnabled,
    int Priority,
    DateTimeOffset? StartsAtUtc,
    DateTimeOffset? EndsAtUtc,
    string PresentationTimeZone,
    DateTimeOffset PublishedAtUtc,
    Guid ConcurrencyToken);

public sealed record AssignmentPublishedResponse(
    DeviceAssignmentResponse Assignment,
    Guid DesiredStateId,
    long DesiredStateVersion,
    string ManifestSha256);
