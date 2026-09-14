using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using DisplayControl.Api.Devices;
using DisplayControl.Api.Security;
using DisplayControl.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DisplayControl.Api.Controllers;

[ApiController]
[Route("device/v1/heartbeats")]
public sealed class DeviceHeartbeatsController(DeviceHeartbeatWorkflow workflow) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.DeviceAuthenticated)]
    [IgnoreAntiforgeryToken]
    [RequestSizeLimit(64 * 1024)]
    public async Task<ActionResult<DeviceHeartbeatResponse>> Heartbeat(
        DeviceHeartbeatRequest request,
        CancellationToken cancellationToken)
    {
        var result = await workflow.ProcessAsync(
            RequiredClaimGuid(DeviceClaimTypes.TenantId),
            RequiredClaimGuid(DeviceClaimTypes.DeviceId),
            RequiredClaimGuid(DeviceClaimTypes.CertificateId),
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            HttpContext.TraceIdentifierGuid(),
            request,
            cancellationToken);
        return result.Outcome switch
        {
            DeviceHeartbeatOutcome.Success => Ok(result.Response),
            DeviceHeartbeatOutcome.Invalid => InvalidHeartbeat(),
            DeviceHeartbeatOutcome.Replay => HeartbeatReplay(),
            _ => throw new InvalidOperationException("Unsupported heartbeat workflow outcome.")
        };
    }

    private Guid RequiredClaimGuid(string claimType) =>
        Guid.TryParse(User.FindFirstValue(claimType), out var value)
            ? value
            : throw new InvalidOperationException("The device principal is missing a required binding claim.");

    private static BadRequestObjectResult InvalidHeartbeat() => new(new ValidationProblemDetails(
        new Dictionary<string, string[]> { ["heartbeat"] = ["The heartbeat inventory or timestamp is invalid."] })
    {
        Type = "https://docs.example.invalid/problems/heartbeat-invalid",
        Title = "The heartbeat is invalid.",
        Status = StatusCodes.Status400BadRequest,
        Extensions = { ["code"] = "heartbeat_invalid" }
    });

    private static ConflictObjectResult HeartbeatReplay() => new(new ProblemDetails
    {
        Type = "https://docs.example.invalid/problems/heartbeat-replay",
        Title = "The heartbeat sequence was already observed.",
        Status = StatusCodes.Status409Conflict,
        Extensions = { ["code"] = "heartbeat_replay" }
    });
}

public sealed record DeviceHeartbeatRequest(
    Guid BootId,
    [param: Range(1, long.MaxValue)] long Sequence,
    DateTimeOffset? ReportedSentAtUtc,
    [param: Required, StringLength(253, MinimumLength = 1)] string Hostname,
    [param: Required, StringLength(256, MinimumLength = 1)] string OsDescription,
    [param: Required, StringLength(32, MinimumLength = 1)] string Architecture,
    [param: Required, StringLength(64, MinimumLength = 1)] string AgentVersion,
    [param: Required, StringLength(64, MinimumLength = 1)] string PlayerVersion,
    [param: Range(1, long.MaxValue)] long DiskCapacityBytes,
    [param: Range(0, long.MaxValue)] long? FreeDiskBytes,
    long? AppliedDesiredStateVersion,
    [param: Required, StringLength(64, MinimumLength = 1)] string PlayerStateCode,
    [param: StringLength(64)] string? LastErrorCode,
    Guid? CurrentContentVersionId,
    IReadOnlyList<DeviceEnrollmentNetworkRequest> NetworkInterfaces);

public sealed record DeviceHeartbeatResponse(
    DateTimeOffset ServerTimeUtc,
    int RetryAfterSeconds,
    string LicenseStatus,
    LicenseLeaseResponse? Lease,
    DateTimeOffset? LicenseExpiresAtUtc,
    DesiredStateSummaryResponse DesiredState,
    IReadOnlyList<LicenseLeaseVerificationKey>? LicenseVerificationKeys = null);

public sealed record LicenseLeaseResponse(
    string Token,
    string KeyId,
    DateTimeOffset ExpiresAtUtc,
    string Algorithm,
    string SubjectPublicKeyInfoPem);

public sealed record DesiredStateSummaryResponse(string Status, Guid? Id, long? Version);
