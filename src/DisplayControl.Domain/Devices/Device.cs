using DisplayControl.Domain.Tenancy;

namespace DisplayControl.Domain.Devices;

public sealed class Device : TenantOwnedEntity
{
    private Device()
    {
    }

    public Device(Guid id, Guid tenantId, string displayName, DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty)
        {
            throw new ArgumentException("Device and tenant identifiers cannot be empty.");
        }

        if (createdAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Creation timestamp must have a UTC offset.", nameof(createdAtUtc));
        }

        var normalizedName = displayName.Trim();
        if (normalizedName.Length is 0 or > 160)
        {
            throw new ArgumentException("Display name must contain 1–160 characters.", nameof(displayName));
        }

        Id = id;
        TenantId = tenantId;
        DisplayName = normalizedName;
        State = DeviceLifecycleState.PendingEnrollment;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    public string DisplayName { get; private set; } = string.Empty;

    public DeviceLifecycleState State { get; private set; }

    public string? SerialNumberNormalized { get; private set; }

    public string? Hostname { get; private set; }

    public string? OsDescription { get; private set; }

    public string? Architecture { get; private set; }

    public string? AgentVersion { get; private set; }

    public string? PlayerVersion { get; private set; }

    public long? DiskCapacityBytes { get; private set; }

    public DateTimeOffset? LastSeenUtc { get; private set; }

    public long? AppliedManifestVersion { get; private set; }

    public string? PlaybackHealthCode { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public Guid ConcurrencyToken { get; private set; }

    public void Rename(string displayName, DateTimeOffset updatedAtUtc)
    {
        EnsureMutable();
        DisplayName = NormalizeDisplayName(displayName);
        Touch(updatedAtUtc);
    }

    public void CompleteEnrollment(
        string serialNumber,
        string hostname,
        string osDescription,
        string architecture,
        string agentVersion,
        string playerVersion,
        long diskCapacityBytes,
        DateTimeOffset enrolledAtUtc)
    {
        if (State != DeviceLifecycleState.PendingEnrollment)
        {
            throw new InvalidOperationException("Only a pending device can complete enrollment.");
        }

        SerialNumberNormalized = NormalizeRequired(serialNumber, 32, nameof(serialNumber)).ToUpperInvariant();
        Hostname = NormalizeRequired(hostname, 253, nameof(hostname));
        OsDescription = NormalizeRequired(osDescription, 256, nameof(osDescription));
        Architecture = NormalizeRequired(architecture, 32, nameof(architecture));
        AgentVersion = NormalizeRequired(agentVersion, 64, nameof(agentVersion));
        PlayerVersion = NormalizeRequired(playerVersion, 64, nameof(playerVersion));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(diskCapacityBytes);

        DiskCapacityBytes = diskCapacityBytes;
        LastSeenUtc = enrolledAtUtc;
        PlaybackHealthCode = "enrolled";
        State = DeviceLifecycleState.Active;
        Touch(enrolledAtUtc);
    }

    public void Suspend(DateTimeOffset suspendedAtUtc)
    {
        EnsureMutable();
        if (State == DeviceLifecycleState.Suspended)
        {
            return;
        }

        State = DeviceLifecycleState.Suspended;
        Touch(suspendedAtUtc);
    }

    public void Reactivate(DateTimeOffset reactivatedAtUtc)
    {
        if (State != DeviceLifecycleState.Suspended)
        {
            throw new InvalidOperationException("Only a suspended device can be reactivated.");
        }

        State = DeviceLifecycleState.Active;
        Touch(reactivatedAtUtc);
    }

    public void Retire(DateTimeOffset retiredAtUtc)
    {
        if (State == DeviceLifecycleState.Retired)
        {
            return;
        }

        State = DeviceLifecycleState.Retired;
        Touch(retiredAtUtc);
    }

    public void Quarantine(DateTimeOffset quarantinedAtUtc)
    {
        EnsureMutable();
        State = DeviceLifecycleState.Quarantined;
        Touch(quarantinedAtUtc);
    }

    public void RecordHeartbeat(
        string hostname,
        string osDescription,
        string architecture,
        string agentVersion,
        string playerVersion,
        long diskCapacityBytes,
        long? appliedManifestVersion,
        string playbackHealthCode,
        DateTimeOffset receivedAtUtc)
    {
        if (State != DeviceLifecycleState.Active)
        {
            throw new InvalidOperationException("Only an active device may record a heartbeat.");
        }

        Hostname = NormalizeRequired(hostname, 253, nameof(hostname));
        OsDescription = NormalizeRequired(osDescription, 256, nameof(osDescription));
        Architecture = NormalizeRequired(architecture, 32, nameof(architecture));
        AgentVersion = NormalizeRequired(agentVersion, 64, nameof(agentVersion));
        PlayerVersion = NormalizeRequired(playerVersion, 64, nameof(playerVersion));
        PlaybackHealthCode = NormalizeRequired(playbackHealthCode, 64, nameof(playbackHealthCode));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(diskCapacityBytes);
        if (appliedManifestVersion is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(appliedManifestVersion));
        }

        DiskCapacityBytes = diskCapacityBytes;
        AppliedManifestVersion = appliedManifestVersion;
        LastSeenUtc = receivedAtUtc;
        Touch(receivedAtUtc);
    }

    private static string NormalizeDisplayName(string displayName) =>
        NormalizeRequired(displayName, 160, nameof(displayName));

    private static string NormalizeRequired(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException($"Value cannot exceed {maximumLength} characters.", parameterName);
        }

        return normalized;
    }

    private void EnsureMutable()
    {
        if (State == DeviceLifecycleState.Retired)
        {
            throw new InvalidOperationException("A retired device is immutable.");
        }
    }

    private void Touch(DateTimeOffset occurredAtUtc)
    {
        if (occurredAtUtc.Offset != TimeSpan.Zero || occurredAtUtc < UpdatedAtUtc)
        {
            throw new ArgumentException("Device timestamps must be monotonic UTC instants.", nameof(occurredAtUtc));
        }

        UpdatedAtUtc = occurredAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }
}
