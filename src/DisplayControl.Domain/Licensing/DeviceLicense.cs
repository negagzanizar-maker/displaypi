using DisplayControl.Domain.Tenancy;

namespace DisplayControl.Domain.Licensing;

public sealed class DeviceLicense : TenantOwnedEntity
{
    private DeviceLicense()
    {
    }

    public DeviceLicense(
        Guid id,
        Guid tenantId,
        Guid deviceId,
        DateTimeOffset validFromUtc,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || deviceId == Guid.Empty)
        {
            throw new ArgumentException("Licence, tenant, and device identifiers cannot be empty.");
        }

        _ = new LicenseWindow(validFromUtc, expiresAtUtc);
        if (createdAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Creation timestamp must have a UTC offset.", nameof(createdAtUtc));
        }

        Id = id;
        TenantId = tenantId;
        DeviceId = deviceId;
        ValidFromUtc = validFromUtc;
        ExpiresAtUtc = expiresAtUtc;
        ControlState = LicenseControlState.Enabled;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    public Guid DeviceId { get; private set; }

    public DateTimeOffset ValidFromUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public LicenseControlState ControlState { get; private set; }

    public DateTimeOffset? LatestIssuedLeaseExpiryUtc { get; private set; }

    public Guid? TransferDestinationDeviceId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public Guid ConcurrencyToken { get; private set; }

    public LicenseEffectiveState EvaluateAt(DateTimeOffset nowUtc) => new LicenseWindow(ValidFromUtc, ExpiresAtUtc).Evaluate(
        nowUtc,
        ControlState is LicenseControlState.Suspended or LicenseControlState.TransferPending,
        ControlState == LicenseControlState.Revoked ? UpdatedAtUtc : null);

    public void Renew(DateTimeOffset expiresAtUtc, DateTimeOffset updatedAtUtc)
    {
        EnsureNotRevoked();
        _ = new LicenseWindow(ValidFromUtc, expiresAtUtc);
        if (expiresAtUtc <= ExpiresAtUtc)
        {
            throw new ArgumentException("A renewal must extend the current licence expiry.", nameof(expiresAtUtc));
        }

        ExpiresAtUtc = expiresAtUtc;
        Touch(updatedAtUtc);
    }

    public void Suspend(DateTimeOffset updatedAtUtc)
    {
        EnsureNotRevoked();
        ControlState = LicenseControlState.Suspended;
        Touch(updatedAtUtc);
    }

    public void Reactivate(DateTimeOffset updatedAtUtc)
    {
        if (ControlState != LicenseControlState.Suspended)
        {
            throw new InvalidOperationException("Only a suspended licence can be reactivated.");
        }

        ControlState = LicenseControlState.Enabled;
        Touch(updatedAtUtc);
    }

    public void Revoke(DateTimeOffset updatedAtUtc)
    {
        if (ControlState == LicenseControlState.Revoked)
        {
            return;
        }

        ControlState = LicenseControlState.Revoked;
        Touch(updatedAtUtc);
    }

    public DateTimeOffset BeginTransfer(Guid destinationDeviceId, DateTimeOffset updatedAtUtc)
    {
        EnsureNotRevoked();
        if (destinationDeviceId == Guid.Empty || destinationDeviceId == DeviceId)
        {
            throw new ArgumentException("Transfer destination must be a different non-empty device.", nameof(destinationDeviceId));
        }

        if (ControlState != LicenseControlState.Enabled)
        {
            throw new InvalidOperationException("Only an enabled licence can begin transfer.");
        }

        if (EvaluateAt(updatedAtUtc) != LicenseEffectiveState.Active)
        {
            throw new InvalidOperationException("Only a currently active licence can be transferred.");
        }

        var destinationValidFromUtc = LatestIssuedLeaseExpiryUtc is { } leaseExpiry && leaseExpiry > updatedAtUtc
            ? leaseExpiry
            : updatedAtUtc;
        if (destinationValidFromUtc >= ExpiresAtUtc)
        {
            throw new InvalidOperationException("No licence interval remains after the outstanding source lease.");
        }

        TransferDestinationDeviceId = destinationDeviceId;
        ControlState = LicenseControlState.TransferPending;
        Touch(updatedAtUtc);
        return destinationValidFromUtc;
    }

    public void RecordLeaseIssued(DateTimeOffset leaseExpiresAtUtc, DateTimeOffset issuedAtUtc)
    {
        if (EvaluateAt(issuedAtUtc) != LicenseEffectiveState.Active ||
            leaseExpiresAtUtc <= issuedAtUtc ||
            leaseExpiresAtUtc > ExpiresAtUtc ||
            leaseExpiresAtUtc > issuedAtUtc.Add(LicenseWindow.MaximumOfflineAllowance))
        {
            throw new InvalidOperationException("The issued lease is outside the licence authorization bounds.");
        }

        LatestIssuedLeaseExpiryUtc = LatestIssuedLeaseExpiryUtc is { } outstanding && outstanding > leaseExpiresAtUtc
            ? outstanding
            : leaseExpiresAtUtc;
        TouchLeaseTelemetry(issuedAtUtc);
    }

    private void EnsureNotRevoked()
    {
        if (ControlState is LicenseControlState.Revoked or LicenseControlState.TransferPending)
        {
            throw new InvalidOperationException("A revoked or transferred licence cannot be modified.");
        }
    }

    private void Touch(DateTimeOffset updatedAtUtc)
    {
        TouchLeaseTelemetry(updatedAtUtc);
        ConcurrencyToken = Guid.NewGuid();
    }

    private void TouchLeaseTelemetry(DateTimeOffset updatedAtUtc)
    {
        if (updatedAtUtc.Offset != TimeSpan.Zero || updatedAtUtc < UpdatedAtUtc)
        {
            throw new ArgumentException("Licence timestamps must be monotonic UTC instants.", nameof(updatedAtUtc));
        }

        UpdatedAtUtc = updatedAtUtc;
    }
}
