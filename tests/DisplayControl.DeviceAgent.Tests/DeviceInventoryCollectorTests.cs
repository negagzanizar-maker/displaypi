using System.Net;

using Microsoft.Extensions.Options;

namespace DisplayControl.DeviceAgent.Tests;

public sealed class DeviceInventoryCollectorTests
{
    [Fact]
    public async Task CollectedInventorySatisfiesDeviceProtocolBounds()
    {
        var options = Options.Create(new AgentRuntimeOptions
        {
            ServerBaseAddress = new Uri("https://localhost"),
            StateDirectory = Path.GetFullPath("inventory-collector-test"),
            DevelopmentSerialNumber = "VIRTUALPI0001"
        });

        var inventory = await new DeviceInventoryCollector(options).CollectAsync(CancellationToken.None);

        Assert.InRange(inventory.SerialNumber.Length, 1, 32);
        Assert.Equal(Environment.MachineName, inventory.Hostname);
        Assert.InRange(inventory.NetworkInterfaces.Count, 0, 16);
        Assert.All(inventory.NetworkInterfaces, network =>
        {
            Assert.InRange(network.InterfaceName.Length, 1, 64);
            Assert.NotEmpty(network.LocalAddresses);
            Assert.All(network.LocalAddresses, address => Assert.True(IPAddress.TryParse(address, out _)));
        });
    }
}
