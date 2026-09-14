using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

using DisplayControl.Application.Security;
using DisplayControl.Domain.Devices;
using DisplayControl.Domain.Operations;
using DisplayControl.Infrastructure.Persistence;
using DisplayControl.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace DisplayControl.Api.Controllers;

[ApiController]
[Route("device/v1/enrollment")]
public sealed class DeviceEnrollmentController(
    DisplayControlDbContext dbContext,
    ScopedTenantContext tenantContext,
    ITenantCapabilityTokenService capabilityTokenService,
    IDeviceCertificateIssuer certificateIssuer,
    ILicenseLeaseSigner leaseSigner,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpPost]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [EnableRateLimiting("device-enrollment")]
    [RequestSizeLimit(64 * 1024)]
    public async Task<ActionResult<DeviceEnrollmentResponse>> Enroll(
        DeviceEnrollmentRequest request,
        CancellationToken cancellationToken)
    {
        if (!capabilityTokenService.TryReadTenantId(request.EnrollmentCode, out var tenantId))
        {
            return InvalidEnrollment();
        }

        if (!DeviceInventoryNormalizer.TryNormalize(request.NetworkInterfaces, out var normalizedInterfaces))
        {
            return InvalidInventory();
        }

        tenantContext.SetForCapabilityLookup(tenantId);
        await using var transaction = await dbContext.BeginTenantTransactionAsync(tenantId, cancellationToken);
        var digest = capabilityTokenService.ComputeDigest(request.EnrollmentCode);
        var enrollment = await dbContext.EnrollmentTokens.SingleOrDefaultAsync(
            value => value.TokenDigest == digest,
            cancellationToken);
        var nowUtc = timeProvider.GetUtcNow();
        if (enrollment is null || enrollment.ExpectedDeviceId is not Guid deviceId)
        {
            return InvalidEnrollment();
        }

        var normalizedSerial = request.SerialNumber.Trim().ToUpperInvariant();
        if (enrollment.ConsumedAtUtc is DateTimeOffset consumedAtUtc)
        {
            var replayDeadlineUtc = consumedAtUtc.AddMinutes(10) < enrollment.ExpiresAtUtc
                ? consumedAtUtc.AddMinutes(10)
                : enrollment.ExpiresAtUtc;
            if (enrollment.ConsumedByDeviceId != deviceId || nowUtc >= replayDeadlineUtc ||
                enrollment.ExpectedSerialNumberNormalized is not null &&
                !string.Equals(enrollment.ExpectedSerialNumberNormalized, normalizedSerial, StringComparison.Ordinal))
            {
                return InvalidEnrollment();
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

            var originalCertificate = await dbContext.DeviceCertificates.AsNoTracking().SingleOrDefaultAsync(
                value => value.DeviceId == deviceId && value.RotatedFromCertificateId == null &&
                    value.State == DeviceCertificateState.Active && value.NotAfterUtc > nowUtc,
                cancellationToken);
            var enrolledDevice = await dbContext.Devices.AsNoTracking().SingleOrDefaultAsync(
                value => value.Id == deviceId && value.SerialNumberNormalized == normalizedSerial &&
                    value.State == DeviceLifecycleState.Active,
                cancellationToken);
            if (enrolledDevice is null || originalCertificate is null || !CryptographicOperations.FixedTimeEquals(
                    originalCertificate.SubjectPublicKeyInfoSha256,
                    requestedPublicKeySha256))
            {
                return InvalidEnrollment();
            }

            await transaction.CommitAsync(cancellationToken);
            return Ok(CreateResponse(tenantId, deviceId, originalCertificate, nowUtc));
        }

        if (!enrollment.CanBeConsumedAt(nowUtc))
        {
            return InvalidEnrollment();
        }

        if (enrollment.ExpectedSerialNumberNormalized is not null &&
            !string.Equals(enrollment.ExpectedSerialNumberNormalized, normalizedSerial, StringComparison.Ordinal))
        {
            enrollment.RecordFailedAttempt(nowUtc);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return InvalidEnrollment();
        }

        var device = await dbContext.Devices.SingleOrDefaultAsync(value => value.Id == deviceId, cancellationToken);
        if (device is null || device.State != DeviceLifecycleState.PendingEnrollment)
        {
            return InvalidEnrollment();
        }

        var serialConflict = await dbContext.Devices.AnyAsync(
            value => value.Id != deviceId && value.SerialNumberNormalized == normalizedSerial,
            cancellationToken);
        if (serialConflict)
        {
            device.Quarantine(nowUtc);
            enrollment.RecordFailedAttempt(nowUtc);
            AddAudit(tenantId, deviceId, "device.enrollment.identity-conflict", "Rejected", nowUtc);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Conflict(new ProblemDetails
            {
                Type = "https://docs.example.invalid/problems/enrollment-identity-conflict",
                Title = "The reported hardware identity requires administrative review.",
                Status = StatusCodes.Status409Conflict,
                Extensions = { ["code"] = "enrollment_identity_conflict" }
            });
        }

        IssuedDeviceCertificate issuedCertificate;
        try
        {
            issuedCertificate = certificateIssuer.Issue(
                tenantId,
                deviceId,
                request.CertificateSigningRequestPem,
                nowUtc);
        }
        catch (CryptographicException)
        {
            enrollment.RecordFailedAttempt(nowUtc);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return InvalidCertificateRequest();
        }

        device.CompleteEnrollment(
            normalizedSerial,
            request.Hostname,
            request.OsDescription,
            request.Architecture,
            request.AgentVersion,
            request.PlayerVersion,
            request.DiskCapacityBytes,
            nowUtc);
        enrollment.Consume(deviceId, nowUtc);
        var certificate = new DeviceCertificate(
            Guid.NewGuid(),
            tenantId,
            deviceId,
            issuedCertificate.SerialNumber,
            issuedCertificate.ThumbprintSha256,
            issuedCertificate.SubjectPublicKeyInfoSha256,
            issuedCertificate.CertificateDer,
            issuedCertificate.NotBeforeUtc,
            issuedCertificate.NotAfterUtc,
            nowUtc);
        dbContext.DeviceCertificates.Add(certificate);
        foreach (var network in normalizedInterfaces)
        {
            dbContext.DeviceNetworkInterfaces.Add(new DeviceNetworkInterface(
                Guid.NewGuid(),
                tenantId,
                deviceId,
                network.InterfaceName,
                network.MacAddress,
                JsonSerializer.Serialize(network.LocalAddresses),
                nowUtc));
        }

        AddAudit(tenantId, deviceId, "device.enrollment.completed", "Succeeded", nowUtc);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return Conflict(new ProblemDetails
            {
                Type = "https://docs.example.invalid/problems/enrollment-conflict",
                Title = "Enrollment conflicted with another device operation.",
                Status = StatusCodes.Status409Conflict,
                Extensions = { ["code"] = "enrollment_conflict" }
            });
        }

        return Ok(CreateResponse(tenantId, deviceId, certificate, nowUtc));
    }

    private DeviceEnrollmentResponse CreateResponse(
        Guid tenantId,
        Guid deviceId,
        DeviceCertificate certificate,
        DateTimeOffset serverTimeUtc) => new(
            tenantId,
            deviceId,
            certificate.Id,
            ExportCertificatePem(certificate),
            certificateIssuer.CertificateAuthorityPem,
            leaseSigner.VerificationKey,
            certificate.NotAfterUtc,
            serverTimeUtc,
            new Uri("/device/v1/heartbeats", UriKind.Relative));

    private static string ExportCertificatePem(DeviceCertificate certificate) =>
        certificate.CertificateDer is { Length: > 0 } certificateDer
            ? PemEncoding.WriteString("CERTIFICATE", certificateDer)
            : throw new InvalidOperationException("The issued certificate bytes are unavailable for replay.");

    private void AddAudit(Guid tenantId, Guid deviceId, string action, string outcome, DateTimeOffset nowUtc) =>
        dbContext.AuditEvents.Add(new TenantAuditEvent(
            Guid.NewGuid(),
            tenantId,
            "EnrollmentCapability",
            null,
            action,
            "Device",
            deviceId,
            outcome,
            null,
            HttpContext.TraceIdentifierGuid(),
            "{}",
            nowUtc));

    private static BadRequestObjectResult InvalidEnrollment() => Problem(
        "enrollment_invalid",
        "The enrollment request is invalid, expired, already used, or does not match this device.");

    private static BadRequestObjectResult InvalidInventory() => Problem(
        "inventory_invalid",
        "The device inventory is invalid.");

    private static BadRequestObjectResult InvalidCertificateRequest() => Problem(
        "csr_invalid",
        "The certificate signing request is invalid or does not use ECDSA P-256.");

    private static BadRequestObjectResult Problem(string code, string title) => new(new ProblemDetails
    {
        Type = "https://docs.example.invalid/problems/device-enrollment",
        Title = title,
        Status = StatusCodes.Status400BadRequest,
        Extensions = { ["code"] = code }
    });

}

