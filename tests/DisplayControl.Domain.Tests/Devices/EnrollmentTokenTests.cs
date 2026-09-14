using DisplayControl.Domain.Devices;

namespace DisplayControl.Domain.Tests.Devices;

public sealed class EnrollmentTokenTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ConstructorCopiesDigestAndStartsConsumable()
    {
        var digest = Enumerable.Repeat((byte)0xA5, 32).ToArray();
        var token = CreateToken(digest: digest);

        digest[0] = 0;

        Assert.Equal(0xA5, token.TokenDigest[0]);
        Assert.True(token.CanBeConsumedAt(CreatedAt.AddMinutes(1)));
    }

    [Fact]
    public void ConsumeEnforcesExpectedDeviceAndIsOneUse()
    {
        var expectedDeviceId = Guid.NewGuid();
        var token = CreateToken(expectedDeviceId: expectedDeviceId);

        Assert.Throws<InvalidOperationException>(() => token.Consume(Guid.NewGuid(), CreatedAt.AddMinutes(1)));

        token.Consume(expectedDeviceId, CreatedAt.AddMinutes(1));

        Assert.Equal(expectedDeviceId, token.ConsumedByDeviceId);
        Assert.False(token.CanBeConsumedAt(CreatedAt.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => token.Consume(expectedDeviceId, CreatedAt.AddMinutes(2)));
    }

    [Fact]
    public void FailedAttemptLimitLocksToken()
    {
        var token = CreateToken();

        for (var attempt = 0; attempt < EnrollmentToken.MaximumFailedAttempts; attempt++)
        {
            token.RecordFailedAttempt(CreatedAt.AddSeconds(attempt + 1));
        }

        Assert.Equal(EnrollmentToken.MaximumFailedAttempts, token.FailedAttemptCount);
        Assert.False(token.CanBeConsumedAt(CreatedAt.AddMinutes(1)));
    }

    [Fact]
    public void ExpiredTokenCannotBeConsumed()
    {
        var token = CreateToken();

        Assert.False(token.CanBeConsumedAt(CreatedAt.AddMinutes(10)));
        Assert.Throws<InvalidOperationException>(() => token.Consume(Guid.NewGuid(), CreatedAt.AddMinutes(10)));
    }

    private static EnrollmentToken CreateToken(
        byte[]? digest = null,
        Guid? expectedDeviceId = null) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            digest ?? new byte[32],
            CreatedAt.AddMinutes(10),
            Guid.NewGuid(),
            CreatedAt,
            expectedDeviceId);
}
