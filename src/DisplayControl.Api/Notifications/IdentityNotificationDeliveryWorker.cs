using System.Data;
using System.Net;
using System.Net.Mail;

using DisplayControl.Application.Security;
using Microsoft.Data.SqlClient;

namespace DisplayControl.Api.Notifications;

public sealed class IdentityNotificationDeliveryWorker(
    NotificationDeliveryOptions options,
    ISensitivePayloadProtector payloadProtector,
    TimeProvider timeProvider,
    ILogger<IdentityNotificationDeliveryWorker> logger) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> LogLoopFailure = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(2100, "NotificationDeliveryLoopFailure"),
        "Identity notification delivery loop failed safely.");
    private static readonly Action<ILogger, Guid, string, int, Exception?> LogDeliveryFailure = LoggerMessage.Define<Guid, string, int>(
        LogLevel.Warning,
        new EventId(2101, "NotificationDeliveryFailure"),
        "Identity notification {NotificationId} failed with {SafeErrorCode} on attempt {Attempt}.");
    private static readonly Action<ILogger, Guid, int, Exception?> LogDeliveryAbandoned = LoggerMessage.Define<Guid, int>(
        LogLevel.Error,
        new EventId(2102, "NotificationDeliveryAbandoned"),
        "Identity notification {NotificationId} reached the terminal failure state after {AttemptCount} attempts.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delivered = false;
            try
            {
                delivered = await DeliverNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogLoopFailure(logger, exception);
            }

            await Task.Delay(delivered ? TimeSpan.FromMilliseconds(250) : TimeSpan.FromSeconds(15), stoppingToken);
        }
    }

    private async Task<bool> DeliverNextAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.DatabaseConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await using (var contextCommand = new SqlCommand(
            "EXEC sys.sp_set_session_context @key=N'notification_delivery', @value=1, @read_only=0",
            connection,
            transaction))
        {
            await contextCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        const string selectSql =
            """
            SELECT TOP (1) id, notification_type, normalized_recipient_email, protection_scheme, protected_payload, attempt_count
            FROM app.identity_notifications WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE processed_at_utc IS NULL
              AND failed_at_utc IS NULL
              AND next_attempt_at_utc <= @now
            ORDER BY created_at_utc, id

            """;
        NotificationRow? row = null;
        await using (var select = new SqlCommand(selectSql, connection, transaction))
        {
            select.Parameters.AddWithValue("now", timeProvider.GetUtcNow());
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                row = new NotificationRow(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    (byte[])reader[4],
                    reader.GetInt32(5));
            }
        }

        if (row is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        string? safeErrorCode = null;
        try
        {
            if (!string.Equals(row.ProtectionScheme, payloadProtector.ProtectionScheme, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Unsupported notification protection scheme.");
            }

            var plaintext = payloadProtector.Unprotect(row.ProtectedPayload);
            var mail = row.NotificationType switch
            {
                "PasswordResetRequested" => PasswordResetMessageFactory.Create(
                    options.PublicBaseUri,
                    row.RecipientEmail,
                    plaintext),
                "InvitationCreated" => InvitationMessageFactory.Create(
                    options.PublicBaseUri,
                    row.RecipientEmail,
                    plaintext),
                _ => throw new InvalidOperationException("Unsupported notification type.")
            };
            await SendAsync(mail, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            safeErrorCode = "smtp_delivery_timeout";
            LogDeliveryFailure(
                logger,
                row.Id,
                safeErrorCode,
                row.AttemptCount + 1,
                null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            safeErrorCode = exception switch
            {
                SmtpException => "smtp_delivery_failed",
                InvalidOperationException => "notification_payload_invalid",
                _ => "notification_delivery_failed"
            };
            LogDeliveryFailure(
                logger,
                row.Id,
                safeErrorCode,
                row.AttemptCount + 1,
                null);
        }

        var nowUtc = timeProvider.GetUtcNow();
        var decision = NotificationDeliveryAttemptPolicy.Evaluate(
            row.AttemptCount,
            safeErrorCode is null,
            options.MaximumAttempts,
            nowUtc);
        if (decision.IsTerminalFailure)
        {
            LogDeliveryAbandoned(logger, row.Id, decision.AttemptCount, null);
        }

        const string updateSql =
            """
            UPDATE app.identity_notifications
            SET processed_at_utc = CASE WHEN @safe_error_code IS NULL THEN @now ELSE NULL END,
                failed_at_utc = CASE WHEN @is_terminal_failure THEN @now ELSE NULL END,
                next_attempt_at_utc = CASE
                    WHEN @safe_error_code IS NULL OR @is_terminal_failure THEN next_attempt_at_utc
                    ELSE @next_attempt
                END,
                attempt_count = attempt_count + 1,
                last_safe_error_code = @safe_error_code,
                concurrency_token = @concurrency_token
            WHERE id = @id
            """;
        await using (var update = new SqlCommand(updateSql, connection, transaction))
        {
            update.Parameters.Add("safe_error_code", System.Data.SqlDbType.VarChar).Value =
                (object?)safeErrorCode ?? DBNull.Value;
            update.Parameters.AddWithValue("now", nowUtc);
            update.Parameters.AddWithValue("next_attempt", decision.NextAttemptAtUtc);
            update.Parameters.AddWithValue("is_terminal_failure", decision.IsTerminalFailure);
            update.Parameters.AddWithValue("concurrency_token", Guid.NewGuid());
            update.Parameters.AddWithValue("id", row.Id);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task SendAsync(NotificationMail notification, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(options.SmtpTimeout);
        using var message = new MailMessage
        {
            From = options.FromAddress,
            Subject = notification.Subject,
            Body = notification.Html,
            IsBodyHtml = true
        };
        message.To.Add(new MailAddress(notification.RecipientEmail));
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
            notification.PlainText,
            null,
            "text/plain"));
        using var client = new SmtpClient(options.SmtpHost, options.SmtpPort)
        {
            EnableSsl = true,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false
        };
        if (!string.IsNullOrWhiteSpace(options.SmtpUsername))
        {
            client.Credentials = new NetworkCredential(options.SmtpUsername, options.SmtpPassword);
        }

        await client.SendMailAsync(message, timeoutSource.Token);
    }

    private sealed record NotificationRow(
        Guid Id,
        string NotificationType,
        string RecipientEmail,
        string ProtectionScheme,
        byte[] ProtectedPayload,
        int AttemptCount);
}
