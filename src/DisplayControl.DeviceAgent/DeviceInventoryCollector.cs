using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
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
        var networks = await CollectNetworksAsync(cancellationToken);
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

    private static async Task<DeviceNetworkSnapshot[]> CollectNetworksAsync(CancellationToken cancellationToken)
    {
        try
        {
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
            return networks.Length > 0 || !OperatingSystem.IsLinux()
                ? networks
                : await CollectLinuxNetworksAsync(cancellationToken);
        }
        catch (NetworkInformationException)
        {
            return await CollectLinuxNetworksAsync(cancellationToken);
        }
    }

    private static async Task<DeviceNetworkSnapshot[]> CollectLinuxNetworksAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
        {
            return [];
        }

        var executable = File.Exists("/usr/sbin/ip") ? "/usr/sbin/ip" : "/usr/bin/ip";
        if (!File.Exists(executable))
        {
            return [];
        }

        try
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("-j");
            startInfo.ArgumentList.Add("address");
            startInfo.ArgumentList.Add("show");
            startInfo.ArgumentList.Add("up");
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return [];
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
            {
                return [];
            }

            using var document = JsonDocument.Parse(output);
            return document.RootElement.EnumerateArray()
                .Select(value => new
                {
                    Name = value.TryGetProperty("ifname", out var name) ? name.GetString()?.Trim() : null,
                    Mac = value.TryGetProperty("address", out var mac) ? NormalizeMac(mac.GetString()) : null,
                    Addresses = value.TryGetProperty("addr_info", out var addresses)
                        ? addresses.EnumerateArray()
                            .Where(address => address.TryGetProperty("family", out var family) &&
                                family.GetString() is "inet" or "inet6")
                            .Select(address => address.TryGetProperty("local", out var local) ? local.GetString() : null)
                            .OfType<string>()
                            .Distinct(StringComparer.Ordinal)
                            .Order(StringComparer.Ordinal)
                            .ToArray()
                        : []
                })
                .Where(value => value.Name is not null && value.Name != "lo" && value.Name.Length <= 64 && value.Addresses.Length > 0)
                .Select(value => new DeviceNetworkSnapshot(value.Name!, value.Mac, value.Addresses))
                .OrderBy(value => value.InterfaceName, StringComparer.Ordinal)
                .Take(16)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
        {
            return [];
        }
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

    private static string? NormalizeMac(string? address)
    {
        var value = address?.Replace(":", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        return value?.Length == 12 ? value : null;
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
