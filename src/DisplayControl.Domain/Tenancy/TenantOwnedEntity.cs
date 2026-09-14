namespace DisplayControl.Domain.Tenancy;

public abstract class TenantOwnedEntity
{
    public Guid Id { get; protected init; }

    public Guid TenantId { get; protected init; }
}
