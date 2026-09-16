using DisplayControl.Domain.Scheduling;
using DisplayControl.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DisplayControl.Api.Scheduling;

public sealed class DesiredStateResolver(DisplayControlDbContext dbContext)
{
    public async Task<DesiredState?> ResolveAsync(
        Guid deviceId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        AssignmentSchedule.EnsureUtc(nowUtc, nameof(nowUtc));
        var direct = await (
            from state in dbContext.DesiredStates.AsNoTracking()
            join assignment in dbContext.DeviceAssignments.AsNoTracking()
                on state.SourceDeviceAssignmentId equals (Guid?)assignment.Id
            where state.DeviceId == deviceId && state.SupersededByDesiredStateId == null &&
                !dbContext.DesiredStates.Any(newer => newer.DeviceId == state.DeviceId &&
                    newer.SourceDeviceAssignmentId == state.SourceDeviceAssignmentId &&
                    newer.Version > state.Version && newer.SupersededByDesiredStateId == null) &&
                assignment.IsEnabled &&
                (assignment.StartsAtUtc == null || assignment.StartsAtUtc <= nowUtc) &&
                (assignment.EndsAtUtc == null || assignment.EndsAtUtc > nowUtc)
            orderby assignment.PublishedAtUtc descending, state.Version descending, assignment.Priority descending
            select new DesiredStateCandidate(state, assignment.PublishedAtUtc, assignment.Priority))
            .Take(1)
            .ToListAsync(cancellationToken);

        var group = await (
            from state in dbContext.DesiredStates.AsNoTracking()
            join assignment in dbContext.GroupAssignments.AsNoTracking()
                on state.SourceGroupAssignmentId equals (Guid?)assignment.Id
            join membership in dbContext.DeviceGroupMembers.AsNoTracking()
                on new { assignment.TenantId, assignment.DeviceGroupId, DeviceId = state.DeviceId }
                equals new { membership.TenantId, membership.DeviceGroupId, membership.DeviceId }
            where state.DeviceId == deviceId && state.SupersededByDesiredStateId == null &&
                !dbContext.DesiredStates.Any(newer => newer.DeviceId == state.DeviceId &&
                    newer.SourceGroupAssignmentId == state.SourceGroupAssignmentId &&
                    newer.Version > state.Version && newer.SupersededByDesiredStateId == null) &&
                assignment.IsEnabled &&
                (assignment.StartsAtUtc == null || assignment.StartsAtUtc <= nowUtc) &&
                (assignment.EndsAtUtc == null || assignment.EndsAtUtc > nowUtc)
            orderby assignment.PublishedAtUtc descending, state.Version descending, assignment.Priority descending
            select new DesiredStateCandidate(state, assignment.PublishedAtUtc, assignment.Priority))
            .Take(1)
            .ToListAsync(cancellationToken);
        return direct.Concat(group)
            .OrderByDescending(value => value.PublishedAtUtc)
            .ThenByDescending(value => value.State.Version)
            .ThenByDescending(value => value.Priority)
            .Select(value => value.State)
            .FirstOrDefault();
    }

    private sealed record DesiredStateCandidate(
        DesiredState State,
        DateTimeOffset PublishedAtUtc,
        int Priority);
}
