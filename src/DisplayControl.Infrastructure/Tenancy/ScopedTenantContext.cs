using DisplayControl.Application.Tenancy;

namespace DisplayControl.Infrastructure.Tenancy;

public sealed class ScopedTenantContext : ICurrentTenant
{
    public Guid? TenantId { get; private set; }

    public void SetFromTrustedBoundary(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant identifier cannot be empty.", nameof(tenantId));
        }

        if (TenantId is not null && TenantId != tenantId)
        {
            throw new InvalidOperationException("Tenant context cannot change during one request scope.");
        }

        TenantId = tenantId;
    }

    public void SetForCapabilityLookup(Guid tenantId) => SetFromTrustedBoundary(tenantId);
}