internal static class DeviceInventoryNormalizer
{
    public static bool TryNormalize(
        IReadOnlyList<DeviceEnrollmentNetworkRequest> values,
        out IReadOnlyList<NormalizedNetworkInterface> normalized)
    {
        normalized = [];
        if (values.Count > 16 ||
            values.Select(value => value.InterfaceName.Trim()).Distinct(StringComparer.Ordinal).Count() != values.Count)
        {
            return false;
        }

        var output = new List<NormalizedNetworkInterface>(values.Count);
        foreach (var value in values)
        {
            var addresses = value.LocalAddresses
                .Select(address => IPAddress.TryParse(address, out var parsed) ? parsed.ToString() : null)
                .ToArray();
            if (addresses.Length > 16 || addresses.Any(address => address is null))
            {
                return false;
            }

            var mac = value.MacAddress?.Replace(":", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal)
                .ToUpperInvariant();
            if (mac is not null && (mac.Length != 12 || mac.Any(character => !Uri.IsHexDigit(character))))
            {
                return false;
            }

            output.Add(new NormalizedNetworkInterface(
                value.InterfaceName.Trim(),
                mac,
                addresses.Cast<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()));
        }

        normalized = output;
        return true;
    }
}

internal sealed record NormalizedNetworkInterface(
    string InterfaceName,
    string? MacAddress,
    IReadOnlyList<string> LocalAddresses);

