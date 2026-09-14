using System.Data;
using Microsoft.Data.SqlClient;

namespace DisplayControl.Api.Operations;

public sealed record OperationalDataRetentionOptions(
    string DatabaseConnectionString,
    TimeSpan HeartbeatRetention,
    TimeSpan? AuditRetention,
    TimeSpan Interval,
    int BatchSize)
{
    public int MaximumBatchesPerRun { get; init; } = 100;

    public static OperationalDataRetentionOptions FromConfiguration(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("MaintenanceDatabase");
        var heartbeatDays = configuration.GetValue<int?>("Operations:Retention:HeartbeatDays") ?? 30;
        var auditDays = configuration.GetValue<int?>("Operations:Retention:AuditDays");
        var intervalMinutes = configuration.GetValue<int?>("Operations:Retention:IntervalMinutes") ?? 15;
        var batchSize = configuration.GetValue<int?>("Operations:Retention:BatchSize") ?? 1_000;
        var maximumBatches = configuration.GetValue<int?>("Operations:Retention:MaximumBatchesPerRun") ?? 100;
        if (string.IsNullOrWhiteSpace(connectionString) ||
            heartbeatDays is < 1 or > 3_650 ||
            auditDays is < 30 or > 3_650 ||
            intervalMinutes is < 5 or > 1_440 ||
            batchSize is < 100 or > 10_000 || maximumBatches is < 1 or > 1_000)
        {
            throw new InvalidOperationException(
                "Enabled retention requires a maintenance connection and valid retention, interval, and batch limits.");
        }

        var connectionBuilder = new SqlConnectionStringBuilder(connectionString);
        if (!string.Equals(connectionBuilder.UserID, "display_control_maintenance", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:MaintenanceDatabase must use the dedicated display_control_maintenance login.");
        }

        return new OperationalDataRetentionOptions(
            connectionString,
            TimeSpan.FromDays(heartbeatDays),
            auditDays is null ? null : TimeSpan.FromDays(auditDays.Value),
            TimeSpan.FromMinutes(intervalMinutes),
            batchSize)
        { MaximumBatchesPerRun = maximumBatches };
    }
}

public sealed class OperationalDataRetentionWorker(
    OperationalDataRetentionOptions options,
    TimeProvider timeProvider,
    ILogger<OperationalDataRetentionWorker> logger) : BackgroundService
{
    private static readonly Action<ILogger, int, int, Exception?> LogCompleted = LoggerMessage.Define<int, int>(
        LogLevel.Information,
        new EventId(2200, "OperationalDataRetentionCompleted"),
        "Operational retention removed {HeartbeatCount} heartbeat rows and {AuditCount} audit rows.");
    private static readonly Action<ILogger, Exception?> LogFailure = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(2201, "OperationalDataRetentionFailed"),
        "Operational retention failed safely and will retry later.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var nowUtc = timeProvider.GetUtcNow();
                var heartbeats = await DeleteExpiredBatchesAsync(
                    "device_heartbeats",
                    "received_at_utc",
                    nowUtc.Subtract(options.HeartbeatRetention),
                    stoppingToken);
                var audits = options.AuditRetention is { } auditRetention
                    ? await DeleteExpiredBatchesAsync(
                        "audit_events",
                        "occurred_at_utc",
                        nowUtc.Subtract(auditRetention),
                        stoppingToken)
                    : 0;
                LogCompleted(logger, heartbeats, audits, null);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogFailure(logger, exception);
            }

            await Task.Delay(options.Interval, timeProvider, stoppingToken);
        }
    }

    private async Task<int> DeleteExpiredBatchesAsync(
        string table,
        string timestampColumn,
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken)
    {
        var total = 0;
        var started = timeProvider.GetTimestamp();
        for (var batch = 0; batch < options.MaximumBatchesPerRun; batch++)
        {
            if (timeProvider.GetElapsedTime(started) >= TimeSpan.FromSeconds(30))
            {
                break;
            }

            var deleted = await DeleteExpiredBatchAsync(table, timestampColumn, cutoffUtc, cancellationToken);
            total += deleted;
            if (deleted < options.BatchSize)
            {
                break;
            }
        }

        return total;
    }

    private async Task<int> DeleteExpiredBatchAsync(
        string table,
        string timestampColumn,
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.DatabaseConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await using (var contextCommand = new SqlCommand(
            "EXEC sys.sp_set_session_context @key=N'data_retention', @value=1, @read_only=0",
            connection,
            transaction))
        {
            await contextCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var sql =
            $"""
             DELETE TOP (@batch_size) FROM app.{table}
             WHERE {timestampColumn} < @cutoff
             """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("cutoff", cutoffUtc);
        command.Parameters.AddWithValue("batch_size", options.BatchSize);
        var deleted = await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return deleted;
    }
}
