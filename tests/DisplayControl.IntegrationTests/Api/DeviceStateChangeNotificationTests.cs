using System.Net;
using System.Reflection;
using DisplayControl.Api.Controllers;
using DisplayControl.Api.Realtime;
using DisplayControl.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DisplayControl.IntegrationTests.Api;

public sealed class DeviceStateChangeNotificationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public DeviceStateChangeNotificationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
    }

    [Fact]
    public async Task DevicePublicationSignalsOnlyTheAffectedDeviceAfterFlush()
    {
        var tenantId = Guid.NewGuid();
        var affectedId = Guid.NewGuid();
        var unrelatedId = Guid.NewGuid();
        var broker = new DeviceStateChangeBroker();
        var notifications = new DeviceStateChangeNotifications(broker);
        using var affected = broker.Subscribe(tenantId, affectedId);
        using var unrelated = broker.Subscribe(tenantId, unrelatedId);

        notifications.Enqueue(tenantId, affectedId);
        Assert.False(await ReceivesAsync(affected, TimeSpan.FromMilliseconds(20)));

        notifications.FlushCommitted();

        Assert.True(await ReceivesAsync(affected, TimeSpan.FromSeconds(1)));
        Assert.False(await ReceivesAsync(unrelated, TimeSpan.FromMilliseconds(20)));
    }

    [Fact]
    public async Task GroupPublicationSignalsEveryMemberAndNoOtherDevice()
    {
        var tenantId = Guid.NewGuid();
        var members = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var unrelatedId = Guid.NewGuid();
        var broker = new DeviceStateChangeBroker();
        var notifications = new DeviceStateChangeNotifications(broker);
        using var first = broker.Subscribe(tenantId, members[0]);
        using var second = broker.Subscribe(tenantId, members[1]);
        using var unrelated = broker.Subscribe(tenantId, unrelatedId);

        notifications.Enqueue(tenantId, members);
        notifications.FlushCommitted();

        Assert.True(await ReceivesAsync(first, TimeSpan.FromSeconds(1)));
        Assert.True(await ReceivesAsync(second, TimeSpan.FromSeconds(1)));
        Assert.False(await ReceivesAsync(unrelated, TimeSpan.FromMilliseconds(20)));
    }

    [Fact]
    public async Task DuplicateNotificationsCoalesceAndTenantBoundaryIsPartOfRoutingKey()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var broker = new DeviceStateChangeBroker();
        var notifications = new DeviceStateChangeNotifications(broker);
        using var expected = broker.Subscribe(tenantId, deviceId);
        using var otherTenant = broker.Subscribe(otherTenantId, deviceId);

        notifications.Enqueue(tenantId, deviceId);
        notifications.Enqueue(tenantId, deviceId);
        notifications.FlushCommitted();

        Assert.True(await ReceivesAsync(expected, TimeSpan.FromSeconds(1)));
        Assert.False(await ReceivesAsync(expected, TimeSpan.FromMilliseconds(20)));
        Assert.False(await ReceivesAsync(otherTenant, TimeSpan.FromMilliseconds(20)));
    }

    [Fact]
    public void StateChangeStreamRequiresDeviceCertificateIdentityAndSkipsLongTenantTransaction()
    {
        var action = typeof(DeviceStateChangesController).GetMethod(nameof(DeviceStateChangesController.Stream))
            ?? throw new InvalidOperationException("State-change stream action was not found.");
        var authorization = Assert.Single(action.GetCustomAttributes<AuthorizeAttribute>());

        Assert.Equal(AuthorizationPolicies.DeviceAuthenticated, authorization.Policy);
        Assert.NotNull(action.GetCustomAttribute<SkipTenantTransactionAttribute>());
    }

    [Fact]
    public async Task StateChangeStreamRejectsAClientWithoutAnActiveDeviceCertificate()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync("/device/v1/state-changes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual("text/event-stream", response.Content.Headers.ContentType?.MediaType);
    }

    private static async Task<bool> ReceivesAsync(
        DeviceStateChangeBroker.DeviceStateChangeSubscription subscription,
        TimeSpan timeout)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        try
        {
            return await subscription.WaitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
