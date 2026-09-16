using Microsoft.Extensions.Options;

namespace DisplayControl.DeviceAgent;

public sealed class Worker(
    ILogger<Worker> logger,
    IDeviceSynchronizationClient controlClient,
    AgentSynchronizationSignal synchronizationSignal,
    PlayerStateStore playerState,
    AgentHealthStore health,
    IOptions<AgentRuntimeOptions> runtimeOptions) : BackgroundService
{
    private readonly AgentRuntimeOptions _runtimeOptions = runtimeOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        AgentLog.Started(logger);
        playerState.SetNotLicensed("startup_fail_closed");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await controlClient.SynchronizeOnceAsync(stoppingToken);
                health.RecordCycle(true);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                health.RecordCycle(false);
                playerState.SetNotLicensed("agent_cycle_failed");
                AgentLog.CycleFailed(logger, exception.GetType().Name);
            }

            var jitterMilliseconds = Random.Shared.Next(0, 2001);
            var delay = TimeSpan.FromSeconds(_runtimeOptions.HeartbeatIntervalSeconds)
                .Add(TimeSpan.FromMilliseconds(jitterMilliseconds));
            await synchronizationSignal.WaitForSignalOrTimeoutAsync(delay, stoppingToken);
        }
    }
}

internal static partial class AgentLog
{
    [LoggerMessage(1000, LogLevel.Information, "Display agent started in fail-closed mode.")]
    public static partial void Started(ILogger logger);

    [LoggerMessage(1001, LogLevel.Warning, "The device synchronization cycle failed with safe error type {ErrorType}.")]
    public static partial void CycleFailed(ILogger logger, string errorType);

    [LoggerMessage(1002, LogLevel.Warning, "Device enrollment was rejected with HTTP {StatusCode} and code {ErrorCode}.")]
    public static partial void EnrollmentRejected(ILogger logger, int statusCode, string errorCode);

    [LoggerMessage(1003, LogLevel.Information, "Sending device heartbeat sequence {Sequence}.")]
    public static partial void HeartbeatStarted(ILogger logger, long sequence);

    [LoggerMessage(1004, LogLevel.Information, "Device heartbeat sequence {Sequence} completed with HTTP {StatusCode}.")]
    public static partial void HeartbeatCompleted(ILogger logger, long sequence, int statusCode);

    [LoggerMessage(1005, LogLevel.Warning, "Device heartbeat transport failed with safe error type {ErrorType}.")]
    public static partial void HeartbeatTransportFailed(ILogger logger, string errorType);

    [LoggerMessage(1006, LogLevel.Information, "Connected to the authenticated device state-change stream.")]
    public static partial void StateChangeStreamConnected(ILogger logger);

    [LoggerMessage(1007, LogLevel.Warning, "The device state-change stream disconnected with safe error type {ErrorType}.")]
    public static partial void StateChangeStreamFailed(ILogger logger, string errorType);
}
