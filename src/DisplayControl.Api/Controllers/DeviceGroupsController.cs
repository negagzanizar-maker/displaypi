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
[Route("api/v1/tenants/{tenantId:guid}/device-groups")]
public sealed class DeviceGroupsController(
    DisplayControlDbContext dbContext,
    DesiredStateCompilationService compilationService,
    DeviceStateChangeNotifications notifications,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.TenantViewer)]
    public async Task<ActionResult<IReadOnlyList<DeviceGroupResponse>>> List(
        Guid tenantId,
        [FromQuery, Range(1, 100)] int limit = 50,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        if (!CursorPage.TryReadOffset(cursor, out var offset)) return BadRequest("Invalid pagination cursor.");
        var groups = await dbContext.DeviceGroups.AsNoTracking()
            .OrderBy(value => value.Name)
            .ThenBy(value => value.Id)
            .Skip(offset)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        CursorPage.WriteNext(Response, offset, limit, groups.Count);
        groups = groups.Take(limit).ToList();
        var groupIds = groups.Select(group => group.Id).ToArray();
        var members = await dbContext.DeviceGroupMembers.AsNoTracking()
            .Where(value => groupIds.Contains(value.DeviceGroupId))
            .ToListAsync(cancellationToken);
        return Ok(groups.Select(group => ToResponse(
            group,
            members.Where(member => member.DeviceGroupId == group.Id).Select(member => member.DeviceId)))
            .ToArray());
    }

    [HttpGet("{groupId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.TenantViewer)]
    public async Task<ActionResult<DeviceGroupResponse>> Get(
        Guid tenantId,
        Guid groupId,
        CancellationToken cancellationToken)
    {
        var group = await dbContext.DeviceGroups.AsNoTracking().SingleOrDefaultAsync(
            value => value.Id == groupId,
            cancellationToken);
        if (group is null)
        {
            return NotFound();
        }

        var memberIds = await dbContext.DeviceGroupMembers.AsNoTracking()
            .Where(value => value.DeviceGroupId == groupId)
            .OrderBy(value => value.DeviceId)
            .Select(value => value.DeviceId)
            .ToListAsync(cancellationToken);
        return Ok(ToResponse(group, memberIds));
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.TenantAdministrator)]
    public async Task<ActionResult<DeviceGroupResponse>> Create(
        Guid tenantId,
        CreateDeviceGroupRequest request,
        CancellationToken cancellationToken)
    {
        var actorId = CurrentUserId();
        var nowUtc = TruncateToMilliseconds(timeProvider.GetUtcNow());
        DeviceGroup group;
        try
        {
            group = new DeviceGroup(Guid.NewGuid(), tenantId, request.Name, request.Description, actorId, nowUtc);
        }
        catch (ArgumentException exception)
        {
            return InvalidGroup(exception.Message);
        }

        if (await dbContext.DeviceGroups.AnyAsync(value => value.Name == group.Name, cancellationToken))
        {
            return DeviceAssignmentsController.ConflictProblem(
                "device_group_name_conflict",
                "A device group with this name already exists.");
        }

        dbContext.DeviceGroups.Add(group);
        AddAudit("device_group.created", group.Id, new { group.Name }, actorId, tenantId, nowUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { tenantId, groupId = group.Id }, ToResponse(group, []));
    }

    [HttpPatch("{groupId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.TenantAdministrator)]
    public async Task<ActionResult<DeviceGroupResponse>> Update(
        Guid tenantId,
        Guid groupId,
        UpdateDeviceGroupRequest request,
        CancellationToken cancellationToken)
    {
        var group = await dbContext.DeviceGroups.SingleOrDefaultAsync(value => value.Id == groupId, cancellationToken);
        if (group is null)
        {
            return NotFound();
        }

        if (group.ConcurrencyToken != request.ConcurrencyToken)
        {
            return DeviceAssignmentsController.ConflictProblem(
                "device_group_concurrency_conflict",
                "The device group changed; reload before updating it.");
        }

        var actorId = CurrentUserId();
        var nowUtc = TruncateToMilliseconds(timeProvider.GetUtcNow());
        try
        {
            group.Update(request.Name, request.Description, nowUtc);
        }
        catch (ArgumentException exception)
        {
            return InvalidGroup(exception.Message);
        }

        if (await dbContext.DeviceGroups.AnyAsync(
                value => value.Id != groupId && value.Name == group.Name,
                cancellationToken))
        {
            return DeviceAssignmentsController.ConflictProblem(
                "device_group_name_conflict",
                "A device group with this name already exists.");
        }

        AddAudit("device_group.updated", group.Id, new { group.Name }, actorId, tenantId, nowUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
        var memberIds = await dbContext.DeviceGroupMembers.AsNoTracking()
            .Where(value => value.DeviceGroupId == groupId)
            .Select(value => value.DeviceId)
            .ToListAsync(cancellationToken);
        return Ok(ToResponse(group, memberIds));
    }

    [HttpPut("{groupId:guid}/members")]
    [Authorize(Policy = AuthorizationPolicies.TenantAdministrator)]
    public async Task<ActionResult<DeviceGroupResponse>> ReplaceMembers(
        Guid tenantId,
        Guid groupId,
        ReplaceDeviceGroupMembersRequest request,
        CancellationToken cancellationToken)
    {
        await MutationLocks.SchedulingAsync(dbContext, tenantId, cancellationToken);
        var group = await dbContext.DeviceGroups.SingleOrDefaultAsync(value => value.Id == groupId, cancellationToken);
        if (group is null)
        {
            return NotFound();
        }

        if (group.ConcurrencyToken != request.ConcurrencyToken)
        {
            return DeviceAssignmentsController.ConflictProblem(
                "device_group_concurrency_conflict",
                "The device group changed; reload before replacing members.");
        }

        var requestedIds = request.DeviceIds.Distinct().Order().ToArray();
        var devices = await dbContext.Devices.AsNoTracking()
            .Where(value => requestedIds.Contains(value.Id))
            .ToListAsync(cancellationToken);
        if (devices.Count != requestedIds.Length ||
            devices.Any(value => value.State is DeviceLifecycleState.Retired or DeviceLifecycleState.Quarantined))
        {
            return DeviceAssignmentsController.ConflictProblem(
                "device_group_member_invalid",
                "Every group member must be an existing non-retired, non-quarantined tenant device.");
        }

        var ownAssignments = await dbContext.GroupAssignments.AsNoTracking()
            .Where(value => value.DeviceGroupId == groupId && value.IsEnabled)
            .ToListAsync(cancellationToken);
        if (requestedIds.Length > 0 && ownAssignments.Count > 0)
        {
            var otherGroupIds = await dbContext.DeviceGroupMembers.AsNoTracking()
                .Where(value => requestedIds.Contains(value.DeviceId) && value.DeviceGroupId != groupId)
                .Select(value => value.DeviceGroupId)
                .Distinct()
                .ToListAsync(cancellationToken);
            var otherAssignments = await dbContext.GroupAssignments.AsNoTracking()
                .Where(value => otherGroupIds.Contains(value.DeviceGroupId) && value.IsEnabled)
                .ToListAsync(cancellationToken);
            if (ownAssignments.Any(own => otherAssignments.Any(other =>
                    own.Priority == other.Priority && AssignmentSchedule.Overlaps(
                        own.StartsAtUtc,
                        own.EndsAtUtc,
                        other.StartsAtUtc,
                        other.EndsAtUtc))))
            {
                return DeviceAssignmentsController.ConflictProblem(
                    "assignment_priority_collision",
                    "The requested membership would create an overlapping equal-priority group assignment.");
            }
        }

        var existing = await dbContext.DeviceGroupMembers
            .Where(value => value.DeviceGroupId == groupId)
            .ToListAsync(cancellationToken);
        var existingIds = existing.Select(value => value.DeviceId).ToHashSet();
        var addedIds = requestedIds.Where(value => !existingIds.Contains(value)).ToArray();
        dbContext.DeviceGroupMembers.RemoveRange(existing.Where(value => !requestedIds.Contains(value.DeviceId)));
        var actorId = CurrentUserId();
        var nowUtc = TruncateToMilliseconds(timeProvider.GetUtcNow());
        dbContext.DeviceGroupMembers.AddRange(addedIds.Select(deviceId => new DeviceGroupMember(
            Guid.NewGuid(),
            tenantId,
            groupId,
            deviceId,
            actorId,
            nowUtc)));

        foreach (var assignment in ownAssignments.Where(_ => addedIds.Length > 0))
        {
            IReadOnlyList<DesiredStateManifestAsset> assets;
            try
            {
                assets = await compilationService.LoadApprovedPlaylistAsync(
                    assignment.PlaylistVersionId,
                    cancellationToken);
            }
            catch (AssignmentPublicationException exception)
            {
                return DeviceAssignmentsController.ConflictProblem(exception.Code, exception.Message);
            }

            foreach (var deviceId in addedIds)
            {
                await compilationService.CompileGroupAssignmentAsync(
                    tenantId,
                    deviceId,
                    assignment,
                    assets,
                    cancellationToken);
            }
        }

        group.MarkMembershipChanged(nowUtc);
        AddAudit(
            "device_group.members_replaced",
            group.Id,
            new { deviceIds = requestedIds },
            actorId,
            tenantId,
            nowUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
        notifications.Enqueue(tenantId, existingIds.Concat(requestedIds));
        return Ok(ToResponse(group, requestedIds));
    }

    private void AddAudit(
        string action,
        Guid groupId,
        object details,
        Guid actorId,
        Guid tenantId,
        DateTimeOffset nowUtc) => dbContext.AuditEvents.Add(new TenantAuditEvent(
            Guid.NewGuid(),
            tenantId,
            "user",
            actorId,
            action,
            "device_group",
            groupId,
            "success",
            null,
            HttpContext.TraceIdentifierGuid(),
            JsonSerializer.Serialize(details),
            nowUtc));

    private Guid CurrentUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
        ? userId
        : throw new InvalidOperationException("Authenticated principal has no valid user identifier.");

    private static DateTimeOffset TruncateToMilliseconds(DateTimeOffset value) =>
        value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMillisecond));

    private static DeviceGroupResponse ToResponse(DeviceGroup group, IEnumerable<Guid> memberIds) => new(
        group.Id,
        group.Name,
        group.Description,
        memberIds.Order().ToArray(),
        group.CreatedAtUtc,
        group.UpdatedAtUtc,
        group.ConcurrencyToken);

    private static BadRequestObjectResult InvalidGroup(string detail) => new(new ValidationProblemDetails(
        new Dictionary<string, string[]> { ["deviceGroup"] = [detail] })
    {
        Type = "https://docs.example.invalid/problems/device-group",
        Title = "The device group is invalid.",
        Status = StatusCodes.Status400BadRequest,
        Extensions = { ["code"] = "device_group_invalid" }
    });
}

public sealed record CreateDeviceGroupRequest(
    [param: Required, StringLength(160, MinimumLength = 1)] string Name,
    [param: StringLength(2000)] string? Description);

public sealed record UpdateDeviceGroupRequest(
    [param: Required, StringLength(160, MinimumLength = 1)] string Name,
    [param: StringLength(2000)] string? Description,
    Guid ConcurrencyToken);

public sealed record ReplaceDeviceGroupMembersRequest(
    [param: MaxLength(500)] IReadOnlyList<Guid> DeviceIds,
    Guid ConcurrencyToken);

public sealed record DeviceGroupResponse(
    Guid Id,
    string Name,
    string? Description,
    IReadOnlyList<Guid> DeviceIds,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    Guid ConcurrencyToken);
