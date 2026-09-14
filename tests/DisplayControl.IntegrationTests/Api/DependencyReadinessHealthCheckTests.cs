using DisplayControl.Api.Operations;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DisplayControl.IntegrationTests.Api;

public sealed class DependencyReadinessHealthCheckTests
{
    [Theory]
    [InlineData("database")]
    [InlineData("private-storage")]
    [InlineData("malware-scanner")]
    public async Task ReportsGenericUnhealthyResultWhenARequiredDependencyFails(string failingDependency)
    {
        var dependencies = new[]
        {
            new StubReadinessDependency("database", failingDependency != "database"),
            new StubReadinessDependency("private-storage", failingDependency != "private-storage"),
            new StubReadinessDependency("malware-scanner", failingDependency != "malware-scanner")
        };
        var probe = new CachedReadinessProbe(
            dependencies,
            ReadinessProbeOptions.Default,
            new ManualTimestampProvider());

        var result = await probe.CheckAsync(CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("A required dependency is unavailable.", result.Description);
        Assert.Equal(1, dependencies.Single(item => item.Name == failingDependency).CallCount);
    }

    [Fact]
    public async Task CachesSuccessfulProbeUntilTheHealthyCacheDurationExpires()
    {
        var timeProvider = new ManualTimestampProvider();
        var dependency = new StubReadinessDependency("database", isReady: true);
        var probe = new CachedReadinessProbe(
            [dependency],
            new ReadinessProbeOptions(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)),
            timeProvider);

        Assert.Equal(HealthStatus.Healthy, (await probe.CheckAsync(CancellationToken.None)).Status);
        Assert.Equal(HealthStatus.Healthy, (await probe.CheckAsync(CancellationToken.None)).Status);
        Assert.Equal(1, dependency.CallCount);

        timeProvider.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(HealthStatus.Healthy, (await probe.CheckAsync(CancellationToken.None)).Status);
        Assert.Equal(2, dependency.CallCount);
    }

    private sealed class StubReadinessDependency(string name, bool isReady) : IReadinessDependency
    {
        public string Name { get; } = name;

        public int CallCount { get; private set; }

        public Task<bool> IsReadyAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(isReady);
        }
    }

    private sealed class ManualTimestampProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);

        public void Advance(TimeSpan duration) =>
            Interlocked.Add(ref _timestamp, duration.Ticks);
    }
}
