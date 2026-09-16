using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;

namespace DisplayControl.DeviceAgent;

public sealed class DeviceStateChangeListener(
    AgentStateStore stateStore,
    AgentSynchronizationSignal synchronizationSignal,
    IOptions<AgentRuntimeOptions> options,
    TimeProvider timeProvider,
    ILogger<DeviceStateChangeListener> logger) : BackgroundService
{
    private readonly AgentRuntimeOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retryDelay = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var connected = await ListenOnceAsync(stoppingToken);
                retryDelay = connected ? TimeSpan.FromSeconds(1) : NextDelay(retryDelay);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException exception)
            {
                AgentLog.StateChangeStreamFailed(logger, exception.GetType().Name);
                retryDelay = NextDelay(retryDelay);
            }
            catch (Exception exception) when (
                exception is HttpRequestException or IOException or CryptographicException)
            {
                AgentLog.StateChangeStreamFailed(logger, exception.GetType().Name);
                retryDelay = NextDelay(retryDelay);
            }

            try
            {
                await Task.Delay(retryDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task<bool> ListenOnceAsync(CancellationToken cancellationToken)
    {
        var state = await stateStore.LoadAsync(cancellationToken);
        if (state is null || state.CertificateExpiresAtUtc <= timeProvider.GetUtcNow())
        {
            return false;
        }

        using var privateKey = await stateStore.LoadOrCreatePrivateKeyAsync(cancellationToken);
        using var publicCertificate = X509Certificate2.CreateFromPem(state.CertificatePem);
        using var clientCertificate = DeviceTlsCertificate.Create(publicCertificate, privateKey);
        using var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                ClientCertificates = new X509CertificateCollection { clientCertificate },
                CertificateRevocationCheckMode = _options.CheckServerCertificateRevocation
                    ? X509RevocationMode.Online
                    : X509RevocationMode.NoCheck
            }
        };
        using var client = new HttpClient(handler)
        {
            BaseAddress = _options.ServerBaseAddress,
            Timeout = Timeout.InfiniteTimeSpan
        };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/device/v1/state-changes");
        request.Headers.Accept.ParseAdd("text/event-stream");
        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectTimeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            connectTimeout.Token);
        connectTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
        response.EnsureSuccessStatusCode();
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "text/event-stream", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The state-change stream returned an unexpected content type.");
        }

        AgentLog.StateChangeStreamConnected(logger);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                return true;
            }

            if (string.Equals(line, "event: stateChanged", StringComparison.Ordinal))
            {
                synchronizationSignal.RequestSynchronization();
            }
        }

        return true;
    }

    private static TimeSpan NextDelay(TimeSpan current) =>
        TimeSpan.FromSeconds(Math.Min(30, Math.Max(1, current.TotalSeconds * 2)));
}
