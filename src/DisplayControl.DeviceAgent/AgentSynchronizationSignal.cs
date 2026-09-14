using System.Threading.Channels;

namespace DisplayControl.DeviceAgent;

public sealed class AgentSynchronizationSignal
{
    private readonly Channel<byte> _signals = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false
    });

    public bool RequestSynchronization() => _signals.Writer.TryWrite(0);

    public async Task<bool> WaitForSignalOrTimeoutAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            if (!await _signals.Reader.WaitToReadAsync(timeoutSource.Token))
            {
                return false;
            }

            while (_signals.Reader.TryRead(out _))
            {
            }

            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
