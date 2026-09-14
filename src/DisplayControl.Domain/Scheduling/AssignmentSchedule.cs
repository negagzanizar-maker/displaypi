namespace DisplayControl.Domain.Scheduling;

public static class AssignmentSchedule
{
    public static bool IsActiveAt(
        DateTimeOffset? startsAtUtc,
        DateTimeOffset? endsAtUtc,
        DateTimeOffset nowUtc)
    {
        EnsureWindow(startsAtUtc, endsAtUtc);
        EnsureUtc(nowUtc, nameof(nowUtc));
        return (!startsAtUtc.HasValue || startsAtUtc.Value <= nowUtc) &&
            (!endsAtUtc.HasValue || endsAtUtc.Value > nowUtc);
    }

    public static bool Overlaps(
        DateTimeOffset? firstStartsAtUtc,
        DateTimeOffset? firstEndsAtUtc,
        DateTimeOffset? secondStartsAtUtc,
        DateTimeOffset? secondEndsAtUtc)
    {
        EnsureWindow(firstStartsAtUtc, firstEndsAtUtc);
        EnsureWindow(secondStartsAtUtc, secondEndsAtUtc);
        return (!firstEndsAtUtc.HasValue || !secondStartsAtUtc.HasValue ||
                secondStartsAtUtc.Value < firstEndsAtUtc.Value) &&
            (!secondEndsAtUtc.HasValue || !firstStartsAtUtc.HasValue ||
                firstStartsAtUtc.Value < secondEndsAtUtc.Value);
    }

    public static void EnsureWindow(DateTimeOffset? startsAtUtc, DateTimeOffset? endsAtUtc)
    {
        EnsureUtc(startsAtUtc, nameof(startsAtUtc));
        EnsureUtc(endsAtUtc, nameof(endsAtUtc));
        if (startsAtUtc.HasValue && endsAtUtc.HasValue && endsAtUtc <= startsAtUtc)
        {
            throw new ArgumentException("Assignment end must be after its start.", nameof(endsAtUtc));
        }
    }

    public static void EnsureSupportedTimeZone(string presentationTimeZone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presentationTimeZone);
        var normalized = presentationTimeZone.Trim();
        if (normalized.Length > 80)
        {
            throw new ArgumentException("Time-zone identifier cannot exceed 80 characters.", nameof(presentationTimeZone));
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(normalized);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new ArgumentException("Time-zone identifier is not supported by this deployment.", nameof(presentationTimeZone));
        }
    }

    public static void EnsureUtc(DateTimeOffset? value, string parameterName)
    {
        if (value is { Offset: var offset } && offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must have a UTC offset.", parameterName);
        }
    }
}
