using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;

namespace DisplayControl.DeviceAgent;

public sealed class DeviceInventoryCollector(IOptions<AgentRuntimeOptions> options)
{
    private readonly AgentRuntimeOptions _options = options.Value;

    public async Task<DeviceInventorySnapshot> CollectAsync(CancellationToken cancellationToken)
    {
        var serial = await ReadRaspberryPiSerialAsync(cancellationToken) ?? _options.DevelopmentSerialNumber;
        if (string.IsNullOrWhiteSpace(serial))
        {
            throw new InvalidOperationException("No Raspberry Pi serial number was found and no development override is configured.");
        }

        var root = Path.GetPathRoot(AppContext.BaseDirectory)
            ?? throw new InvalidOperationException("The agent data volume root could not be resolved.");
        var drive = new DriveInfo(root);
        var networks = NetworkInterface.GetAllNetworkInterfaces()
            .Where(value => value.OperationalStatus == OperationalStatus.Up &&
                value.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                !string.IsNullOrWhiteSpace(value.Name) &&
                value.Name.Trim().Length <= 64)
            .Select(value => new DeviceNetworkSnapshot(
                value.Name.Trim(),
                NormalizeMac(value.GetPhysicalAddress()),
                value.GetIPProperties().UnicastAddresses
                    .Select(address => address.Address)
                    .Where(address => address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                    .Select(address => address.ToString())
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray()))
            .Where(value => value.LocalAddresses.Count > 0)
            .OrderBy(value => value.InterfaceName, StringComparer.Ordinal)
            .Take(16)
            .ToArray();
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0] ?? "0.0.0";
        return new DeviceInventorySnapshot(
            serial.Trim().ToUpperInvariant(),
            Environment.MachineName,
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
            version,
            version,
            drive.TotalSize,
            drive.AvailableFreeSpace,
            networks);
    }

    private static async Task<string?> ReadRaspberryPiSerialAsync(CancellationToken cancellationToken)
    {
        const string cpuInfoPath = "/proc/cpuinfo";
        if (!File.Exists(cpuInfoPath))
        {
            return null;
        }

        var lines = await File.ReadAllLinesAsync(cpuInfoPath, cancellationToken);
        var serialLine = lines.FirstOrDefault(line => line.StartsWith("Serial", StringComparison.OrdinalIgnoreCase));
        var separator = serialLine?.IndexOf(':') ?? -1;
        return separator >= 0 ? serialLine![(separator + 1)..].Trim() : null;
    }

    private static string? NormalizeMac(PhysicalAddress address)
    {
        var value = Convert.ToHexString(address.GetAddressBytes());
        return value.Length == 12 ? value : null;
    }
}

public sealed record DeviceInventorySnapshot(
    string SerialNumber,
    string Hostname,
    string OsDescription,
    string Architecture,
    string AgentVersion,
    string PlayerVersion,
    long DiskCapacityBytes,
    long FreeDiskBytes,
    IReadOnlyList<DeviceNetworkSnapshot> NetworkInterfaces);

public sealed record DeviceNetworkSnapshot(
    string InterfaceName,
    string? MacAddress,
    IReadOnlyList<string> LocalAddresses);
