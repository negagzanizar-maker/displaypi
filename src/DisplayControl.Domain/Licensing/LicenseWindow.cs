namespace DisplayControl.Domain.Licensing;

/// <summary>
/// Represents the authoritative UTC interval for one device licence.
/// The start is inclusive and the end is exclusive.
/// </summary>
public sealed record LicenseWindow
{
    public static readonly TimeSpan MaximumOfflineAllowance = TimeSpan.FromHours(24);

    public LicenseWindow(DateTimeOffset validFromUtc, DateTimeOffset expiresAtUtc)
    {
        EnsureUtc(validFromUtc, nameof(validFromUtc));
        EnsureUtc(expiresAtUtc, nameof(expiresAtUtc));

        if (expiresAtUtc <= validFromUtc)
        {
            throw new ArgumentException("Licence expiry must be later than its start.", nameof(expiresAtUtc));
        }

        ValidFromUtc = validFromUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public DateTimeOffset ValidFromUtc { get; }

    public DateTimeOffset ExpiresAtUtc { get; }

    public LicenseEffectiveState Evaluate(
        DateTimeOffset serverNowUtc,
        bool isSuspended = false,
        DateTimeOffset? revokedAtUtc = null)
    {
        EnsureUtc(serverNowUtc, nameof(serverNowUtc));

        if (revokedAtUtc is not null)
        {
            EnsureUtc(revokedAtUtc.Value, nameof(revokedAtUtc));
            return LicenseEffectiveState.Revoked;
        }

        if (isSuspended)
        {
            return LicenseEffectiveState.Suspended;
        }

        if (serverNowUtc < ValidFromUtc)
        {
            return LicenseEffectiveState.Future;
        }

        return serverNowUtc < ExpiresAtUtc
            ? LicenseEffectiveState.Active
            : LicenseEffectiveState.Expired;
    }

    public DateTimeOffset CalculateLeaseExpiry(
        DateTimeOffset serverNowUtc,
        TimeSpan offlineAllowance,
        DateTimeOffset? earlierAuthorizationBoundaryUtc = null)
    {
        EnsureUtc(serverNowUtc, nameof(serverNowUtc));

        if (offlineAllowance <= TimeSpan.Zero || offlineAllowance > MaximumOfflineAllowance)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offlineAllowance),
                $"Offline allowance must be positive and no greater than {MaximumOfflineAllowance.TotalHours} hours.");
        }

        if (Evaluate(serverNowUtc) is not LicenseEffectiveState.Active)
        {
            throw new InvalidOperationException("A lease can be issued only while the licence interval is active.");
        }

        var leaseExpiry = Min(ExpiresAtUtc, serverNowUtc.Add(offlineAllowance));

        if (earlierAuthorizationBoundaryUtc is not null)
        {
            EnsureUtc(earlierAuthorizationBoundaryUtc.Value, nameof(earlierAuthorizationBoundaryUtc));
            leaseExpiry = Min(leaseExpiry, earlierAuthorizationBoundaryUtc.Value);
        }

        if (leaseExpiry <= serverNowUtc)
        {
            throw new InvalidOperationException("The authorization boundary does not permit a positive lease interval.");
        }

        return leaseExpiry;
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left <= right ? left : right;

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Authorization timestamps must have a UTC offset.", parameterName);
        }
    }
}
