using System.Collections.Concurrent;
using System.Threading.Channels;

namespace DisplayControl.Api.Realtime;

public readonly record struct DeviceConnectionKey(Guid TenantId, Guid DeviceId);

public sealed class DeviceStateChangeBroker
{
    private readonly ConcurrentDictionary<DeviceConnectionKey, Connection> _connections = new();

    public DeviceStateChangeSubscription Subscribe(Guid tenantId, Guid deviceId)
    {
        if (tenantId == Guid.Empty || deviceId == Guid.Empty)
        {
            throw new ArgumentException("Tenant and device identifiers cannot be empty.");
        }

        var key = new DeviceConnectionKey(tenantId, deviceId);
        var connection = new Connection();
        _connections.AddOrUpdate(
            key,
            connection,
            (_, previous) =>
            {
                previous.Signals.Writer.TryComplete();
                return connection;
            });
        return new DeviceStateChangeSubscription(this, key, connection);
    }

    public bool Signal(Guid tenantId, Guid deviceId) =>
        _connections.TryGetValue(new DeviceConnectionKey(tenantId, deviceId), out var connection) &&
        connection.Signals.Writer.TryWrite(0);

    private void Remove(DeviceConnectionKey key, Connection connection)
    {
        if (_connections.TryRemove(new KeyValuePair<DeviceConnectionKey, Connection>(key, connection)))
        {
            connection.Signals.Writer.TryComplete();
        }
    }

    internal sealed class Connection
    {
        public Channel<byte> Signals { get; } = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    }

    public sealed class DeviceStateChangeSubscription : IDisposable
    {
        private readonly DeviceStateChangeBroker _owner;
        private readonly DeviceConnectionKey _key;
        private readonly Connection _connection;
        private int _disposed;

        internal DeviceStateChangeSubscription(
            DeviceStateChangeBroker owner,
            DeviceConnectionKey key,
            Connection connection)
        {
            _owner = owner;
            _key = key;
            _connection = connection;
        }

        public async ValueTask<bool> WaitAsync(CancellationToken cancellationToken)
        {
            if (!await _connection.Signals.Reader.WaitToReadAsync(cancellationToken))
            {
                return false;
            }

            while (_connection.Signals.Reader.TryRead(out _))
            {
            }

            return true;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner.Remove(_key, _connection);
            }
        }
    }
}
