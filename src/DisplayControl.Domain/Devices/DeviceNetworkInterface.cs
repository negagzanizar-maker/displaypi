using DisplayControl.Domain.Tenancy;

namespace DisplayControl.Domain.Devices;

public sealed class DeviceNetworkInterface : TenantOwnedEntity
{
    private DeviceNetworkInterface()
    {
    }

    public DeviceNetworkInterface(
        Guid id,
        Guid tenantId,
        Guid deviceId,
        string interfaceName,
        string? macAddressNormalized,
        string localAddressesJson,
        DateTimeOffset observedAtUtc)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || deviceId == Guid.Empty)
        {
            throw new ArgumentException("Network interface, tenant, and device identifiers cannot be empty.");
        }

        var normalizedName = interfaceName.Trim();
        if (normalizedName.Length is 0 or > 64)
        {
            throw new ArgumentException("Interface name must contain 1-64 characters.", nameof(interfaceName));
        }

        var normalizedMac = macAddressNormalized?.Trim().ToUpperInvariant();
        if (normalizedMac is not null &&
            (normalizedMac.Length != 12 || normalizedMac.Any(character => !Uri.IsHexDigit(character))))
        {
            throw new ArgumentException("MAC address must contain 12 hexadecimal characters without separators.", nameof(macAddressNormalized));
        }

        if (string.IsNullOrWhiteSpace(localAddressesJson) || localAddressesJson.Length > 8192)
        {
            throw new ArgumentException("Local address JSON is required and cannot exceed 8192 characters.", nameof(localAddressesJson));
        }

        EnsureUtc(observedAtUtc, nameof(observedAtUtc));
        Id = id;
        TenantId = tenantId;
        DeviceId = deviceId;
        InterfaceName = normalizedName;
        MacAddressNormalized = normalizedMac;
        LocalAddressesJson = localAddressesJson;
        ObservedAtUtc = observedAtUtc;
    }

    public Guid DeviceId { get; private set; }

    public string InterfaceName { get; private set; } = string.Empty;

    public string? MacAddressNormalized { get; private set; }

    public string LocalAddressesJson { get; private set; } = "[]";

    public DateTimeOffset ObservedAtUtc { get; private set; }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must have a UTC offset.", parameterName);
        }
    }
}
