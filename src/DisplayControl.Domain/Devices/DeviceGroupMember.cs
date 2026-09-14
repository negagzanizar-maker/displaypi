using DisplayControl.Domain.Tenancy;

namespace DisplayControl.Domain.Devices;

public sealed class DeviceGroupMember : TenantOwnedEntity
{
    private DeviceGroupMember()
    {
    }

    public DeviceGroupMember(
        Guid id,
        Guid tenantId,
        Guid deviceGroupId,
        Guid deviceId,
        Guid addedByUserId,
        DateTimeOffset addedAtUtc)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || deviceGroupId == Guid.Empty ||
            deviceId == Guid.Empty || addedByUserId == Guid.Empty)
        {
            throw new ArgumentException("Group-member identifiers cannot be empty.");
        }

        if (addedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must have a UTC offset.", nameof(addedAtUtc));
        }

        Id = id;
        TenantId = tenantId;
        DeviceGroupId = deviceGroupId;
        DeviceId = deviceId;
        AddedByUserId = addedByUserId;
        AddedAtUtc = addedAtUtc;
    }

    public Guid DeviceGroupId { get; private set; }

    public Guid DeviceId { get; private set; }

    public Guid AddedByUserId { get; private set; }

    public DateTimeOffset AddedAtUtc { get; private set; }
}
