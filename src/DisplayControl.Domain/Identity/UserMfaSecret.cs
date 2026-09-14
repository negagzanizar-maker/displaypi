namespace DisplayControl.Domain.Identity;

public sealed class UserMfaSecret
{
    private UserMfaSecret()
    {
    }

    public UserMfaSecret(
        Guid userId,
        byte[] protectedSecret,
        string protectionScheme,
        DateTimeOffset createdAtUtc)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User identifier cannot be empty.", nameof(userId));
        }

        ArgumentNullException.ThrowIfNull(protectedSecret);
        if (protectedSecret.Length is 0 or > 4096)
        {
            throw new ArgumentException("Protected MFA secret must contain 1-4096 bytes.", nameof(protectedSecret));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(protectionScheme);
        var scheme = protectionScheme.Trim();
        if (scheme.Length is < 1 or > 128)
        {
            throw new ArgumentException("Protection scheme must contain 1-128 characters.", nameof(protectionScheme));
        }

        EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        UserId = userId;
        ProtectedSecret = protectedSecret.ToArray();
        ProtectionScheme = scheme;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    public Guid UserId { get; private init; }

    public byte[] ProtectedSecret { get; private set; } = [];

    public string ProtectionScheme { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? ConfirmedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public long? LastAcceptedTimeStep { get; private set; }

    public Guid ConcurrencyToken { get; private set; }

    public void Confirm(DateTimeOffset confirmedAtUtc)
    {
        EnsureUtc(confirmedAtUtc, nameof(confirmedAtUtc));
        if (ConfirmedAtUtc is not null)
        {
            throw new InvalidOperationException("MFA secret is already confirmed.");
        }

        if (confirmedAtUtc < CreatedAtUtc)
        {
            throw new ArgumentException("Confirmation cannot predate secret creation.", nameof(confirmedAtUtc));
        }

        ConfirmedAtUtc = confirmedAtUtc;
        UpdatedAtUtc = confirmedAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    public void AcceptTimeStep(long timeStep, DateTimeOffset acceptedAtUtc)
    {
        EnsureUtc(acceptedAtUtc, nameof(acceptedAtUtc));
        if (ConfirmedAtUtc is null)
        {
            throw new InvalidOperationException("MFA secret must be confirmed before accepting codes.");
        }

        if (timeStep < 0 || (LastAcceptedTimeStep is not null && timeStep <= LastAcceptedTimeStep))
        {
            throw new InvalidOperationException("TOTP time step has already been used or is invalid.");
        }

        LastAcceptedTimeStep = timeStep;
        UpdatedAtUtc = acceptedAtUtc;
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
