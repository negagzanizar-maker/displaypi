namespace DisplayControl.Api.Realtime;

public sealed class DeviceStateChangeNotifications(DeviceStateChangeBroker broker)
{
    private readonly HashSet<DeviceConnectionKey> _pending = [];

    public void Enqueue(Guid tenantId, Guid deviceId)
    {
        if (tenantId == Guid.Empty || deviceId == Guid.Empty)
        {
            throw new ArgumentException("Tenant and device identifiers cannot be empty.");
        }

        _pending.Add(new DeviceConnectionKey(tenantId, deviceId));
    }

    public void Enqueue(Guid tenantId, IEnumerable<Guid> deviceIds)
    {
        foreach (var deviceId in deviceIds)
        {
            Enqueue(tenantId, deviceId);
        }
    }

    public void FlushCommitted()
    {
        foreach (var key in _pending)
        {
            broker.Signal(key.TenantId, key.DeviceId);
        }

        _pending.Clear();
    }
}
