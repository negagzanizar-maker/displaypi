using DisplayControl.Domain.Tenancy;

namespace DisplayControl.Domain.Content;

public sealed class ContentVersion : TenantOwnedEntity
{
    private ContentVersion()
    {
    }

    public ContentVersion(
        Guid id,
        Guid tenantId,
        Guid contentAssetId,
        int versionNumber,
        string storageKey,
        long byteLength,
        byte[] sha256,
        string detectedMimeType,
        string originalDisplayFileName,
        string mediaMetadataJson,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || contentAssetId == Guid.Empty || createdByUserId == Guid.Empty)
        {
            throw new ArgumentException("Content version identifiers cannot be empty.");
        }

        if (versionNumber <= 0 || byteLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(versionNumber), "Version and byte length must be positive.");
        }

        if (sha256 is not { Length: 32 })
        {
            throw new ArgumentException("Content SHA-256 must contain exactly 32 bytes.", nameof(sha256));
        }

        EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        Id = id;
        TenantId = tenantId;
        ContentAssetId = contentAssetId;
        VersionNumber = versionNumber;
        StorageKey = Normalize(storageKey, 512, nameof(storageKey));
        ByteLength = byteLength;
        Sha256 = [.. sha256];
        DetectedMimeType = Normalize(detectedMimeType, 128, nameof(detectedMimeType));
        OriginalDisplayFileName = Normalize(originalDisplayFileName, 255, nameof(originalDisplayFileName));
        MediaMetadataJson = Normalize(mediaMetadataJson, 8192, nameof(mediaMetadataJson));
        ScanState = "pending";
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid ContentAssetId { get; private set; }

    public int VersionNumber { get; private set; }

    public string StorageKey { get; private set; } = string.Empty;

    public long ByteLength { get; private set; }

    public byte[] Sha256 { get; private set; } = [];

    public string DetectedMimeType { get; private set; } = string.Empty;

    public string OriginalDisplayFileName { get; private set; } = string.Empty;

    public string MediaMetadataJson { get; private set; } = "{}";

    public string ScanState { get; private set; } = string.Empty;

    public string? ScanEngineVersion { get; private set; }

    public string? RejectionCode { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid? ApprovedByUserId { get; private set; }

    public DateTimeOffset? ApprovedAtUtc { get; private set; }

    public void RecordScanOutcome(
        ContentScanOutcome outcome,
        string? engineVersion,
        string? rejectionCode)
    {
        ScanState = outcome switch
        {
            ContentScanOutcome.Clean => "clean",
            ContentScanOutcome.Infected => "infected",
            ContentScanOutcome.Unavailable => "unavailable",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome))
        };
        ScanEngineVersion = NormalizeOptional(engineVersion, 128, nameof(engineVersion));
        RejectionCode = outcome == ContentScanOutcome.Clean
            ? null
            : Normalize(rejectionCode ?? "scan_failed", 64, nameof(rejectionCode));
    }

    public void Approve(Guid approvedByUserId, DateTimeOffset approvedAtUtc)
    {
        if (approvedByUserId == Guid.Empty)
        {
            throw new ArgumentException("Approver identifier cannot be empty.", nameof(approvedByUserId));
        }

        EnsureUtc(approvedAtUtc, nameof(approvedAtUtc));
        if (!string.Equals(ScanState, "clean", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only a clean content version can be approved.");
        }

        ApprovedByUserId = approvedByUserId;
        ApprovedAtUtc = approvedAtUtc;
    }

    private static string Normalize(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"Value cannot exceed {maximumLength} characters.", parameterName);
    }

    private static string? NormalizeOptional(string? value, int maximumLength, string parameterName) =>
        string.IsNullOrWhiteSpace(value) ? null : Normalize(value, maximumLength, parameterName);

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must have a UTC offset.", parameterName);
        }
    }
}
