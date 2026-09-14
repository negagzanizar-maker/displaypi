using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace DisplayControl.DeviceAgent;

public sealed class AgentStateStore : IDisposable
{
    private const string StateFileName = "agent-state.json";
    private const string PrivateKeyFileName = "device-private-key.pem";
    private const string RotationPrivateKeyFileName = "device-private-key.next.pem";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly string _root;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AgentStateStore(IOptions<AgentRuntimeOptions> options)
    {
        _root = Path.GetFullPath(options.Value.StateDirectory);
        Directory.CreateDirectory(_root);
        var directory = new DirectoryInfo(_root);
        if (directory.LinkTarget is not null || directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException("The agent state directory cannot be a reparse point.");
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(_root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    public async Task<AgentPersistentState?> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var path = Path.Combine(_root, StateFileName);
            if (!File.Exists(path))
            {
                return null;
            }

            var info = new FileInfo(path);
            if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.Length > 128 * 1024)
            {
                throw new InvalidOperationException("The persisted agent state is unsafe or oversized.");
            }

            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            return await JsonSerializer.DeserializeAsync<AgentPersistentState>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidOperationException("The persisted agent state is empty.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AgentPersistentState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var target = Path.Combine(_root, StateFileName);
            var temporary = Path.Combine(_root, $".{StateFileName}.{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var stream = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }

                RestrictFile(temporary);
                File.Move(temporary, target, overwrite: true);
                RestrictFile(target);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ECDsa> LoadOrCreatePrivateKeyAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var path = Path.Combine(_root, PrivateKeyFileName);
            if (File.Exists(path))
            {
                var currentKey = await LoadPrivateKeyAsync(path, cancellationToken);
                var state = await LoadStateWithoutLockAsync(cancellationToken);
                if (state is null || KeyMatchesCertificate(currentKey, state.CertificatePem))
                {
                    return currentKey;
                }

                currentKey.Dispose();
                var rotationPath = Path.Combine(_root, RotationPrivateKeyFileName);
                if (!File.Exists(rotationPath))
                {
                    throw new CryptographicException("Persisted certificate and private key do not match.");
                }

                var rotationKey = await LoadPrivateKeyAsync(rotationPath, cancellationToken);
                if (!KeyMatchesCertificate(rotationKey, state.CertificatePem))
                {
                    rotationKey.Dispose();
                    throw new CryptographicException("Neither persisted private key matches the active certificate.");
                }

                File.Move(rotationPath, path, overwrite: true);
                RestrictFile(path);
                return rotationKey;
            }

            var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            await File.WriteAllTextAsync(path, key.ExportPkcs8PrivateKeyPem(), cancellationToken);
            RestrictFile(path);
            return key;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ECDsa> LoadOrCreateRotationPrivateKeyAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var path = Path.Combine(_root, RotationPrivateKeyFileName);
            if (File.Exists(path))
            {
                return await LoadPrivateKeyAsync(path, cancellationToken);
            }

            var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var temporary = Path.Combine(_root, $".{RotationPrivateKeyFileName}.{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllTextAsync(temporary, key.ExportPkcs8PrivateKeyPem(), cancellationToken);
                RestrictFile(temporary);
                File.Move(temporary, path, overwrite: false);
                RestrictFile(path);
                return key;
            }
            catch
            {
                key.Dispose();
                throw;
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task PromoteRotationPrivateKeyAsync(
        string certificatePem,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var rotationPath = Path.Combine(_root, RotationPrivateKeyFileName);
            var currentPath = Path.Combine(_root, PrivateKeyFileName);
            if (!File.Exists(rotationPath))
            {
                throw new CryptographicException("The pending rotation key is missing.");
            }

            using var key = await LoadPrivateKeyAsync(rotationPath, cancellationToken);
            if (!KeyMatchesCertificate(key, certificatePem))
            {
                throw new CryptographicException("The replacement certificate does not match the pending key.");
            }

            File.Move(rotationPath, currentPath, overwrite: true);
            RestrictFile(currentPath);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<AgentPersistentState?> LoadStateWithoutLockAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(_root, StateFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        return await JsonSerializer.DeserializeAsync<AgentPersistentState>(stream, JsonOptions, cancellationToken);
    }

    private static async Task<ECDsa> LoadPrivateKeyAsync(string path, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.Length > 16 * 1024)
        {
            throw new InvalidOperationException("The persisted device private key is unsafe or oversized.");
        }

        var key = ECDsa.Create();
        try
        {
            var pem = await File.ReadAllTextAsync(path, cancellationToken);
            key.ImportFromPem(pem);
            EnsureP256(key);
            return key;
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    private static bool KeyMatchesCertificate(ECDsa key, string certificatePem)
    {
        using var certificate = X509Certificate2.CreateFromPem(certificatePem);
        using var certificateKey = certificate.GetECDsaPublicKey();
        return certificateKey is not null && CryptographicOperations.FixedTimeEquals(
            key.ExportSubjectPublicKeyInfo(),
            certificateKey.ExportSubjectPublicKeyInfo());
    }

    private static void EnsureP256(ECDsa key)
    {
        var curve = key.ExportParameters(false).Curve.Oid.Value;
        if (!string.Equals(curve, ECCurve.NamedCurves.nistP256.Oid.Value, StringComparison.Ordinal))
        {
            throw new CryptographicException("The persisted device key is not ECDSA P-256.");
        }
    }

    private static void RestrictFile(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    public void Dispose() => _gate.Dispose();
}

public sealed record AgentPersistentState(
    Guid TenantId,
    Guid DeviceId,
    Guid CertificateId,
    string CertificatePem,
    string CertificateAuthorityPem,
    DateTimeOffset CertificateExpiresAtUtc,
    DisplayControl.Application.Security.LicenseLeaseVerificationKey LicenseVerificationKey,
    long LastHeartbeatSequence,
    DateTimeOffset TrustedServerTimeHighWaterUtc,
    string? CurrentLeaseToken,
    DateTimeOffset? CurrentLeaseExpiresAtUtc,
    string DesiredStateStatus,
    Guid? DesiredStateId,
    long? DesiredStateVersion,
    string? DesiredStateManifestSha256,
    long? AppliedDesiredStateVersion);
