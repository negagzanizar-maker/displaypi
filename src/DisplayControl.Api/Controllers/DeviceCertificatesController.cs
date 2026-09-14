using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;

using DisplayControl.Api.Security;
using DisplayControl.Application.Security;
using DisplayControl.Domain.Devices;
using DisplayControl.Domain.Operations;
using DisplayControl.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DisplayControl.Api.Controllers;

[ApiController]
public sealed class DeviceCertificatesController(
    DisplayControlDbContext dbContext,
    IDeviceCertificateIssuer certificateIssuer,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpPost("device/v1/certificates/rotate")]
    [Authorize(Policy = AuthorizationPolicies.DeviceAuthenticated)]
    [IgnoreAntiforgeryToken]
    [RequestSizeLimit(64 * 1024)]
    public async Task<ActionResult<DeviceCertificateRotationResponse>> Rotate(
        DeviceCertificateRotationRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = RequiredClaimGuid(DeviceClaimTypes.TenantId);
        var deviceId = RequiredClaimGuid(DeviceClaimTypes.DeviceId);
        var currentCertificateId = RequiredClaimGuid(DeviceClaimTypes.CertificateId);
        var nowUtc = timeProvider.GetUtcNow();
        var current = await dbContext.DeviceCertificates.SingleAsync(
            value => value.Id == currentCertificateId && value.DeviceId == deviceId,
            cancellationToken);
        if (current.State != DeviceCertificateState.Active || nowUtc < current.NotAfterUtc.AddDays(-30))
        {
            return ConflictProblem("certificate_rotation_not_due", "Certificate rotation is not currently allowed.");
        }

        byte[] requestedPublicKeySha256;
        try
        {
            requestedPublicKeySha256 = certificateIssuer.GetSigningRequestPublicKeySha256(
                request.CertificateSigningRequestPem);
        }
        catch (CryptographicException)
        {
            return InvalidCertificateRequest();
        }

        var existingReplacement = await dbContext.DeviceCertificates.SingleOrDefaultAsync(
            value => value.RotatedFromCertificateId == currentCertificateId &&
                value.State == DeviceCertificateState.Active && value.NotAfterUtc > nowUtc,
            cancellationToken);
        if (existingReplacement is not null)
        {
            if (!CryptographicOperations.FixedTimeEquals(
                    existingReplacement.SubjectPublicKeyInfoSha256,
                    requestedPublicKeySha256))
            {
                return ConflictProblem("certificate_rotation_exists", "A replacement certificate already exists.");
            }

            return Ok(CreateRotationResponse(existingReplacement, current.NotAfterUtc, nowUtc));
        }

        IssuedDeviceCertificate issued;
        try
        {
            issued = certificateIssuer.Issue(tenantId, deviceId, request.CertificateSigningRequestPem, nowUtc);
        }
        catch (CryptographicException)
        {
            return InvalidCertificateRequest();
        }

        var replacement = new DeviceCertificate(
            Guid.NewGuid(),
            tenantId,
            deviceId,
            issued.SerialNumber,
            issued.ThumbprintSha256,
            issued.SubjectPublicKeyInfoSha256,
            issued.CertificateDer,
            issued.NotBeforeUtc,
            issued.NotAfterUtc,
            nowUtc,
            currentCertificateId);
        current.LimitValidity(nowUtc.AddHours(24));
        dbContext.DeviceCertificates.Add(replacement);
        dbContext.AuditEvents.Add(new TenantAuditEvent(
            Guid.NewGuid(),
            tenantId,
            "device",
            deviceId,
            "device.certificate.rotated",
            "deviceCertificate",
            replacement.Id,
            "success",
            null,
            HttpContext.TraceIdentifierGuid(),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                previousCertificateId = currentCertificateId,
                previousCertificateValidUntilUtc = current.NotAfterUtc
            }),
            nowUtc));
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(CreateRotationResponse(replacement, current.NotAfterUtc, nowUtc));
    }

    [HttpGet("api/v1/tenants/{tenantId:guid}/devices/{deviceId:guid}/certificates")]
    [Authorize(Policy = AuthorizationPolicies.TenantAdministrator)]
    public async Task<ActionResult<IReadOnlyList<DeviceCertificateSummaryResponse>>> List(
        Guid tenantId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var exists = await dbContext.Devices.AsNoTracking().AnyAsync(value => value.Id == deviceId, cancellationToken);
        if (!exists)
        {
            return NotFound();
        }

        var values = await dbContext.DeviceCertificates.AsNoTracking()
            .Where(value => value.DeviceId == deviceId)
            .OrderByDescending(value => value.IssuedAtUtc)
            .Select(value => new DeviceCertificateSummaryResponse(
                value.Id,
                value.State,
                value.NotBeforeUtc,
                value.NotAfterUtc,
                value.IssuedAtUtc,
                value.RotatedFromCertificateId,
                value.RevokedAtUtc))
            .ToListAsync(cancellationToken);
        return Ok(values);
    }

    [HttpPost("api/v1/tenants/{tenantId:guid}/devices/{deviceId:guid}/certificates/{certificateId:guid}/revoke")]
    [Authorize(Policy = AuthorizationPolicies.TenantAdministratorRecentMfa)]
    public async Task<IActionResult> Revoke(
        Guid tenantId,
        Guid deviceId,
        Guid certificateId,
        RevokeDeviceCertificateRequest request,
        CancellationToken cancellationToken)
    {
        var certificate = await dbContext.DeviceCertificates.SingleOrDefaultAsync(
            value => value.Id == certificateId && value.DeviceId == deviceId,
            cancellationToken);
        if (certificate is null)
        {
            return NotFound();
        }

        var activeCount = await dbContext.DeviceCertificates.CountAsync(
            value => value.DeviceId == deviceId && value.State == DeviceCertificateState.Active &&
                value.NotAfterUtc > timeProvider.GetUtcNow(),
            cancellationToken);
        if (certificate.State == DeviceCertificateState.Active && activeCount <= 1)
        {
            return ConflictProblem(
                "last_device_certificate",
                "Suspend or retire the device instead of revoking its last active credential.");
        }

        var nowUtc = timeProvider.GetUtcNow();
        certificate.Revoke(request.Reason, nowUtc);
        dbContext.AuditEvents.Add(new TenantAuditEvent(
            Guid.NewGuid(),
            tenantId,
            "user",
            CurrentUserId(),
            "device.certificate.revoked",
            "deviceCertificate",
            certificate.Id,
            "success",
            request.Reason,
            HttpContext.TraceIdentifierGuid(),
            "{}",
            nowUtc));
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private Guid RequiredClaimGuid(string claimType) => Guid.TryParse(User.FindFirstValue(claimType), out var value)
        ? value
        : throw new InvalidOperationException("The device principal is missing a required binding claim.");

    private Guid CurrentUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var value)
        ? value
        : throw new InvalidOperationException("The user principal is missing its identifier.");

    private DeviceCertificateRotationResponse CreateRotationResponse(
        DeviceCertificate certificate,
        DateTimeOffset previousCertificateAcceptedUntilUtc,
        DateTimeOffset serverTimeUtc) => new(
            certificate.Id,
            ExportCertificatePem(certificate),
            certificateIssuer.CertificateAuthorityPem,
            certificate.NotAfterUtc,
            previousCertificateAcceptedUntilUtc,
            serverTimeUtc);

    private static string ExportCertificatePem(DeviceCertificate certificate) =>
        certificate.CertificateDer is { Length: > 0 } certificateDer
            ? PemEncoding.WriteString("CERTIFICATE", certificateDer)
            : throw new InvalidOperationException("The issued certificate bytes are unavailable for retry.");

    private static BadRequestObjectResult InvalidCertificateRequest() => new(new ProblemDetails
    {
        Type = "https://docs.example.invalid/problems/device-certificate",
        Title = "The replacement certificate request is invalid.",
        Status = StatusCodes.Status400BadRequest,
        Extensions = { ["code"] = "csr_invalid" }
    });

    private static ObjectResult ConflictProblem(string code, string title) => new(new ProblemDetails
    {
        Type = "https://docs.example.invalid/problems/device-certificate",
        Title = title,
        Status = StatusCodes.Status409Conflict,
        Extensions = { ["code"] = code }
    })
    {
        StatusCode = StatusCodes.Status409Conflict
    };
}

public sealed record DeviceCertificateRotationRequest(
    [param: Required, StringLength(32_768, MinimumLength = 100)] string CertificateSigningRequestPem);

public sealed record DeviceCertificateRotationResponse(
    Guid CertificateId,
    string CertificatePem,
    string CertificateAuthorityPem,
    DateTimeOffset CertificateExpiresAtUtc,
    DateTimeOffset PreviousCertificateAcceptedUntilUtc,
    DateTimeOffset ServerTimeUtc);

public sealed record DeviceCertificateSummaryResponse(
    Guid Id,
    DeviceCertificateState State,
    DateTimeOffset NotBeforeUtc,
    DateTimeOffset NotAfterUtc,
    DateTimeOffset IssuedAtUtc,
    Guid? RotatedFromCertificateId,
    DateTimeOffset? RevokedAtUtc);

public sealed record RevokeDeviceCertificateRequest(
    [param: Required, StringLength(64, MinimumLength = 3)] string Reason);
