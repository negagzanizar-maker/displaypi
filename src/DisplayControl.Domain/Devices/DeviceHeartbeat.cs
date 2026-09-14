using DisplayControl.Domain.Tenancy;

namespace DisplayControl.Domain.Devices;

public sealed class DeviceHeartbeat : TenantOwnedEntity
{
    private DeviceHeartbeat()
    {
    }

    public DeviceHeartbeat(
        Guid id,
        Guid tenantId,
        Guid deviceId,
        Guid bootId,
        long sequence,
        byte[] requestSha256,
        DateTimeOffset? reportedSentAtUtc,
        DateTimeOffset receivedAtUtc,
        string serverObservedIp,
        string inventoryJson,
        long? appliedDesiredStateVersion,
        string playerStateCode,
        long? freeDiskBytes,
        string? lastErrorCode,
        Guid correlationId)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || deviceId == Guid.Empty || bootId == Guid.Empty ||
            correlationId == Guid.Empty)
        {
            throw new ArgumentException("Heartbeat identifiers cannot be empty.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        if (requestSha256 is not { Length: 32 })
        {
            throw new ArgumentException("Heartbeat request SHA-256 must contain exactly 32 bytes.", nameof(requestSha256));
        }

        EnsureUtc(receivedAtUtc, nameof(receivedAtUtc));
        if (reportedSentAtUtc is not null)
        {
            EnsureUtc(reportedSentAtUtc.Value, nameof(reportedSentAtUtc));
        }

        if (appliedDesiredStateVersion is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(appliedDesiredStateVersion));
        }

        if (freeDiskBytes is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(freeDiskBytes));
        }

        Id = id;
        TenantId = tenantId;
        DeviceId = deviceId;
        BootId = bootId;
        Sequence = sequence;
        RequestSha256 = [.. requestSha256];
        ReportedSentAtUtc = reportedSentAtUtc;
        ReceivedAtUtc = receivedAtUtc;
        ServerObservedIp = Normalize(serverObservedIp, 64, nameof(serverObservedIp));
        if (string.IsNullOrWhiteSpace(inventoryJson) || inventoryJson.Length > 16_384)
        {
            throw new ArgumentException("Heartbeat inventory JSON is invalid.", nameof(inventoryJson));
        }

        InventoryJson = inventoryJson;
        AppliedDesiredStateVersion = appliedDesiredStateVersion;
        PlayerStateCode = Normalize(playerStateCode, 64, nameof(playerStateCode));
        FreeDiskBytes = freeDiskBytes;
        LastErrorCode = lastErrorCode is null ? null : Normalize(lastErrorCode, 64, nameof(lastErrorCode));
        CorrelationId = correlationId;
    }

    public Guid DeviceId { get; private set; }

    public Guid BootId { get; private set; }

    public long Sequence { get; private set; }

    public byte[] RequestSha256 { get; private set; } = [];

    public string? ResponseJson { get; private set; }

    public DateTimeOffset? ReportedSentAtUtc { get; private set; }

    public DateTimeOffset ReceivedAtUtc { get; private set; }

    public string ServerObservedIp { get; private set; } = string.Empty;

    public string InventoryJson { get; private set; } = "{}";

    public long? AppliedDesiredStateVersion { get; private set; }

    public string PlayerStateCode { get; private set; } = string.Empty;

    public long? FreeDiskBytes { get; private set; }

    public string? LastErrorCode { get; private set; }

    public Guid CorrelationId { get; private set; }

    public void RecordResponse(string responseJson)
    {
        if (ResponseJson is not null)
        {
            throw new InvalidOperationException("Heartbeat response has already been recorded.");
        }

        if (string.IsNullOrWhiteSpace(responseJson) || responseJson.Length > 32_768)
        {
            throw new ArgumentException("Heartbeat response JSON is invalid.", nameof(responseJson));
        }

        ResponseJson = responseJson;
    }

    private static string Normalize(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"Value cannot exceed {maximumLength} characters.", parameterName);
    }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Heartbeat timestamps must be UTC.", parameterName);
        }
    }
}
