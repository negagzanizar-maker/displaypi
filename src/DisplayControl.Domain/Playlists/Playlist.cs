using DisplayControl.Domain.Tenancy;

namespace DisplayControl.Domain.Playlists;

public sealed class Playlist : TenantOwnedEntity
{
    private Playlist()
    {
    }

    public Playlist(
        Guid id,
        Guid tenantId,
        string name,
        string? description,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || createdByUserId == Guid.Empty)
        {
            throw new ArgumentException("Playlist identifiers cannot be empty.");
        }

        EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        Id = id;
        TenantId = tenantId;
        Name = Normalize(name, 200, nameof(name));
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

    public DateTimeOffset? ArchivedAtUtc { get; private set; }

    public Guid ConcurrencyToken { get; private set; }

    private static string Normalize(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"Value cannot exceed {maximumLength} characters.", parameterName);
    }

    private static string? NormalizeOptional(string? value, int maximumLength, string parameterName) =>
        string.IsNullOrWhiteSpace(value) ? null : Normalize(value, maximumLength, parameterName);

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must have a UTC offset.", parameterName);
        }
    }
}
