namespace DisplayControl.Application.Tenancy;

/// <summary>
/// Exposes tenant identity already established by a trusted authentication boundary.
/// Request selectors must never implement this contract directly.
/// </summary>
public interface ICurrentTenant
{
    public Guid? TenantId { get; }
}
