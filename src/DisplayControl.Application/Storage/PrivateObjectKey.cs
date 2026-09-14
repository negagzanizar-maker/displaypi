namespace DisplayControl.Application.Storage;

public readonly record struct PrivateObjectKey
{
    public PrivateObjectKey(Guid tenantId, Guid objectId)
    {
        if (tenantId == Guid.Empty || objectId == Guid.Empty)
        {
            throw new ArgumentException("Tenant and object identifiers cannot be empty.");
        }

        TenantId = tenantId;
        ObjectId = objectId;
    }

    public Guid TenantId { get; }

    public Guid ObjectId { get; }

    public override string ToString() => $"tenants/{TenantId:N}/objects/{ObjectId:N}";
}
