using DisplayControl.Domain.Licensing;

namespace DisplayControl.Domain.Tests.Licensing;

public sealed class LicenseWindowTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = Start.AddDays(30);

    [Fact]
    public void EvaluateUsesInclusiveStartAndExclusiveEnd()
    {
        var window = new LicenseWindow(Start, End);

        Assert.Equal(LicenseEffectiveState.Future, window.Evaluate(Start.AddTicks(-1)));
        Assert.Equal(LicenseEffectiveState.Active, window.Evaluate(Start));
        Assert.Equal(LicenseEffectiveState.Active, window.Evaluate(End.AddTicks(-1)));
        Assert.Equal(LicenseEffectiveState.Expired, window.Evaluate(End));
    }

    [Fact]
    public void EvaluateRevocationTakesPriorityOverSuspensionAndTime()
    {
        var window = new LicenseWindow(Start, End);

        var state = window.Evaluate(Start.AddDays(1), isSuspended: true, revokedAtUtc: Start.AddHours(1));

        Assert.Equal(LicenseEffectiveState.Revoked, state);
    }

    [Fact]
    public void CalculateLeaseExpiryIsCappedAtTwentyFourHours()
    {
        var window = new LicenseWindow(Start, End);
        var now = Start.AddHours(1);

        var expiry = window.CalculateLeaseExpiry(now, TimeSpan.FromHours(24));

        Assert.Equal(now.AddHours(24), expiry);
    }

    [Fact]
    public void CalculateLeaseExpiryNeverExceedsActualLicenceExpiry()
    {
        var now = Start.AddHours(1);
        var licenceEnd = now.AddMinutes(10);
        var window = new LicenseWindow(Start, licenceEnd);

        var expiry = window.CalculateLeaseExpiry(now, TimeSpan.FromHours(24));

        Assert.Equal(licenceEnd, expiry);
    }

    [Fact]
    public void CalculateLeaseExpiryUsesEarlierAuthorizationBoundary()
    {
        var window = new LicenseWindow(Start, End);
        var now = Start.AddHours(1);
        var boundary = now.AddMinutes(20);

        var expiry = window.CalculateLeaseExpiry(now, TimeSpan.FromHours(24), boundary);

        Assert.Equal(boundary, expiry);
    }

    [Fact]
    public void CalculateLeaseExpiryRejectsAllowanceAboveTwentyFourHours()
    {
        var window = new LicenseWindow(Start, End);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            window.CalculateLeaseExpiry(Start, TimeSpan.FromHours(24).Add(TimeSpan.FromTicks(1))));
    }

    [Fact]
    public void ConstructorRejectsNonUtcAuthorizationTimestamps()
    {
        var nonUtc = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.FromHours(1));

        Assert.Throws<ArgumentException>(() => new LicenseWindow(nonUtc, End));
    }
}
