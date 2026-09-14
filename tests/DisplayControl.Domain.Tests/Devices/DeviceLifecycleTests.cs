using DisplayControl.Domain.Devices;

namespace DisplayControl.Domain.Tests.Devices;

public sealed class DeviceLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EnrollmentNormalizesInventoryAndActivatesDevice()
    {
        var device = new Device(Guid.NewGuid(), Guid.NewGuid(), " Lobby ", Now);

        device.CompleteEnrollment(
            " 10000000abcd1234 ",
            " pi-lobby ",
            "Raspberry Pi OS",
            "arm64",
            "1.0.0",
            "1.0.0",
            64L * 1024 * 1024 * 1024,
            Now.AddMinutes(1));

        Assert.Equal(DeviceLifecycleState.Active, device.State);
        Assert.Equal("10000000ABCD1234", device.SerialNumberNormalized);
        Assert.Equal("pi-lobby", device.Hostname);
        Assert.Equal(Now.AddMinutes(1), device.LastSeenUtc);
    }

    [Fact]
    public void RetiredDeviceCannotBeRenamedOrReactivated()
    {
        var device = new Device(Guid.NewGuid(), Guid.NewGuid(), "Lobby", Now);
        device.Retire(Now.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => device.Rename("Other", Now.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => device.Reactivate(Now.AddMinutes(2)));
    }
}
