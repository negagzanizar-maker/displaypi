using System.Text.Json;
using DisplayControl.Domain.Operations;
using DisplayControl.Infrastructure.Persistence;
using DisplayControl.Infrastructure.Tenancy;

namespace DisplayControl.Api.Security;

public sealed class TenantSecurityAuditService(
    DisplayControlDbContext dbContext,
    ScopedTenantContext tenantContext,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider timeProvider)
{
    public void Add(
        Guid tenantId,
        Guid? actorId,
        string action,
        string targetType,
        Guid? targetId,
        string outcome,
        string? reasonCode,
        object? details = null)
    {
        tenantContext.SetFromTrustedBoundary(tenantId);
        var context = httpContextAccessor.HttpContext;
        dbContext.AuditEvents.Add(new TenantAuditEvent(
            Guid.NewGuid(),
            tenantId,
            actorId is null ? "anonymous" : "user",
            actorId,
            action,
            targetType,
            targetId,
            outcome,
            reasonCode,
            TraceIdentifierGuid(context),
            JsonSerializer.Serialize(details ?? new { }),
            timeProvider.GetUtcNow()));
    }

    private static Guid TraceIdentifierGuid(HttpContext? context)
    {
        if (context is null)
        {
            return Guid.NewGuid();
        }

        return Guid.TryParse(context.TraceIdentifier, out var correlationId)
            ? correlationId
            : new Guid(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(context.TraceIdentifier)).AsSpan(0, 16));
    }
}
