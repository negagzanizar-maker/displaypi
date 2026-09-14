namespace DisplayControl.Domain.Tenancy;

public sealed class Tenant
{
    private Tenant()
    {
    }

    public Tenant(Guid id, string name, string slug, string timeZone, DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Tenant identifier cannot be empty.", nameof(id));
        }

        Id = id;
        Name = Required(name, nameof(name), 160);
        Slug = NormalizeSlug(slug);
        TimeZone = NormalizeTimeZone(timeZone);
        EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
        State = TenantState.Active;
        ConcurrencyToken = Guid.NewGuid();
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    public string TimeZone { get; private set; } = string.Empty;

    public TenantState State { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public Guid ConcurrencyToken { get; private set; }

    public void UpdateDetails(string name, string timeZone, DateTimeOffset updatedAtUtc)
    {
        EnsureMutable();
        EnsureUtc(updatedAtUtc, nameof(updatedAtUtc));
        if (updatedAtUtc < UpdatedAtUtc)
        {
            throw new ArgumentException("Update timestamp cannot move backwards.", nameof(updatedAtUtc));
        }

        Name = Required(name, nameof(name), 160);
        TimeZone = NormalizeTimeZone(timeZone);
        Touch(updatedAtUtc);
    }

    public void ChangeState(TenantState state, DateTimeOffset changedAtUtc)
    {
        EnsureUtc(changedAtUtc, nameof(changedAtUtc));
        if (changedAtUtc < UpdatedAtUtc)
        {
            throw new ArgumentException("State timestamp cannot move backwards.", nameof(changedAtUtc));
        }

        if (State == state)
        {
            throw new InvalidOperationException("Tenant is already in the requested state.");
        }

        if (State == TenantState.Archived)
        {
            throw new InvalidOperationException("An archived tenant cannot change state.");
        }

        State = state;
        Touch(changedAtUtc);
    }

    private static string Required(string value, string parameterName, int maximumLength)
    {
        var normalized = value.Trim();
        if (normalized.Length is 0 || normalized.Length > maximumLength)
        {
            throw new ArgumentException($"Value must contain 1–{maximumLength} characters.", parameterName);
        }

        return normalized;
    }

    private static string NormalizeSlug(string value)
    {
        var normalized = Required(value, nameof(value), 80).ToLowerInvariant();
        if (normalized.Length < 3 ||
            !char.IsAsciiLetterOrDigit(normalized[0]) ||
            !char.IsAsciiLetterOrDigit(normalized[^1]) ||
            normalized.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            throw new ArgumentException(
                "Tenant slug must contain 3–80 lowercase letters, digits, or hyphens and start/end with a letter or digit.",
                nameof(value));
        }

        return normalized;
    }

    private static string NormalizeTimeZone(string value)
    {
        var normalized = Required(value, nameof(value), 80);
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(normalized);
        }
        catch (TimeZoneNotFoundException exception)
        {
            throw new ArgumentException("Tenant time zone must be a recognized IANA time zone.", nameof(value), exception);
        }
        catch (InvalidTimeZoneException exception)
        {
            throw new ArgumentException("Tenant time zone is invalid.", nameof(value), exception);
        }

        return normalized;
    }

    private void EnsureMutable()
    {
        if (State == TenantState.Archived)
        {
            throw new InvalidOperationException("An archived tenant cannot be modified.");
        }
    }

    private void Touch(DateTimeOffset updatedAtUtc)
    {
        UpdatedAtUtc = updatedAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must have a UTC offset.", parameterName);
        }
    }
}
