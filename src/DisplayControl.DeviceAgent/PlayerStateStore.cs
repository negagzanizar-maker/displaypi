namespace DisplayControl.DeviceAgent;

public sealed class PlayerStateStore(TimeProvider timeProvider)
{
    private readonly object _gate = new();
    private PlayerStateSnapshot _state = new(
        "notLicensed",
        "Not licensed",
        null,
        null,
        null,
        null,
        null,
        null,
        DateTimeOffset.MinValue);
    private ActivePlayerManifest? _manifest;
    private Dictionary<Guid, PlayerAssetFile> _assetFiles =
        new Dictionary<Guid, PlayerAssetFile>();
    private DateTimeOffset? _authorizationExpiresAtUtc;
    private long? _authorizationStartedTimestamp;
    private TimeSpan? _authorizationDuration;
    private string? _manifestSha256;

    public bool RenewReady(Guid deviceId, Guid desiredStateId, long version, string manifestSha256,
        DateTimeOffset expiresAtUtc, DateTimeOffset trustedNowUtc)
    {
        lock (_gate)
        {
            ExpireAuthorizationIfRequired();
            if (_state.Status != "ready" || _state.DeviceId != deviceId ||
                _manifest?.DesiredStateId != desiredStateId || _manifest.Version != version ||
                !string.Equals(_manifestSha256, manifestSha256, StringComparison.Ordinal)) return false;
            SetAuthorization(expiresAtUtc, trustedNowUtc);
            return true;
        }
    }

    public bool ReportPlayback(PlaybackReport report)
    {
        lock (_gate)
        {
            ExpireAuthorizationIfRequired();
            if (_manifest?.DesiredStateId != report.DesiredStateId || _manifest.Version != report.Version ||
                !_assetFiles.ContainsKey(report.ContentVersionId) ||
                !(report.Status == "playing" && report.ErrorCode is null ||
                  report.Status == "error" && report.ErrorCode is "media_error" or "media_stalled")) return false;
            _state = _state with
            {
                SafeReasonCode = report.ErrorCode,
                CurrentContentVersionId = report.ContentVersionId,
                UpdatedAtUtc = timeProvider.GetUtcNow()
            };
            return true;
        }
    }

    public PlayerStateSnapshot Snapshot()
    {
        lock (_gate)
        {
            ExpireAuthorizationIfRequired();
            return _state with { AuthorizationRemainingMilliseconds = AuthorizationRemainingMilliseconds() };
        }
    }

    public PlayerManifestSnapshot? ManifestSnapshot()
    {
        lock (_gate)
        {
            ExpireAuthorizationIfRequired();
            return _manifest is null
                ? null
                : new PlayerManifestSnapshot(
                    _manifest.DesiredStateId,
                    _manifest.Version,
                    _manifest.Assets.Select(value => new PlayerManifestAssetSnapshot(
                        value.ContentVersionId,
                        value.Position,
                        value.MediaKind,
                        value.DurationMilliseconds,
                        value.LoopVideo,
                        value.CaptionContentVersionId,
                        value.IsCaption,
                        value.CaptionText,
                        $"/player/v1/assets/{value.ContentVersionId:D}")).ToArray());
        }
    }

    public bool IsReady(long desiredStateVersion)
    {
        lock (_gate)
        {
            ExpireAuthorizationIfRequired();
            return _state.Status == "ready" && _manifest?.Version == desiredStateVersion;
        }
    }

    public bool TryResolveAsset(Guid contentVersionId, out PlayerAssetFile? asset)
    {
        lock (_gate)
        {
            ExpireAuthorizationIfRequired();
            return _assetFiles.TryGetValue(contentVersionId, out asset);
        }
    }

    public IReadOnlySet<string> ActiveAssetHashesSnapshot()
    {
        lock (_gate)
        {
            ExpireAuthorizationIfRequired();
            return _assetFiles.Values.Select(value => value.Sha256).ToHashSet(StringComparer.Ordinal);
        }
    }

    public void SetNotLicensed(string? safeReasonCode = null)
    {
        lock (_gate)
        {
            _manifest = null;
            _assetFiles = new Dictionary<Guid, PlayerAssetFile>();
            _authorizationExpiresAtUtc = null;
            _authorizationStartedTimestamp = null;
            _authorizationDuration = null;
            Set("notLicensed", "Not licensed", safeReasonCode, null, null);
        }
    }

    public void SetLicensedNoContent(
        Guid deviceId,
        DateTimeOffset authorizationExpiresAtUtc,
        DateTimeOffset trustedNowUtc)
    {
        lock (_gate)
        {
            _manifest = null;
            _assetFiles = new Dictionary<Guid, PlayerAssetFile>();
            SetAuthorization(authorizationExpiresAtUtc, trustedNowUtc);
            Set("noContent", "No content assigned", null, deviceId, null);
        }
    }

