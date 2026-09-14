using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using DisplayControl.Api.Notifications;
using DisplayControl.Api.Operations;
using DisplayControl.Api.Security;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace DisplayControl.IntegrationTests.Security;

public sealed class NotificationSecurityTests
{
    [Fact]
    public void NotificationAttemptPolicyStopsAtTheConfiguredMaximum()
    {
        var nowUtc = new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

        var retry = NotificationDeliveryAttemptPolicy.Evaluate(6, succeeded: false, 8, nowUtc);
        var terminal = NotificationDeliveryAttemptPolicy.Evaluate(7, succeeded: false, 8, nowUtc);

        Assert.False(retry.IsTerminalFailure);
        Assert.Equal(7, retry.AttemptCount);
        Assert.Equal(nowUtc.AddHours(1), retry.NextAttemptAtUtc);
        Assert.True(terminal.IsTerminalFailure);
        Assert.Equal(8, terminal.AttemptCount);
    }

    [Fact]
    public void RetentionRequiresTheDedicatedMaintenanceLoginAndLeavesAuditHistoryByDefault()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MaintenanceDatabase"] =
                "Server=localhost;Database=test;User Id=display_control_maintenance;Password=test;TrustServerCertificate=True"
        }).Build();

        var options = OperationalDataRetentionOptions.FromConfiguration(configuration);

        Assert.Equal(TimeSpan.FromDays(30), options.HeartbeatRetention);
        Assert.Null(options.AuditRetention);

        var wrongRole = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MaintenanceDatabase"] =
                "Server=localhost;Database=test;User Id=display_control_runtime;Password=test;TrustServerCertificate=True"
        }).Build();
        Assert.Throws<InvalidOperationException>(() =>
            OperationalDataRetentionOptions.FromConfiguration(wrongRole));
    }

    [Fact]
    public void NotificationDeliveryDefaultsToEightAttemptsAndRejectsUnsafeBounds()
    {
        var defaults = NotificationDeliveryOptions.FromConfiguration(CreateNotificationConfiguration());
        Assert.Equal(8, defaults.MaximumAttempts);
        Assert.Equal(TimeSpan.FromSeconds(30), defaults.SmtpTimeout);

        var invalid = CreateNotificationConfiguration(new Dictionary<string, string?>
        {
            ["Notifications:MaximumAttempts"] = "0"
        });
        Assert.Throws<InvalidOperationException>(() => NotificationDeliveryOptions.FromConfiguration(invalid));
    }

    [Fact]
    public void AuthenticationLimiterCombinesAttemptsByAccountKey()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var limiter = new AuthenticationAccountRateLimiter(
            cache,
            TimeProvider.System,
            AuthenticationAccountRateLimitOptions.Default);

        for (var attempt = 0; attempt < 10; attempt++)
        {
            Assert.True(limiter.TryAcquire("email:USER@EXAMPLE.TEST"));
        }

        Assert.False(limiter.TryAcquire("email:USER@EXAMPLE.TEST"));
        Assert.True(limiter.TryAcquire("email:OTHER@EXAMPLE.TEST"));
    }

    [Fact]
    public void AuthenticationLimiterAllowsTheAccountAgainAtTheWindowBoundary()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var timeProvider = new ManualTimestampProvider();
        var limiter = new AuthenticationAccountRateLimiter(
            cache,
            timeProvider,
            new AuthenticationAccountRateLimitOptions(2, TimeSpan.FromMinutes(5)));

        Assert.True(limiter.TryAcquire("account"));
        Assert.True(limiter.TryAcquire("account"));
        Assert.False(limiter.TryAcquire("account"));

        timeProvider.Advance(TimeSpan.FromMinutes(5));
        Assert.True(limiter.TryAcquire("account"));
    }

    [Fact]
    public void PlatformBootstrapCredentialUsesOnlyTheConfiguredSha256Digest()
    {
        const string token = "one-time-bootstrap-token-with-entropy";
        var digest = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        var credential = new PlatformBootstrapCredential(digest);

        Assert.True(credential.Enabled);
        Assert.True(credential.Verify(token));
        Assert.False(credential.Verify("wrong-token"));
        Assert.False(new PlatformBootstrapCredential(null).Verify(token));
    }

    [Fact]
    public void PasswordResetMessageEncodesUntrustedQueryValuesAndHtmlAttribute()
    {
        var payload = JsonSerializer.Serialize(new { Token = "a&b?=" });
        var message = PasswordResetMessageFactory.Create(
            new Uri("https://display.example.test/"),
            "user+screen@example.test",
            payload);

        Assert.Contains("email=user%2Bscreen%40example.test", message.PlainText, StringComparison.Ordinal);
        Assert.Contains("token=a%26b%3F%3D", message.PlainText, StringComparison.Ordinal);
        Assert.Contains("&amp;token=a%26b%3F%3D", message.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("a&b?=", message.Html, StringComparison.Ordinal);
        var resetLink = ExtractFirstUri(message.PlainText);
        Assert.Empty(resetLink.Query);
        Assert.StartsWith("#/reset-password?", resetLink.Fragment, StringComparison.Ordinal);
    }

    [Fact]
    public void InvitationTokenIsKeptInTheBrowserFragment()
    {
        var payload = JsonSerializer.Serialize(new { Token = "invitation-token-value" });
        var message = InvitationMessageFactory.Create(
            new Uri("https://display.example.test/"),
            "user@example.test",
            payload);

        var invitationLink = ExtractFirstUri(message.PlainText);
        Assert.Empty(invitationLink.Query);
        Assert.StartsWith("#/accept-invitation?token=", invitationLink.Fragment, StringComparison.Ordinal);
    }

    private static Uri ExtractFirstUri(string text)
    {
        var start = text.IndexOf("https://", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = text.IndexOfAny([' ', '\r', '\n'], start);
        return new Uri(end < 0 ? text[start..] : text[start..end]);
    }

    private static IConfiguration CreateNotificationConfiguration(
        IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:NotificationDatabase"] = "Server=localhost;Database=test;User Id=test;Password=test;TrustServerCertificate=True",
            ["Notifications:PublicBaseUrl"] = "https://display.example.test",
            ["Notifications:Smtp:Host"] = "smtp.example.test",
            ["Notifications:Smtp:FromAddress"] = "display@example.test"
        };
        if (overrides is not null)
        {
            foreach (var setting in overrides)
            {
                settings[setting.Key] = setting.Value;
            }
        }

        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    private sealed class ManualTimestampProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);

        public void Advance(TimeSpan duration) =>
            Interlocked.Add(ref _timestamp, duration.Ticks);
    }
}
