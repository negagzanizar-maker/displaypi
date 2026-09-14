using DisplayControl.Domain.Tenancy;

namespace DisplayControl.Domain.Devices;

public sealed class DeviceGroup : TenantOwnedEntity
{
    private DeviceGroup()
    {
    }

    public DeviceGroup(
        Guid id,
        Guid tenantId,
        string name,
        string? description,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || createdByUserId == Guid.Empty)
        {
            throw new ArgumentException("Group, tenant, and creator identifiers cannot be empty.");
        }

        EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        Id = id;
        TenantId = tenantId;
        Name = Normalize(name, 160, nameof(name));
        Description = NormalizeOptional(description, 2000, nameof(description));
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public Guid ConcurrencyToken { get; private set; }

    public void Update(string name, string? description, DateTimeOffset updatedAtUtc)
    {
        EnsureUtc(updatedAtUtc, nameof(updatedAtUtc));
        if (updatedAtUtc < UpdatedAtUtc)
        {
            throw new ArgumentException("Group timestamps must be monotonic.", nameof(updatedAtUtc));
        }

        Name = Normalize(name, 160, nameof(name));
        Description = NormalizeOptional(description, 2000, nameof(description));
        UpdatedAtUtc = updatedAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    public void MarkMembershipChanged(DateTimeOffset updatedAtUtc)
    {
        EnsureUtc(updatedAtUtc, nameof(updatedAtUtc));
        if (updatedAtUtc < UpdatedAtUtc)
        {
            throw new ArgumentException("Group timestamps must be monotonic.", nameof(updatedAtUtc));
        }

        UpdatedAtUtc = updatedAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    private static string Normalize(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException($"Value cannot exceed {maximumLength} characters.", parameterName);
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Normalize(value, maximumLength, parameterName);
    }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must have a UTC offset.", parameterName);
        }
    }
}
