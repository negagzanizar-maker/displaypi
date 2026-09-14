using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using DisplayControl.Api.Pagination;
using DisplayControl.Api.Realtime;
using DisplayControl.Api.Scheduling;
using DisplayControl.Api.Security;
using DisplayControl.Application.Content;
using DisplayControl.Domain.Operations;
using DisplayControl.Domain.Scheduling;
using DisplayControl.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DisplayControl.Api.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId:guid}/device-groups/{groupId:guid}/assignments")]
public sealed class GroupAssignmentsController(
    DisplayControlDbContext dbContext,
    DesiredStateCompilationService compilationService,
    DeviceStateChangeNotifications notifications,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.TenantViewer)]
    public async Task<ActionResult<IReadOnlyList<GroupAssignmentResponse>>> List(
        Guid tenantId,
        Guid groupId,
        [FromQuery, Range(1, 100)] int limit = 50,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        if (!CursorPage.TryReadOffset(cursor, out var offset)) return BadRequest("Invalid pagination cursor.");
        if (!await dbContext.DeviceGroups.AsNoTracking().AnyAsync(value => value.Id == groupId, cancellationToken))
        {
            return NotFound();
        }

        var assignments = await dbContext.GroupAssignments.AsNoTracking()
            .Where(value => value.DeviceGroupId == groupId)
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
    public async Task<ActionResult<GroupAssignmentPublishedResponse>> Publish(
        Guid tenantId,
        Guid groupId,
        PublishGroupAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        await MutationLocks.SchedulingAsync(dbContext, tenantId, cancellationToken);
        if (!await dbContext.DeviceGroups.AsNoTracking().AnyAsync(value => value.Id == groupId, cancellationToken))
        {
            return NotFound();
        }

        var memberIds = await dbContext.DeviceGroupMembers.AsNoTracking()
            .Where(value => value.DeviceGroupId == groupId)
            .Select(value => value.DeviceId)
            .OrderBy(value => value)
            .ToListAsync(cancellationToken);
        if (memberIds.Count == 0)
        {
            return DeviceAssignmentsController.ConflictProblem(
                "device_group_empty",
                "Assign at least one device to the group before publishing content.");
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
                : DeviceAssignmentsController.ConflictProblem(exception.Code, exception.Message);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["schedule"] = [exception.Message] })
            {
                Type = "https://docs.example.invalid/problems/assignment-schedule",
                Title = "The assignment schedule is invalid.",
                Status = StatusCodes.Status400BadRequest,
                Extensions = { ["code"] = "assignment_schedule_invalid" }
            });
        }

        var memberGroupIds = await dbContext.DeviceGroupMembers.AsNoTracking()
            .Where(value => memberIds.Contains(value.DeviceId))
            .Select(value => value.DeviceGroupId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var existingAssignments = await dbContext.GroupAssignments.AsNoTracking()
            .Where(value => memberGroupIds.Contains(value.DeviceGroupId) &&
                value.IsEnabled &&
                value.Priority == request.Priority)
            .ToListAsync(cancellationToken);
        if (existingAssignments.Any(value => AssignmentSchedule.Overlaps(
                value.StartsAtUtc,
                value.EndsAtUtc,
                request.StartsAtUtc,
                request.EndsAtUtc)))
        {
            return DeviceAssignmentsController.ConflictProblem(
                "assignment_priority_collision",
                "The requested schedule overlaps an equal-priority group assignment for one or more devices.");
        }

        var actorId = CurrentUserId();
        var nowUtc = timeProvider.GetUtcNow();
        nowUtc = nowUtc.AddTicks(-(nowUtc.Ticks % TimeSpan.TicksPerMillisecond));
        var assignment = new GroupAssignment(
            Guid.NewGuid(),
            tenantId,
            groupId,
            request.PlaylistVersionId,
            request.Priority,
            request.StartsAtUtc,
            request.EndsAtUtc,
            request.PresentationTimeZone,
            actorId,
            nowUtc);
        dbContext.GroupAssignments.Add(assignment);
        var desiredStates = new List<DesiredState>(memberIds.Count);
        foreach (var deviceId in memberIds)
        {
            desiredStates.Add(await compilationService.CompileGroupAssignmentAsync(
                tenantId,
                deviceId,
                assignment,
                manifestAssets,
                cancellationToken));
        }

        dbContext.AuditEvents.Add(new TenantAuditEvent(
            Guid.NewGuid(),
            tenantId,
            "user",
            actorId,
            "group_assignment.published",
            "device_group",
            groupId,
            "success",
            null,
            HttpContext.TraceIdentifierGuid(),
            JsonSerializer.Serialize(new
            {
                assignmentId = assignment.Id,
                request.PlaylistVersionId,
                desiredStateCount = desiredStates.Count,
                request.StartsAtUtc,
                request.EndsAtUtc,
                request.Priority
            }),
            nowUtc));
        await dbContext.SaveChangesAsync(cancellationToken);
        notifications.Enqueue(tenantId, memberIds);
        return CreatedAtAction(
            nameof(List),
            new { tenantId, groupId },
            new GroupAssignmentPublishedResponse(
                ToResponse(assignment),
                desiredStates.Select(value => new CompiledDesiredStateResponse(
                    value.DeviceId,
                    value.Id,
                    value.Version,
                    Convert.ToHexString(value.ManifestSha256).ToLowerInvariant())).ToArray()));
    }

    private Guid CurrentUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
        ? userId
        : throw new InvalidOperationException("Authenticated principal has no valid user identifier.");

    private static GroupAssignmentResponse ToResponse(GroupAssignment value) => new(
        value.Id,
        value.DeviceGroupId,
        value.PlaylistVersionId,
        value.IsEnabled,
        value.Priority,
        value.StartsAtUtc,
        value.EndsAtUtc,
        value.PresentationTimeZone,
        value.PublishedAtUtc,
        value.ConcurrencyToken);
}

public sealed record PublishGroupAssignmentRequest(
    Guid PlaylistVersionId,
    [param: Range(-1000, 1000)] int Priority = 0,
    DateTimeOffset? StartsAtUtc = null,
    DateTimeOffset? EndsAtUtc = null,
    [param: Required, StringLength(80, MinimumLength = 1)] string PresentationTimeZone = "UTC");

public sealed record GroupAssignmentResponse(
    Guid Id,
    Guid DeviceGroupId,
    Guid PlaylistVersionId,
    bool IsEnabled,
    int Priority,
    DateTimeOffset? StartsAtUtc,
    DateTimeOffset? EndsAtUtc,
    string PresentationTimeZone,
    DateTimeOffset PublishedAtUtc,
    Guid ConcurrencyToken);

public sealed record CompiledDesiredStateResponse(
    Guid DeviceId,
    Guid DesiredStateId,
    long DesiredStateVersion,
    string ManifestSha256);

public sealed record GroupAssignmentPublishedResponse(
    GroupAssignmentResponse Assignment,
    IReadOnlyList<CompiledDesiredStateResponse> DesiredStates);