public sealed record DeviceEnrollmentRequest(
    [param: Required, StringLength(160, MinimumLength = 1)] string EnrollmentCode,
    [param: Required, StringLength(16_384, MinimumLength = 128)] string CertificateSigningRequestPem,
    [param: Required, StringLength(32, MinimumLength = 1)] string SerialNumber,
    [param: Required, StringLength(253, MinimumLength = 1)] string Hostname,
    [param: Required, StringLength(256, MinimumLength = 1)] string OsDescription,
    [param: Required, StringLength(32, MinimumLength = 1)] string Architecture,
    [param: Required, StringLength(64, MinimumLength = 1)] string AgentVersion,
    [param: Required, StringLength(64, MinimumLength = 1)] string PlayerVersion,
    [param: Range(1, long.MaxValue)] long DiskCapacityBytes,
    IReadOnlyList<DeviceEnrollmentNetworkRequest> NetworkInterfaces);

public sealed record DeviceEnrollmentNetworkRequest(
    [param: Required, StringLength(64, MinimumLength = 1)] string InterfaceName,
    [param: StringLength(32)] string? MacAddress,
    IReadOnlyList<string> LocalAddresses);

public sealed record DeviceEnrollmentResponse(
    Guid TenantId,
    Guid DeviceId,
    Guid CertificateId,
    string CertificatePem,
    string CertificateAuthorityPem,
    LicenseLeaseVerificationKey LicenseVerificationKey,
    DateTimeOffset CertificateExpiresAtUtc,
    DateTimeOffset ServerTimeUtc,
    Uri HeartbeatEndpoint);
