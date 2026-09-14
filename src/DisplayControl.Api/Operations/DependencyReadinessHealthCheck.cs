using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using DisplayControl.Api.Notifications;
using DisplayControl.Application.Content;
using DisplayControl.Application.Security;
using DisplayControl.Application.Storage;
using DisplayControl.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DisplayControl.Api.Operations;

public interface IReadinessDependency
{
    public string Name { get; }

    public Task<bool> IsReadyAsync(CancellationToken cancellationToken);
}

public sealed record ReadinessProbeOptions(
    TimeSpan HealthyCacheDuration,
    TimeSpan UnhealthyCacheDuration)
{
    public static ReadinessProbeOptions Default { get; } = new(
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(5));

    public bool IsValid() =>
        HealthyCacheDuration > TimeSpan.Zero &&
        UnhealthyCacheDuration > TimeSpan.Zero;
}

public sealed class CachedReadinessProbe : IDisposable
{
    private readonly IReadinessDependency[] _dependencies;
    private readonly ReadinessProbeOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HealthCheckResult? _cachedResult;
    private long _cachedAtTimestamp;

    public CachedReadinessProbe(
        IEnumerable<IReadinessDependency> dependencies,
        ReadinessProbeOptions options,
        TimeProvider timeProvider)
    {
        if (!options.IsValid())
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Readiness cache durations must be positive.");
        }

        _dependencies = dependencies.ToArray();
        _options = options;
        _timeProvider = timeProvider;
    }

    public async Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        if (TryGetCached(out var cached))
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (TryGetCached(out cached))
            {
                return cached;
            }

            var result = await ProbeDependenciesAsync(cancellationToken);
            _cachedResult = result;
            _cachedAtTimestamp = _timeProvider.GetTimestamp();
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool TryGetCached(out HealthCheckResult result)
    {
        if (_cachedResult is { } cached)
        {
            var duration = cached.Status == HealthStatus.Healthy
                ? _options.HealthyCacheDuration
                : _options.UnhealthyCacheDuration;
            if (_timeProvider.GetElapsedTime(_cachedAtTimestamp) < duration)
            {
                result = cached;
                return true;
            }
        }

        result = default;
        return false;
    }

    private async Task<HealthCheckResult> ProbeDependenciesAsync(CancellationToken cancellationToken)
    {
        try
        {
            foreach (var dependency in _dependencies)
            {
                if (!await dependency.IsReadyAsync(cancellationToken))
                {
                    return HealthCheckResult.Unhealthy("A required dependency is unavailable.");
                }
            }

            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("A required dependency is unavailable.");
        }
    }

    public void Dispose() => _gate.Dispose();
}

public sealed class DependencyReadinessHealthCheck(CachedReadinessProbe readinessProbe) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        readinessProbe.CheckAsync(cancellationToken);
}

public sealed class DatabaseReadinessDependency(IServiceScopeFactory scopeFactory) : IReadinessDependency
{
    public string Name => "database";

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DisplayControlDbContext>();
        if (!await dbContext.Database.CanConnectAsync(cancellationToken))
        {
            return false;
        }

        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            """
            SELECT CONVERT(bit, CASE
                WHEN IS_SRVROLEMEMBER(N'sysadmin') = 1 THEN 0
                WHEN IS_ROLEMEMBER(N'db_owner') = 1 THEN 0
                ELSE 1
            END)
            """;
        if (command.Connection?.State != System.Data.ConnectionState.Open)
        {
            await command.Connection!.OpenAsync(cancellationToken);
        }

        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }
}

public sealed class PrivateStorageReadinessDependency(IPrivateObjectStore objectStore) : IReadinessDependency
{
    private static readonly Guid ReadinessStorageTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public string Name => "private-storage";

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        var storageKey = new PrivateObjectKey(ReadinessStorageTenantId, Guid.NewGuid());
        try
        {
            await objectStore.PutAsync(storageKey, Stream.Null, 0, cancellationToken);
            return await objectStore.ExistsAsync(storageKey, cancellationToken);
        }
        finally
        {
            await objectStore.DeleteAsync(storageKey, cancellationToken);
        }
    }
}

public sealed class CryptographicReadinessDependency(
    ILicenseLeaseSigner leaseSigner,
    IDeviceCertificateIssuer certificateIssuer,
    TimeProvider timeProvider) : IReadinessDependency
{
    public string Name => "cryptography";

    public Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var verificationKey = leaseSigner.VerificationKey;
        if (!string.Equals(verificationKey.Algorithm, "ES256", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(verificationKey.KeyId) ||
            string.IsNullOrWhiteSpace(verificationKey.SubjectPublicKeyInfoPem))
        {
            return Task.FromResult(false);
        }

        using var certificateAuthority = X509Certificate2.CreateFromPem(certificateIssuer.CertificateAuthorityPem);
        var nowUtc = timeProvider.GetUtcNow();
        var isReady = nowUtc >= certificateAuthority.NotBefore.ToUniversalTime() &&
            nowUtc < certificateAuthority.NotAfter.ToUniversalTime() &&
            certificateAuthority.Extensions.OfType<X509BasicConstraintsExtension>()
                .Any(extension => extension.CertificateAuthority);
        return Task.FromResult(isReady);
    }
}

public sealed class MalwareScannerReadinessDependency(IContentMalwareScanner malwareScanner) : IReadinessDependency
{
    public string Name => "malware-scanner";

    public Task<bool> IsReadyAsync(CancellationToken cancellationToken) =>
        malwareScanner.IsReadyAsync(cancellationToken);
}

public sealed class NotificationReadinessDependency(IServiceProvider serviceProvider) : IReadinessDependency
{
    public string Name => "notification-delivery";

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        if (serviceProvider.GetService<NotificationDeliveryOptions>() is not { } options)
        {
            return true;
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await using var connection = new SqlConnection(options.DatabaseConnectionString);
            await connection.OpenAsync(timeoutSource.Token);
            await using (var command = new SqlCommand("SELECT 1", connection))
            {
                await command.ExecuteScalarAsync(timeoutSource.Token);
            }

            using var smtp = new TcpClient();
            await smtp.ConnectAsync(options.SmtpHost, options.SmtpPort, timeoutSource.Token);
            return smtp.Connected;
        }
        catch (Exception exception) when (
            exception is SqlException or SocketException or IOException or OperationCanceledException)
        {
            return false;
        }
    }
}