    public void SetSynchronizing(
        Guid deviceId,
        long desiredStateVersion,
        DateTimeOffset authorizationExpiresAtUtc,
        DateTimeOffset trustedNowUtc)
    {
        lock (_gate)
        {
            ExpireAuthorizationIfRequired();
            // A replacement lease never extends the old manifest's authorization.
            if (_state.Status == "ready" && _state.DeviceId == deviceId) return;
            _manifest = null;
            _assetFiles = new Dictionary<Guid, PlayerAssetFile>();
            SetAuthorization(authorizationExpiresAtUtc, trustedNowUtc);
            Set("synchronizing", "Synchronizing", null, deviceId, desiredStateVersion);
        }
    }

    public void SetReady(
        Guid deviceId,
        ActivePlayerManifest manifest,
        IReadOnlyDictionary<Guid, PlayerAssetFile> assetFiles,
        DateTimeOffset authorizationExpiresAtUtc,
        DateTimeOffset trustedNowUtc,
        string? manifestSha256 = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(assetFiles);
        lock (_gate)
        {
            _manifest = manifest;
            _manifestSha256 = manifestSha256;
            _assetFiles = new Dictionary<Guid, PlayerAssetFile>(assetFiles);
            SetAuthorization(authorizationExpiresAtUtc, trustedNowUtc);
            Set("ready", "Playing", null, deviceId, manifest.Version);
        }
    }

    private void ExpireAuthorizationIfRequired()
    {
        if (_authorizationStartedTimestamp is not long startedTimestamp ||
            _authorizationDuration is not TimeSpan duration ||
            timeProvider.GetElapsedTime(startedTimestamp) < duration)
        {
            return;
        }

        _manifest = null;
        _assetFiles = new Dictionary<Guid, PlayerAssetFile>();
        _authorizationExpiresAtUtc = null;
        _authorizationStartedTimestamp = null;
        _authorizationDuration = null;
        Set("notLicensed", "Not licensed", "lease_expired", null, null);
    }

    private void SetAuthorization(DateTimeOffset authorizationExpiresAtUtc, DateTimeOffset trustedNowUtc)
    {
        if (authorizationExpiresAtUtc.Offset != TimeSpan.Zero || trustedNowUtc.Offset != TimeSpan.Zero ||
            authorizationExpiresAtUtc <= trustedNowUtc)
        {
            throw new InvalidOperationException("Player authorization must have a future UTC expiry.");
        }

        _authorizationExpiresAtUtc = authorizationExpiresAtUtc;
        _authorizationStartedTimestamp = timeProvider.GetTimestamp();
        _authorizationDuration = authorizationExpiresAtUtc - trustedNowUtc;
    }

    private long? AuthorizationRemainingMilliseconds()
    {
        if (_authorizationStartedTimestamp is not long startedTimestamp ||
            _authorizationDuration is not TimeSpan duration)
        {
            return null;
        }

        var remaining = duration - timeProvider.GetElapsedTime(startedTimestamp);
        return Math.Max(0, (long)Math.Ceiling(remaining.TotalMilliseconds));
    }

    private void Set(string status, string message, string? safeReasonCode, Guid? deviceId, long? version) =>
        _state = new PlayerStateSnapshot(
            status,
            message,
            safeReasonCode,
            deviceId,
            version,
            _authorizationExpiresAtUtc,
            AuthorizationRemainingMilliseconds(),
            null,
            timeProvider.GetUtcNow());
}

public sealed record PlaybackReport(Guid DesiredStateId, long Version, Guid ContentVersionId, string Status, string? ErrorCode);

public sealed record PlayerStateSnapshot(
    string Status,
    string Message,
    string? SafeReasonCode,
    Guid? DeviceId,
    long? DesiredStateVersion,
    DateTimeOffset? AuthorizationExpiresAtUtc,
    long? AuthorizationRemainingMilliseconds,
    Guid? CurrentContentVersionId,
    DateTimeOffset UpdatedAtUtc);

public sealed record ActivePlayerManifest(
    Guid DesiredStateId,
    long Version,
    IReadOnlyList<ActivePlayerAsset> Assets);

public sealed record ActivePlayerAsset(
    Guid ContentVersionId,
    int Position,
    string MediaKind,
    int? DurationMilliseconds,
    bool LoopVideo,
    Guid? CaptionContentVersionId = null,
    bool IsCaption = false,
    string? CaptionText = null);

public sealed record PlayerAssetFile(string Path, string ContentType, long ByteLength, string Sha256);

public sealed record PlayerManifestSnapshot(
    Guid DesiredStateId,
    long Version,
    IReadOnlyList<PlayerManifestAssetSnapshot> Assets);

public sealed record PlayerManifestAssetSnapshot(
    Guid ContentVersionId,
    int Position,
    string MediaKind,
    int? DurationMilliseconds,
    bool LoopVideo,
    Guid? CaptionContentVersionId,
    bool IsCaption,
    string? CaptionText,
    string Url);
