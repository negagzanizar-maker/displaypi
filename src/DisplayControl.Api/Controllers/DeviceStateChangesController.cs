using System.Security.Claims;
using System.Text;
using DisplayControl.Api.Realtime;
using DisplayControl.Api.Security;
using DisplayControl.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DisplayControl.Api.Controllers;

[ApiController]
[Route("device/v1/state-changes")]
public sealed class DeviceStateChangesController(DeviceStateChangeBroker broker) : ControllerBase
{
    private static readonly byte[] StateChangedEvent = Encoding.UTF8.GetBytes("event: stateChanged\ndata: {}\n\n");
    private static readonly byte[] KeepAliveEvent = Encoding.UTF8.GetBytes(": keepalive\n\n");

    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.DeviceAuthenticated)]
    [SkipTenantTransaction]
    public async Task Stream(CancellationToken cancellationToken)
    {
        if (!TryReadClaim(DeviceClaimTypes.TenantId, out var tenantId) ||
            !TryReadClaim(DeviceClaimTypes.DeviceId, out var deviceId))
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache, no-store";
        Response.Headers.Append("X-Accel-Buffering", "no");

        using var subscription = broker.Subscribe(tenantId, deviceId);
        await WriteAsync(StateChangedEvent, cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            using var keepAlive = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            keepAlive.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                if (!await subscription.WaitAsync(keepAlive.Token))
                {
                    return;
                }

                await WriteAsync(StateChangedEvent, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await WriteAsync(KeepAliveEvent, cancellationToken);
            }
        }
    }

    private async Task WriteAsync(byte[] value, CancellationToken cancellationToken)
    {
        await Response.Body.WriteAsync(value, cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    private bool TryReadClaim(string claimType, out Guid value) =>
        Guid.TryParse(User.FindFirstValue(claimType), out value) && value != Guid.Empty;
}
