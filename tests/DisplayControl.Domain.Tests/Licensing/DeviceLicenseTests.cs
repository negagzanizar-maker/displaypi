using DisplayControl.Domain.Licensing;

namespace DisplayControl.Domain.Tests.Licensing;

public sealed class DeviceLicenseTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LifecycleDerivesStateAndRotatesConcurrencyToken()
    {
        var license = new DeviceLicense(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Now,
            Now.AddDays(30),
            Now);
        var initialToken = license.ConcurrencyToken;

        license.Suspend(Now.AddMinutes(1));

        Assert.Equal(LicenseEffectiveState.Suspended, license.EvaluateAt(Now.AddDays(1)));
        Assert.NotEqual(initialToken, license.ConcurrencyToken);

        license.Reactivate(Now.AddMinutes(2));
        Assert.Equal(LicenseEffectiveState.Active, license.EvaluateAt(Now.AddDays(1)));
    }

    [Fact]
    public void RevokedLicenseIsTerminal()
    {
        var license = new DeviceLicense(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Now,
            Now.AddDays(30),
            Now);
        license.Revoke(Now.AddMinutes(1));

        Assert.Equal(LicenseEffectiveState.Revoked, license.EvaluateAt(Now.AddDays(1)));
        Assert.Throws<InvalidOperationException>(() => license.Renew(Now.AddDays(60), Now.AddMinutes(2)));
    }

    [Fact]
    public void RenewalMustExtendExpiry()
    {
        var license = new DeviceLicense(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Now,
            Now.AddDays(30),
            Now);

        Assert.Throws<ArgumentException>(() => license.Renew(Now.AddDays(20), Now.AddMinutes(1)));
    }

    [Fact]
    public void TransferWaitsForLatestOutstandingSourceLease()
    {
        var sourceDeviceId = Guid.NewGuid();
        var destinationDeviceId = Guid.NewGuid();
        var license = new DeviceLicense(
            Guid.NewGuid(),
            Guid.NewGuid(),
            sourceDeviceId,
            Now.AddDays(-1),
            Now.AddDays(30),
            Now.AddDays(-1));
        license.RecordLeaseIssued(Now.AddHours(12), Now);

        var destinationStartsAtUtc = license.BeginTransfer(destinationDeviceId, Now.AddMinutes(1));

        Assert.Equal(Now.AddHours(12), destinationStartsAtUtc);
        Assert.Equal(LicenseControlState.TransferPending, license.ControlState);
        Assert.Equal(destinationDeviceId, license.TransferDestinationDeviceId);
        Assert.Equal(LicenseEffectiveState.Suspended, license.EvaluateAt(Now.AddMinutes(2)));
    }

    [Fact]
    public void ShorterLeaseCannotMoveTransferBoundaryBackwards()
    {
        var license = new DeviceLicense(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now, Now.AddDays(30), Now);
        license.RecordLeaseIssued(Now.AddHours(24), Now);
        license.RecordLeaseIssued(Now.AddHours(1), Now.AddMinutes(1));

        Assert.Equal(Now.AddHours(24), license.LatestIssuedLeaseExpiryUtc);
        Assert.Equal(Now.AddHours(24), license.BeginTransfer(Guid.NewGuid(), Now.AddMinutes(2)));
    }

    [Fact]
    public void LeaseTelemetryDoesNotInvalidateAdministrativeConcurrencyToken()
    {
        var license = new DeviceLicense(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now, Now.AddDays(30), Now);
        var administrativeToken = license.ConcurrencyToken;

        license.RecordLeaseIssued(Now.AddHours(24), Now.AddMinutes(1));

        Assert.Equal(administrativeToken, license.ConcurrencyToken);
        Assert.Equal(Now.AddMinutes(1), license.UpdatedAtUtc);
    }

    [Fact]
    public void TransferredSourceCannotBeSuspendedRenewedOrReactivated()
    {
        var license = new DeviceLicense(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now, Now.AddDays(30), Now);
        license.BeginTransfer(Guid.NewGuid(), Now.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => license.Suspend(Now.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => license.Renew(Now.AddDays(60), Now.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => license.Reactivate(Now.AddMinutes(2)));
        Assert.Equal(LicenseControlState.TransferPending, license.ControlState);
        license.Revoke(Now.AddMinutes(3));
        Assert.Equal(LicenseControlState.Revoked, license.ControlState);
    }
}
