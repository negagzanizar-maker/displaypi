namespace DisplayControl.Api.Notifications;

public sealed record NotificationDeliveryAttemptDecision(
    int AttemptCount,
    bool IsTerminalFailure,
    DateTimeOffset NextAttemptAtUtc);

public static class NotificationDeliveryAttemptPolicy
{
    public static NotificationDeliveryAttemptDecision Evaluate(
        int previousAttemptCount,
        bool succeeded,
        int maximumAttempts,
        DateTimeOffset nowUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(previousAttemptCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumAttempts);
        if (nowUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Notification timestamps must use UTC.", nameof(nowUtc));
        }

        var attemptCount = checked(previousAttemptCount + 1);
        var terminal = !succeeded && attemptCount >= maximumAttempts;
        var backoff = TimeSpan.FromMinutes(Math.Min(Math.Pow(2, Math.Clamp(attemptCount - 1, 0, 6)), 60));
        return new NotificationDeliveryAttemptDecision(attemptCount, terminal, nowUtc.Add(backoff));
    }
}
