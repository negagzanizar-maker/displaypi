using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DisplayControl.DeviceAgent.Tests;

public sealed class RealTimeSynchronizationTests
{
    [Fact]
    public async Task DuplicateNotificationDuringSynchronizationTriggersOneSerializedFollowUpCycle()
    {
        var synchronization = new BlockingSynchronizationClient();
        var signal = new AgentSynchronizationSignal();
        var worker = new Worker(
            NullLogger<Worker>.Instance,
            synchronization,
            signal,
            new PlayerStateStore(TimeProvider.System),
            new AgentHealthStore(TimeProvider.System),
            Options.Create(OptionsForTest()));

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await synchronization.FirstCycleEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            signal.RequestSynchronization();
            signal.RequestSynchronization();
            synchronization.ReleaseFirstCycle.TrySetResult();

            await synchronization.SecondCycleCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(1, synchronization.MaximumConcurrency);
            Assert.Equal(2, synchronization.CycleCount);
        }
        finally
        {
            synchronization.ReleaseFirstCycle.TrySetResult();
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }
    }

    [Fact]
    public async Task MissingNotificationTimesOutSoPeriodicSynchronizationRemainsTheFallback()
    {
        var signal = new AgentSynchronizationSignal();

        var wasSignaled = await signal.WaitForSignalOrTimeoutAsync(
            TimeSpan.FromMilliseconds(20),
            CancellationToken.None);

        Assert.False(wasSignaled);
    }

    [Fact]
    public async Task WorkerRepeatsAuthoritativeSynchronizationWhenNoNotificationArrives()
    {
        var synchronization = new CountingSynchronizationClient();
        var worker = new Worker(
            NullLogger<Worker>.Instance,
            synchronization,
            new AgentSynchronizationSignal(),
            new PlayerStateStore(TimeProvider.System),
            new AgentHealthStore(TimeProvider.System),
            Options.Create(OptionsForTest(heartbeatIntervalSeconds: 0)));

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await synchronization.SecondCycleCompleted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(synchronization.CycleCount >= 2);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }
    }

    private static AgentRuntimeOptions OptionsForTest(int heartbeatIntervalSeconds = 300) => new()
    {
        ServerBaseAddress = new Uri("https://localhost"),
        HeartbeatIntervalSeconds = heartbeatIntervalSeconds,
        StateDirectory = Path.GetTempPath(),
        MaximumCacheBytes = 67_108_864,
        MinimumFreeDiskBytes = 16_777_216
    };

    private sealed class CountingSynchronizationClient : IDeviceSynchronizationClient
    {
        private int _cycleCount;

        public TaskCompletionSource SecondCycleCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CycleCount => Volatile.Read(ref _cycleCount);

        public Task SynchronizeOnceAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _cycleCount) >= 2)
            {
                SecondCycleCompleted.TrySetResult();
            }

            return Task.CompletedTask;
        }
    }

    private sealed class BlockingSynchronizationClient : IDeviceSynchronizationClient
    {
        private int _active;
        private int _cycleCount;
        private int _maximumConcurrency;

        public TaskCompletionSource FirstCycleEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirstCycle { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource SecondCycleCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CycleCount => Volatile.Read(ref _cycleCount);

        public int MaximumConcurrency => Volatile.Read(ref _maximumConcurrency);

        public async Task SynchronizeOnceAsync(CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _active);
            InterlockedExtensions.Max(ref _maximumConcurrency, active);
            var cycle = Interlocked.Increment(ref _cycleCount);
            try
            {
                if (cycle == 1)
                {
                    FirstCycleEntered.TrySetResult();
                    await ReleaseFirstCycle.Task.WaitAsync(cancellationToken);
                }

                if (cycle == 2)
                {
                    SecondCycleCompleted.TrySetResult();
                }
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref int location, int value)
        {
            var current = Volatile.Read(ref location);
            while (current < value)
            {
                var previous = Interlocked.CompareExchange(ref location, value, current);
                if (previous == current)
                {
                    return;
                }

                current = previous;
            }
        }
    }
}
