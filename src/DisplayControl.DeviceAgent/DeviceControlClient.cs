using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

using DisplayControl.Application.Security;
using Microsoft.Extensions.Options;

namespace DisplayControl.DeviceAgent;

public interface IDeviceSynchronizationClient
{
    public Task SynchronizeOnceAsync(CancellationToken cancellationToken);
}

public sealed class DeviceControlClient(
    HttpClient enrollmentClient,
    AgentStateStore stateStore,
    DeviceInventoryCollector inventoryCollector,
    PlayerStateStore playerState,
    ContentCacheStore contentCache,
    IOptions<AgentRuntimeOptions> options,
    TimeProvider timeProvider,
    ILogger<DeviceControlClient> logger) : IDeviceSynchronizationClient
{
    private readonly AgentRuntimeOptions _options = options.Value;
    private readonly object _clockGate = new();
    private readonly Guid _bootId = Guid.NewGuid();
    private long _heartbeatSequence;
    private HeartbeatRequestContract? _pendingHeartbeat;
    private DateTimeOffset? _lastAuthenticatedServerTimeUtc;
    private long _lastAuthenticatedTimestamp;

    public async Task SynchronizeOnceAsync(CancellationToken cancellationToken)
    {
        var state = await stateStore.LoadAsync(cancellationToken);
        if (state is null)
        {
            state = await TryEnrollAsync(cancellationToken);
            if (state is null)
            {
                playerState.SetNotLicensed("not_enrolled");
                return;
            }
        }

        if (state.CertificateExpiresAtUtc <= timeProvider.GetUtcNow())
        {
            playerState.SetNotLicensed("certificate_expired");
            return;
        }

        if (state.CertificateExpiresAtUtc - timeProvider.GetUtcNow() <= TimeSpan.FromDays(30))
        {
            try
            {
                state = await TryRotateCertificateAsync(state, cancellationToken);
            }
            catch (Exception exception) when (
                exception is HttpRequestException or IOException or TaskCanceledException)
            {
                // The still-valid current certificate remains usable until the next retry.
            }
            catch (CryptographicException)
            {
                playerState.SetNotLicensed("certificate_rotation_invalid");
                return;
            }
        }

        try
        {
            await SendHeartbeatAsync(state, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            AgentLog.HeartbeatTransportFailed(logger, SafeExceptionChain(exception));
            ApplyOfflineAuthorization(state);
        }
    }

    private static string SafeExceptionChain(Exception exception)
    {
        var names = new List<string>(4);
        for (var current = exception; current is not null && names.Count < 4; current = current.InnerException)
        {
            names.Add($"{current.GetType().Name}:0x{current.HResult:X8}");
        }

        return string.Join('>', names);
    }

    private async Task<AgentPersistentState> TryRotateCertificateAsync(
        AgentPersistentState state,
        CancellationToken cancellationToken)
    {
        using var currentPrivateKey = await stateStore.LoadOrCreatePrivateKeyAsync(cancellationToken);
        using var publicCertificate = X509Certificate2.CreateFromPem(state.CertificatePem);
        using var clientCertificate = DeviceTlsCertificate.Create(publicCertificate, currentPrivateKey);
        using var handler = new HttpClientHandler
        {
            CheckCertificateRevocationList = _options.CheckServerCertificateRevocation
        };
        handler.ClientCertificates.Add(clientCertificate);
        using var client = new HttpClient(handler)
        {
            BaseAddress = _options.ServerBaseAddress,
            Timeout = TimeSpan.FromSeconds(30)
        };
        using var rotationPrivateKey = await stateStore.LoadOrCreateRotationPrivateKeyAsync(cancellationToken);
        var signingRequest = new CertificateRequest(
            "CN=display-control-device-rotation",
            rotationPrivateKey,
            HashAlgorithmName.SHA256);
        using var response = await client.PostAsJsonAsync(
            "/device/v1/certificates/rotate",
            new CertificateRotationRequestContract(signingRequest.CreateSigningRequestPem()),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return state;
        }

        var rotated = await response.Content.ReadFromJsonAsync<CertificateRotationResponseContract>(cancellationToken)
            ?? throw new CryptographicException("The certificate rotation response was empty.");
        ValidateRotationResponse(rotated, state, rotationPrivateKey);
        var highWater = rotated.ServerTimeUtc > state.TrustedServerTimeHighWaterUtc
            ? rotated.ServerTimeUtc
            : state.TrustedServerTimeHighWaterUtc;
        var rotatedState = state with
        {
            CertificateId = rotated.CertificateId,
            CertificatePem = rotated.CertificatePem,
            CertificateAuthorityPem = rotated.CertificateAuthorityPem,
            CertificateExpiresAtUtc = rotated.CertificateExpiresAtUtc,
            TrustedServerTimeHighWaterUtc = highWater,
            CurrentLeaseToken = null,
            CurrentLeaseExpiresAtUtc = null,
            DesiredStateStatus = "notLicensed",
            DesiredStateId = null,
            DesiredStateVersion = null,
            DesiredStateManifestSha256 = null,
            AppliedDesiredStateVersion = null
        };
        await stateStore.SaveAsync(rotatedState, cancellationToken);
        await stateStore.PromoteRotationPrivateKeyAsync(rotated.CertificatePem, cancellationToken);
        UpdateTrustedClock(rotated.ServerTimeUtc);
        return rotatedState;
    }

    private async Task<AgentPersistentState?> TryEnrollAsync(CancellationToken cancellationToken)
    {
        var enrollmentCode = await LoadEnrollmentCodeAsync(cancellationToken);
        if (enrollmentCode is null)
        {
            return null;
        }

        var inventory = await inventoryCollector.CollectAsync(cancellationToken);
        using var privateKey = await stateStore.LoadOrCreatePrivateKeyAsync(cancellationToken);
        var signingRequest = new CertificateRequest(
            "CN=display-control-device-enrollment",
            privateKey,
            HashAlgorithmName.SHA256);
        using var response = await enrollmentClient.PostAsJsonAsync(
            new Uri(_options.ServerBaseAddress, "/device/v1/enrollment"),
            new EnrollmentRequestContract(
                enrollmentCode,
                signingRequest.CreateSigningRequestPem(),
                inventory.SerialNumber,
                inventory.Hostname,
                inventory.OsDescription,
                inventory.Architecture,
                inventory.AgentVersion,
                inventory.PlayerVersion,
                inventory.DiskCapacityBytes,
                inventory.NetworkInterfaces),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorCode = "unknown";
            try
            {
                await using var problemStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var problem = await JsonDocument.ParseAsync(problemStream, cancellationToken: cancellationToken);
                if (problem.RootElement.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String)
                {
                    errorCode = code.GetString() ?? errorCode;
                }
                else if (problem.RootElement.TryGetProperty("errors", out var errors) &&
                    errors.ValueKind == JsonValueKind.Object)
                {
                    errorCode = "validation_" + string.Join(
                        '_',
                        errors.EnumerateObject().Select(property => property.Name));
                }
            }
            catch (JsonException)
            {
                errorCode = "invalid_problem_response";
            }

            AgentLog.EnrollmentRejected(logger, (int)response.StatusCode, errorCode);
            return null;
        }

        var enrolled = await response.Content.ReadFromJsonAsync<EnrollmentResponseContract>(cancellationToken)
            ?? throw new InvalidOperationException("The enrollment response was empty.");
        ValidateEnrollmentResponse(enrolled, privateKey);
        var state = new AgentPersistentState(
            enrolled.TenantId,
            enrolled.DeviceId,
            enrolled.CertificateId,
            enrolled.CertificatePem,
            enrolled.CertificateAuthorityPem,
            enrolled.CertificateExpiresAtUtc,
            enrolled.LicenseVerificationKey,
            0,
            enrolled.ServerTimeUtc,
            null,
            null,
            "notLicensed",
            null,
            null,
            null,
            null);
        await stateStore.SaveAsync(state, cancellationToken);
        DeleteConsumedEnrollmentCodeFile();
        UpdateTrustedClock(enrolled.ServerTimeUtc);
        return state;
    }

    private async Task<string?> LoadEnrollmentCodeAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_options.EnrollmentCode))
        {
            return _options.EnrollmentCode;
        }

        if (string.IsNullOrWhiteSpace(_options.EnrollmentCodeFile) || !File.Exists(_options.EnrollmentCodeFile))
        {
            return null;
        }

        var information = new FileInfo(_options.EnrollmentCodeFile);
        if (information.LinkTarget is not null || information.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
            information.Length is < 32 or > 512)
        {
            throw new InvalidOperationException("The enrollment-code file is unsafe or invalid.");
        }

        var value = (await File.ReadAllTextAsync(_options.EnrollmentCodeFile, cancellationToken)).Trim();
        return value.Length is >= 32 and <= 160
            ? value
            : throw new InvalidOperationException("The enrollment code has an invalid length.");
    }

    private void DeleteConsumedEnrollmentCodeFile()
    {
        if (string.IsNullOrWhiteSpace(_options.EnrollmentCodeFile) || !File.Exists(_options.EnrollmentCodeFile))
        {
            return;
        }

        var information = new FileInfo(_options.EnrollmentCodeFile);
        if (information.LinkTarget is not null || information.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException("The enrollment-code file became unsafe.");
        }

        File.Delete(_options.EnrollmentCodeFile);
    }

    private async Task SendHeartbeatAsync(AgentPersistentState state, CancellationToken cancellationToken)
    {
        var inventory = await inventoryCollector.CollectAsync(cancellationToken);
        using var privateKey = await stateStore.LoadOrCreatePrivateKeyAsync(cancellationToken);
        using var publicCertificate = X509Certificate2.CreateFromPem(state.CertificatePem);
        using var clientCertificate = DeviceTlsCertificate.Create(publicCertificate, privateKey);
        using var handler = new HttpClientHandler
        {
            CheckCertificateRevocationList = _options.CheckServerCertificateRevocation
        };
        handler.ClientCertificates.Add(clientCertificate);
        using var client = new HttpClient(handler)
        {
            BaseAddress = _options.ServerBaseAddress,
            Timeout = TimeSpan.FromSeconds(30)
        };
        var sequence = _pendingHeartbeat?.Sequence ?? checked(_heartbeatSequence + 1);
        var playerSnapshot = playerState.Snapshot();
        var heartbeatRequest = _pendingHeartbeat ?? new HeartbeatRequestContract(
            _bootId,
            sequence,
            timeProvider.GetUtcNow(),
            inventory.Hostname,
            inventory.OsDescription,
            inventory.Architecture,
            inventory.AgentVersion,
            inventory.PlayerVersion,
            inventory.DiskCapacityBytes,
            inventory.FreeDiskBytes,
            state.AppliedDesiredStateVersion,
            playerSnapshot.Status,
            playerSnapshot.SafeReasonCode,
            playerSnapshot.CurrentContentVersionId,
            inventory.NetworkInterfaces);
        _pendingHeartbeat = heartbeatRequest;
        AgentLog.HeartbeatStarted(logger, sequence);
        using var response = await client.PostAsJsonAsync(
            "/device/v1/heartbeats",
            heartbeatRequest,
            cancellationToken);
        AgentLog.HeartbeatCompleted(logger, sequence, (int)response.StatusCode);
        response.EnsureSuccessStatusCode();
        var heartbeat = await response.Content.ReadFromJsonAsync<HeartbeatResponseContract>(cancellationToken)
            ?? throw new InvalidOperationException("The heartbeat response was empty.");
        _heartbeatSequence = sequence;
        _pendingHeartbeat = null;
        if (heartbeat.ServerTimeUtc.Offset != TimeSpan.Zero ||
            heartbeat.ServerTimeUtc < state.TrustedServerTimeHighWaterUtc.AddMinutes(-2))
        {
            playerState.SetNotLicensed("server_time_rollback");
            return;
        }

        UpdateTrustedClock(heartbeat.ServerTimeUtc);
        var highWater = heartbeat.ServerTimeUtc > state.TrustedServerTimeHighWaterUtc
            ? heartbeat.ServerTimeUtc
            : state.TrustedServerTimeHighWaterUtc;
        if (!string.Equals(heartbeat.LicenseStatus, "licensed", StringComparison.Ordinal))
        {
            var deniedState = state with
            {
                LastHeartbeatSequence = sequence,
                TrustedServerTimeHighWaterUtc = highWater,
                CurrentLeaseToken = null,
                CurrentLeaseExpiresAtUtc = null,
                DesiredStateStatus = "notLicensed",
                DesiredStateId = null,
                DesiredStateVersion = null,
                DesiredStateManifestSha256 = null,
                AppliedDesiredStateVersion = null
            };
            await stateStore.SaveAsync(deniedState, cancellationToken);
            playerState.SetNotLicensed("license_denied");
            return;
        }

        var verificationKey = SelectAuthenticatedVerificationKey(heartbeat, state.LicenseVerificationKey);
        if (heartbeat.Lease is null || verificationKey is null || !MatchesPinnedKey(heartbeat.Lease, verificationKey) ||
            !LicenseLeaseTokenCodec.TryValidate(
                heartbeat.Lease.Token,
                verificationKey,
                state.TenantId,
                state.DeviceId,
                state.CertificateId,
                heartbeat.ServerTimeUtc,
                out var leasePayload) ||
            !DesiredStateMatches(heartbeat.DesiredState, leasePayload))
        {
            playerState.SetNotLicensed("lease_invalid");
            return;
        }

        var authorizedState = state with
        {
            LicenseVerificationKey = verificationKey,
            LastHeartbeatSequence = sequence,
            TrustedServerTimeHighWaterUtc = highWater,
            CurrentLeaseToken = heartbeat.Lease.Token,
            CurrentLeaseExpiresAtUtc = heartbeat.Lease.ExpiresAtUtc,
            DesiredStateStatus = heartbeat.DesiredState.Status,
            DesiredStateId = heartbeat.DesiredState.Id,
            DesiredStateVersion = heartbeat.DesiredState.Version,
            DesiredStateManifestSha256 = leasePayload!.DesiredStateManifestSha256
        };
        await stateStore.SaveAsync(authorizedState, cancellationToken);
        if (string.Equals(authorizedState.DesiredStateStatus, "licensedNoContent", StringComparison.Ordinal))
        {
            var noContentState = authorizedState with { AppliedDesiredStateVersion = null };
            await stateStore.SaveAsync(noContentState, cancellationToken);
            playerState.SetLicensedNoContent(
                noContentState.DeviceId,
                leasePayload!.ExpiresAtUtc,
                heartbeat.ServerTimeUtc);
            return;
        }

        if (authorizedState.DesiredStateId is not Guid desiredStateId ||
            authorizedState.DesiredStateVersion is not long desiredStateVersion ||
            authorizedState.DesiredStateManifestSha256 is not string manifestSha256)
        {
            playerState.SetNotLicensed("desired_state_invalid");
            return;
        }

        if (playerState.RenewReady(authorizedState.DeviceId, desiredStateId, desiredStateVersion,
                manifestSha256, leasePayload!.ExpiresAtUtc, heartbeat.ServerTimeUtc))
        {
            await stateStore.SaveAsync(authorizedState with { AppliedDesiredStateVersion = desiredStateVersion }, cancellationToken);
            return;
        }

        var protectedCacheHashes = playerState.ActiveAssetHashesSnapshot();
        playerState.SetSynchronizing(
            authorizedState.DeviceId,
            desiredStateVersion,
            leasePayload!.ExpiresAtUtc,
            heartbeat.ServerTimeUtc);
        try
        {
            var activation = await contentCache.SynchronizeAsync(
                client,
                desiredStateId,
                desiredStateVersion,
                manifestSha256,
                protectedCacheHashes,
                cancellationToken);
            if (!TryGetTrustedTime(out var activationTimeUtc) || activationTimeUtc >= leasePayload!.ExpiresAtUtc)
            {
                playerState.SetNotLicensed("lease_expired_during_synchronization");
                return;
            }

            playerState.SetReady(
                authorizedState.DeviceId,
                activation.Manifest,
                activation.AssetFiles,
                leasePayload.ExpiresAtUtc,
                activationTimeUtc,
                manifestSha256);
            await stateStore.SaveAsync(
                authorizedState with { AppliedDesiredStateVersion = desiredStateVersion },
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            SetSynchronizingIfAuthorized(authorizedState.DeviceId, desiredStateVersion, leasePayload!.ExpiresAtUtc);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or CryptographicException)
        {
            SetSynchronizingIfAuthorized(authorizedState.DeviceId, desiredStateVersion, leasePayload!.ExpiresAtUtc);
        }
    }

    private void ApplyOfflineAuthorization(AgentPersistentState state)
    {
        if (!TryGetTrustedTime(out var trustedNowUtc) ||
            state.CurrentLeaseToken is null ||
            !LicenseLeaseTokenCodec.TryValidate(
                state.CurrentLeaseToken,
                state.LicenseVerificationKey,
                state.TenantId,
                state.DeviceId,
                state.CertificateId,
                trustedNowUtc,
            out var payload) ||
            !PersistentStateMatchesLease(state, payload))
        {
            playerState.SetNotLicensed("offline_authorization_unavailable");
            return;
        }

        ApplyAuthorizedPresentation(state, payload!.ExpiresAtUtc, trustedNowUtc);
    }

    private void ApplyAuthorizedPresentation(
        AgentPersistentState state,
        DateTimeOffset authorizationExpiresAtUtc,
        DateTimeOffset trustedNowUtc)
    {
        if (string.Equals(state.DesiredStateStatus, "licensedNoContent", StringComparison.Ordinal))
        {
            playerState.SetLicensedNoContent(state.DeviceId, authorizationExpiresAtUtc, trustedNowUtc);
        }
        else if (state.DesiredStateVersion is long version)
        {
            if (!playerState.IsReady(version) || state.AppliedDesiredStateVersion != version)
            {
                playerState.SetSynchronizing(
                    state.DeviceId,
                    version,
                    authorizationExpiresAtUtc,
                    trustedNowUtc);
            }
        }
        else
        {
            playerState.SetNotLicensed("desired_state_invalid");
        }
    }

    private void SetSynchronizingIfAuthorized(
        Guid deviceId,
        long desiredStateVersion,
        DateTimeOffset authorizationExpiresAtUtc)
    {
        if (!TryGetTrustedTime(out var trustedNowUtc) || trustedNowUtc >= authorizationExpiresAtUtc)
        {
            playerState.SetNotLicensed("lease_expired_during_synchronization");
            return;
        }

        playerState.SetSynchronizing(
            deviceId,
            desiredStateVersion,
            authorizationExpiresAtUtc,
            trustedNowUtc);
    }

    private static bool MatchesPinnedKey(
        LeaseContract lease,
        LicenseLeaseVerificationKey pinnedKey) =>
        string.Equals(lease.KeyId, pinnedKey.KeyId, StringComparison.Ordinal) &&
        string.Equals(lease.Algorithm, pinnedKey.Algorithm, StringComparison.Ordinal) &&
        string.Equals(lease.SubjectPublicKeyInfoPem, pinnedKey.SubjectPublicKeyInfoPem, StringComparison.Ordinal);

    internal static LicenseLeaseVerificationKey? SelectAuthenticatedVerificationKey(
        HeartbeatResponseContract heartbeat, LicenseLeaseVerificationKey pinnedKey)
    {
        // This response arrived over validated server TLS using the enrolled mTLS identity.
        // Persist the selected key atomically with its validated lease; offline trust never changes.
        if (heartbeat.LicenseVerificationKeys is null) return pinnedKey;
        if (heartbeat.LicenseVerificationKeys.Count is < 1 or > 4) return null;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var candidate in heartbeat.LicenseVerificationKeys)
            {
                if (candidate is null || candidate.Algorithm != "ES256" ||
                    candidate.SubjectPublicKeyInfoPem is not { Length: <= 1024 } || !ids.Add(candidate.KeyId)) return null;
                using var key = ECDsa.Create();
                key.ImportFromPem(candidate.SubjectPublicKeyInfoPem);
                var digest = SHA256.HashData(key.ExportSubjectPublicKeyInfo());
                if (key.KeySize != 256 || key.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7" ||
                    candidate.KeyId != Convert.ToHexStringLower(digest.AsSpan(0, 16))) return null;
            }
            return heartbeat.LicenseVerificationKeys.SingleOrDefault(key => key.KeyId == heartbeat.Lease?.KeyId);
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            return null;
        }
    }

    private static bool DesiredStateMatches(
        DesiredStateContract desiredState,
        LicenseLeasePayload? payload) => payload is not null &&
        ((string.Equals(desiredState.Status, "licensedNoContent", StringComparison.Ordinal) &&
            desiredState.Id is null && desiredState.Version is null &&
            payload.DesiredStateId is null && payload.DesiredStateVersion is null &&
            payload.DesiredStateManifestSha256 is null) ||
         (string.Equals(desiredState.Status, "available", StringComparison.Ordinal) &&
            desiredState.Id == payload.DesiredStateId && desiredState.Version == payload.DesiredStateVersion &&
            payload.DesiredStateManifestSha256 is { Length: 64 }));

    private static bool PersistentStateMatchesLease(
        AgentPersistentState state,
        LicenseLeasePayload? payload) => payload is not null &&
        state.DesiredStateId == payload.DesiredStateId &&
        state.DesiredStateVersion == payload.DesiredStateVersion &&
        string.Equals(
            state.DesiredStateManifestSha256,
            payload.DesiredStateManifestSha256,
            StringComparison.Ordinal);

    private static void ValidateEnrollmentResponse(EnrollmentResponseContract response, ECDsa privateKey)
    {
        if (response.TenantId == Guid.Empty || response.DeviceId == Guid.Empty || response.CertificateId == Guid.Empty ||
            response.ServerTimeUtc.Offset != TimeSpan.Zero || response.CertificateExpiresAtUtc <= response.ServerTimeUtc)
        {
            throw new CryptographicException("The enrollment bindings are invalid.");
        }

        using var certificate = X509Certificate2.CreateFromPem(response.CertificatePem);
        using var authority = X509Certificate2.CreateFromPem(response.CertificateAuthorityPem);
        using var issuedPublicKey = certificate.GetECDsaPublicKey()
            ?? throw new CryptographicException("The enrolled certificate is not ECDSA.");
        if (!CryptographicOperations.FixedTimeEquals(
                issuedPublicKey.ExportSubjectPublicKeyInfo(),
                privateKey.ExportSubjectPublicKeyInfo()))
        {
            throw new CryptographicException("The enrolled certificate is bound to another private key.");
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(authority);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.DisableCertificateDownloads = true;
        chain.ChainPolicy.VerificationTime = response.ServerTimeUtc.UtcDateTime;
        if (!chain.Build(certificate))
        {
            throw new CryptographicException("The enrolled certificate chain is invalid.");
        }

        using var leasePublicKey = ECDsa.Create();
        leasePublicKey.ImportFromPem(response.LicenseVerificationKey.SubjectPublicKeyInfoPem);
        if (leasePublicKey.KeySize != 256 ||
            !string.Equals(response.LicenseVerificationKey.Algorithm, "ES256", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(response.LicenseVerificationKey.KeyId))
        {
            throw new CryptographicException("The licence verification key is invalid.");
        }
    }

    private static void ValidateRotationResponse(
        CertificateRotationResponseContract response,
        AgentPersistentState currentState,
        ECDsa rotationPrivateKey)
    {
        if (response.CertificateId == Guid.Empty || response.ServerTimeUtc.Offset != TimeSpan.Zero ||
            response.CertificateExpiresAtUtc <= response.ServerTimeUtc ||
            response.PreviousCertificateAcceptedUntilUtc <= response.ServerTimeUtc ||
            response.PreviousCertificateAcceptedUntilUtc > response.ServerTimeUtc.AddHours(24))
        {
            throw new CryptographicException("Certificate rotation bindings are invalid.");
        }

        using var certificate = X509Certificate2.CreateFromPem(response.CertificatePem);
        using var responseAuthority = X509Certificate2.CreateFromPem(response.CertificateAuthorityPem);
        using var pinnedAuthority = X509Certificate2.CreateFromPem(currentState.CertificateAuthorityPem);
        using var issuedPublicKey = certificate.GetECDsaPublicKey()
            ?? throw new CryptographicException("The rotated certificate is not ECDSA.");
        if (!CryptographicOperations.FixedTimeEquals(responseAuthority.RawData, pinnedAuthority.RawData) ||
            !CryptographicOperations.FixedTimeEquals(
                issuedPublicKey.ExportSubjectPublicKeyInfo(),
                rotationPrivateKey.ExportSubjectPublicKeyInfo()))
        {
            throw new CryptographicException("The rotated certificate authority or public key changed unexpectedly.");
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(pinnedAuthority);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.DisableCertificateDownloads = true;
        chain.ChainPolicy.VerificationTime = response.ServerTimeUtc.UtcDateTime;
        if (!chain.Build(certificate))
        {
            throw new CryptographicException("The rotated certificate chain is invalid.");
        }
    }

    private void UpdateTrustedClock(DateTimeOffset serverTimeUtc)
    {
        lock (_clockGate)
        {
            _lastAuthenticatedServerTimeUtc = serverTimeUtc;
            _lastAuthenticatedTimestamp = timeProvider.GetTimestamp();
        }
    }

    private bool TryGetTrustedTime(out DateTimeOffset trustedNowUtc)
    {
        lock (_clockGate)
        {
            if (_lastAuthenticatedServerTimeUtc is null)
            {
                trustedNowUtc = default;
                return false;
            }

            trustedNowUtc = _lastAuthenticatedServerTimeUtc.Value.Add(
                timeProvider.GetElapsedTime(_lastAuthenticatedTimestamp));
            return true;
        }
    }
}

internal sealed record EnrollmentRequestContract(
    string EnrollmentCode,
    string CertificateSigningRequestPem,
    string SerialNumber,
    string Hostname,
    string OsDescription,
    string Architecture,
    string AgentVersion,
    string PlayerVersion,
    long DiskCapacityBytes,
    IReadOnlyList<DeviceNetworkSnapshot> NetworkInterfaces);

internal sealed record EnrollmentResponseContract(
    Guid TenantId,
    Guid DeviceId,
    Guid CertificateId,
    string CertificatePem,
    string CertificateAuthorityPem,
    LicenseLeaseVerificationKey LicenseVerificationKey,
    DateTimeOffset CertificateExpiresAtUtc,
    DateTimeOffset ServerTimeUtc,
    Uri HeartbeatEndpoint);

internal sealed record HeartbeatRequestContract(
    Guid BootId,
    long Sequence,
    DateTimeOffset ReportedSentAtUtc,
    string Hostname,
    string OsDescription,
    string Architecture,
    string AgentVersion,
    string PlayerVersion,
    long DiskCapacityBytes,
    long FreeDiskBytes,
    long? AppliedDesiredStateVersion,
    string PlayerStateCode,
    string? LastErrorCode,
    Guid? CurrentContentVersionId,
    IReadOnlyList<DeviceNetworkSnapshot> NetworkInterfaces);

internal sealed record HeartbeatResponseContract(
    DateTimeOffset ServerTimeUtc,
    int RetryAfterSeconds,
    string LicenseStatus,
    LeaseContract? Lease,
    DateTimeOffset? LicenseExpiresAtUtc,
    DesiredStateContract DesiredState,
    IReadOnlyList<LicenseLeaseVerificationKey>? LicenseVerificationKeys = null);

internal sealed record LeaseContract(
    string Token,
    string KeyId,
    DateTimeOffset ExpiresAtUtc,
    string Algorithm,
    string SubjectPublicKeyInfoPem);

internal sealed record DesiredStateContract(string Status, Guid? Id, long? Version);

internal sealed record CertificateRotationRequestContract(string CertificateSigningRequestPem);

internal sealed record CertificateRotationResponseContract(
    Guid CertificateId,
    string CertificatePem,
    string CertificateAuthorityPem,
    DateTimeOffset CertificateExpiresAtUtc,
    DateTimeOffset PreviousCertificateAcceptedUntilUtc,
    DateTimeOffset ServerTimeUtc);
