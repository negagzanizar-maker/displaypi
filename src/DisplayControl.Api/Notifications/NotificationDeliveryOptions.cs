using System.Net.Mail;

namespace DisplayControl.Api.Notifications;

public sealed record NotificationDeliveryOptions(
    string DatabaseConnectionString,
    Uri PublicBaseUri,
    string SmtpHost,
    int SmtpPort,
    string? SmtpUsername,
    string? SmtpPassword,
    MailAddress FromAddress,
    int MaximumAttempts,
    TimeSpan SmtpTimeout)
{
    public static NotificationDeliveryOptions FromConfiguration(IConfiguration configuration)
    {
        var database = configuration.GetConnectionString("NotificationDatabase");
        var publicBaseUrl = configuration["Notifications:PublicBaseUrl"];
        var host = configuration["Notifications:Smtp:Host"];
        var from = configuration["Notifications:Smtp:FromAddress"];
        var port = configuration.GetValue<int?>("Notifications:Smtp:Port") ?? 587;
        var maximumAttempts = configuration.GetValue<int?>("Notifications:MaximumAttempts") ?? 8;
        var smtpTimeoutSeconds = configuration.GetValue<int?>("Notifications:Smtp:TimeoutSeconds") ?? 30;
        if (string.IsNullOrWhiteSpace(database) ||
            !Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var publicBaseUri) ||
            publicBaseUri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(host) ||
            host.Length > 253 ||
            port is < 1 or > 65535 ||
            maximumAttempts is < 1 or > 100 ||
            smtpTimeoutSeconds is < 5 or > 120 ||
            string.IsNullOrWhiteSpace(from))
        {
            throw new InvalidOperationException(
                "Enabled notification delivery requires NotificationDatabase, an HTTPS PublicBaseUrl, and valid SMTP host/port/from settings.");
        }

        MailAddress fromAddress;
        try
        {
            fromAddress = new MailAddress(from);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("Notifications:Smtp:FromAddress is invalid.", exception);
        }

        var username = configuration["Notifications:Smtp:Username"];
        var password = configuration["Notifications:Smtp:Password"];
        if (string.IsNullOrWhiteSpace(username) != string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("SMTP username and password must either both be supplied or both be absent.");
        }

        return new NotificationDeliveryOptions(
            database,
            publicBaseUri,
            host,
            port,
            username,
            password,
            fromAddress,
            maximumAttempts,
            TimeSpan.FromSeconds(smtpTimeoutSeconds));
    }
}
