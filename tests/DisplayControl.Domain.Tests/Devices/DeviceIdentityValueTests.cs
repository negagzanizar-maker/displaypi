using DisplayControl.Domain.Devices;

namespace DisplayControl.Domain.Tests.Devices;

public sealed class DeviceIdentityValueTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CertificateRequiresExactSha256Values()
    {
        Assert.Throws<ArgumentException>(() => new DeviceCertificate(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "01AB",
            new byte[31],
            new byte[32],
            new byte[100],
            Now,
            Now.AddDays(30),
            Now));
    }

    [Fact]
    public void CertificateCopiesCryptographicDigests()
    {
        var thumbprint = Enumerable.Repeat((byte)0x11, 32).ToArray();
        var spki = Enumerable.Repeat((byte)0x22, 32).ToArray();
        var certificate = new DeviceCertificate(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            " 01ab ",
            thumbprint,
            spki,
            new byte[100],
            Now,
            Now.AddDays(30),
            Now);

        thumbprint[0] = 0;
        spki[0] = 0;

        Assert.Equal("01AB", certificate.CertificateSerialNumber);
        Assert.Equal(0x11, certificate.ThumbprintSha256[0]);
        Assert.Equal(0x22, certificate.SubjectPublicKeyInfoSha256[0]);
        Assert.Equal(DeviceCertificateState.Active, certificate.State);
    }

    [Theory]
    [InlineData("001122AABBCC")]
    [InlineData(null)]
    public void NetworkInterfaceAcceptsNormalizedOrAbsentMac(string? macAddress)
    {
        var networkInterface = new DeviceNetworkInterface(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            " eth0 ",
            macAddress,
            "[]",
            Now);

        Assert.Equal("eth0", networkInterface.InterfaceName);
        Assert.Equal(macAddress, networkInterface.MacAddressNormalized);
    }

    [Fact]
    public void NetworkInterfaceRejectsFormattedMac()
    {
        Assert.Throws<ArgumentException>(() => new DeviceNetworkInterface(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "eth0",
            "00:11:22:AA:BB:CC",
            "[]",
            Now));
    }
}
