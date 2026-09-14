using DisplayControl.Domain.Scheduling;

namespace DisplayControl.Domain.Tests.Scheduling;

public sealed class AssignmentScheduleTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ScheduleUsesInclusiveStartAndExclusiveEnd()
    {
        var startsAtUtc = Noon;
        var endsAtUtc = Noon.AddHours(1);

        Assert.False(AssignmentSchedule.IsActiveAt(startsAtUtc, endsAtUtc, Noon.AddTicks(-1)));
        Assert.True(AssignmentSchedule.IsActiveAt(startsAtUtc, endsAtUtc, Noon));
        Assert.True(AssignmentSchedule.IsActiveAt(startsAtUtc, endsAtUtc, endsAtUtc.AddTicks(-1)));
        Assert.False(AssignmentSchedule.IsActiveAt(startsAtUtc, endsAtUtc, endsAtUtc));
    }

    [Fact]
    public void AdjacentWindowsDoNotOverlapButEqualPriorityIntersectionsDo()
    {
        Assert.False(AssignmentSchedule.Overlaps(
            Noon,
            Noon.AddHours(1),
            Noon.AddHours(1),
            Noon.AddHours(2)));
        Assert.True(AssignmentSchedule.Overlaps(
            Noon,
            Noon.AddHours(1),
            Noon.AddMinutes(30),
            null));
        Assert.True(AssignmentSchedule.Overlaps(null, null, Noon, Noon.AddHours(1)));
    }

    [Fact]
    public void AssignmentRejectsUnsupportedTimeZoneAndNonUtcBounds()
    {
        Assert.Throws<ArgumentException>(() => new DeviceAssignment(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            0,
            Noon.ToOffset(TimeSpan.FromHours(1)),
            null,
            "UTC",
            Guid.NewGuid(),
            Noon));
        Assert.Throws<ArgumentException>(() => new DeviceAssignment(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            0,
            null,
            null,
            "Not/A-Real-Time-Zone",
            Guid.NewGuid(),
            Noon));
    }
}
