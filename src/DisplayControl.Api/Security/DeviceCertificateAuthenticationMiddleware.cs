using System.Security.Claims;
using System.Security.Cryptography;
using DisplayControl.Application.Security;
using DisplayControl.Domain.Devices;
using DisplayControl.Domain.Tenancy;
using DisplayControl.Infrastructure.Persistence;
using DisplayControl.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DisplayControl.Api.Security;

public sealed class DeviceCertificateAuthenticationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        DisplayControlDbContext dbContext,
        ScopedTenantContext tenantContext,
        IDeviceCertificateIssuer certificateAuthority,
        TimeProvider timeProvider)
    {
        if (!context.Request.Path.StartsWithSegments("/device/v1", StringComparison.OrdinalIgnoreCase) ||
            context.Request.Path.StartsWithSegments("/device/v1/enrollment", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var certificate = await context.Connection.GetClientCertificateAsync(context.RequestAborted);
        var nowUtc = timeProvider.GetUtcNow();
        if (certificate is null ||
            !certificateAuthority.TryValidateClientCertificate(certificate, nowUtc, out var identity))
        {
            await RejectAsync(context, "device_certificate_invalid");
            return;
        }

        tenantContext.SetFromTrustedBoundary(identity.TenantId);
        DeviceCertificate? storedCertificate;
        await using (var transaction = await dbContext.BeginTenantTransactionAsync(
            identity.TenantId,
            context.RequestAborted))
        {
            var thumbprint = SHA256.HashData(certificate.RawData);
            storedCertificate = await dbContext.DeviceCertificates.AsNoTracking().SingleOrDefaultAsync(
                value => value.ThumbprintSha256 == thumbprint,
                context.RequestAborted);
            var deviceIsActive = storedCertificate is not null && await dbContext.Devices.AsNoTracking().AnyAsync(
                value => value.Id == identity.DeviceId && value.State == DeviceLifecycleState.Active,
                context.RequestAborted);
            var tenantIsActive = await dbContext.Tenants.AsNoTracking().AnyAsync(
                value => value.Id == identity.TenantId && value.State == TenantState.Active,
                context.RequestAborted);
            if (storedCertificate is null ||
                storedCertificate.TenantId != identity.TenantId ||
                storedCertificate.DeviceId != identity.DeviceId ||
                storedCertificate.State != DeviceCertificateState.Active ||
                storedCertificate.NotBeforeUtc > nowUtc ||
                storedCertificate.NotAfterUtc <= nowUtc ||
                !deviceIsActive ||
                !tenantIsActive)
            {
                await RejectAsync(context, "device_certificate_inactive");
                return;
            }

            await transaction.CommitAsync(context.RequestAborted);
        }

        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, identity.DeviceId.ToString()),
            new Claim(DeviceClaimTypes.DeviceId, identity.DeviceId.ToString()),
            new Claim(DeviceClaimTypes.TenantId, identity.TenantId.ToString()),
            new Claim(DeviceClaimTypes.CertificateId, storedCertificate.Id.ToString()),
            new Claim(DeviceClaimTypes.AuthenticationMethod, DeviceClaimTypes.MutualTlsAuthenticationMethod)
        ], DeviceClaimTypes.AuthenticationType));

        await next(context);
    }

    private static async Task RejectAsync(HttpContext context, string code)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Type = "https://docs.example.invalid/problems/device-authentication",
            Title = "A valid active device certificate is required.",
            Status = StatusCodes.Status401Unauthorized,
            Extensions = { ["code"] = code }
        }, context.RequestAborted);
    }
}
