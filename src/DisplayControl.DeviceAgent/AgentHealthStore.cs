namespace DisplayControl.DeviceAgent;

public sealed class AgentHealthStore(TimeProvider timeProvider)
{
    private readonly object _gate = new();
    private long? _lastCycleTimestamp;
    private bool _cycleSucceeded;

    public void RecordCycle(bool succeeded)
    {
        lock (_gate)
        {
            _lastCycleTimestamp = timeProvider.GetTimestamp();
            _cycleSucceeded = succeeded;
        }
    }

    public bool IsFresh(int intervalSeconds)
    {
        lock (_gate)
            return _cycleSucceeded && _lastCycleTimestamp is long timestamp &&
                timeProvider.GetElapsedTime(timestamp) <= TimeSpan.FromSeconds(intervalSeconds + 120);
    }
}
