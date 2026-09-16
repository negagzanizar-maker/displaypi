using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

var arguments = ParseArguments(args);
var databaseConnection = Required(arguments, "database-connection");
var outputRoot = Path.GetFullPath(Required(arguments, "output"));
var httpsHost = arguments.GetValueOrDefault("https-host");
var httpsPort = int.Parse(arguments.GetValueOrDefault("https-port") ?? "7443", System.Globalization.CultureInfo.InvariantCulture);
if (httpsPort is < 1 or > 65535)
{
    throw new InvalidOperationException("HTTPS port must be between 1 and 65535.");
}

var deviceCaPath = Path.Combine(outputRoot, "device-ca.pfx");
var leaseKeyPath = Path.Combine(outputRoot, "license-signing-key.pem");
var launcherPath = Path.Combine(outputRoot, "run-api.local.ps1");
var httpsCaPath = Path.Combine(outputRoot, "field-test-server-ca.crt");
var httpsPfxPath = Path.Combine(outputRoot, "field-test-server.pfx");
var targets = new List<string> { deviceCaPath, leaseKeyPath, launcherPath };
if (!string.IsNullOrWhiteSpace(httpsHost))
{
    targets.Add(httpsCaPath);
    targets.Add(httpsPfxPath);
}
foreach (var target in targets)
{
    if (File.Exists(target))
    {
        throw new IOException($"Refusing to overwrite existing security material: {target}");
    }
}

Directory.CreateDirectory(outputRoot);
var dataProtectionPath = Path.Combine(outputRoot, "data-protection");
var contentPath = Path.Combine(outputRoot, "private-content");
Directory.CreateDirectory(dataProtectionPath);
Directory.CreateDirectory(contentPath);

var deviceCaPassword = RandomSecret();
var leasePassword = RandomSecret();
var tokenPepper = RandomSecret();
var platformBootstrapToken = RandomSecret();
var platformBootstrapDigest = Convert.ToBase64String(
    SHA256.HashData(Encoding.UTF8.GetBytes(platformBootstrapToken)));
var now = DateTimeOffset.UtcNow;

using (var deviceCaKey = ECDsa.Create(ECCurve.NamedCurves.nistP256))
{
    var request = new CertificateRequest(
        "CN=Display Control Development Device CA, O=Local Development",
        deviceCaKey,
        HashAlgorithmName.SHA256);
    request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
    request.CertificateExtensions.Add(new X509KeyUsageExtension(
        X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
        true));
    using var authority = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(2));
    File.WriteAllBytes(deviceCaPath, authority.Export(X509ContentType.Pfx, deviceCaPassword));
}

using (var leaseKey = ECDsa.Create(ECCurve.NamedCurves.nistP256))
{
    var pbe = new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 210_000);
    var encrypted = leaseKey.ExportEncryptedPkcs8PrivateKey(leasePassword, pbe);
    File.WriteAllText(leaseKeyPath, PemEncoding.WriteString("ENCRYPTED PRIVATE KEY", encrypted));
    CryptographicOperations.ZeroMemory(encrypted);
}

string? httpsPassword = null;
if (!string.IsNullOrWhiteSpace(httpsHost))
{
    httpsPassword = RandomSecret();
    using var authorityKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var authorityRequest = new CertificateRequest(
        "CN=Display Control Field Test TLS CA, O=Local Field Test",
        authorityKey,
        HashAlgorithmName.SHA256);
    authorityRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
    authorityRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
        X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
        true));
    using var authority = authorityRequest.CreateSelfSigned(now.AddMinutes(-5), now.AddDays(30));
    File.WriteAllText(httpsCaPath, PemEncoding.WriteString("CERTIFICATE", authority.RawData));

    using var serverKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var serverRequest = new CertificateRequest($"CN={httpsHost}", serverKey, HashAlgorithmName.SHA256);
    serverRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
    serverRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
    var usages = new OidCollection { new("1.3.6.1.5.5.7.3.1") };
    serverRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, true));
    var names = new SubjectAlternativeNameBuilder();
    if (IPAddress.TryParse(httpsHost, out var address))
    {
        names.AddIpAddress(address);
    }
    else
    {
        names.AddDnsName(httpsHost);
    }
    names.AddDnsName("localhost");
    names.AddIpAddress(IPAddress.Loopback);
    serverRequest.CertificateExtensions.Add(names.Build(true));
    var serialNumber = RandomNumberGenerator.GetBytes(16);
    using var issued = serverRequest.Create(authority, now.AddMinutes(-5), now.AddDays(14), serialNumber);
    using var serverCertificate = issued.CopyWithPrivateKey(serverKey);
    File.WriteAllBytes(httpsPfxPath, serverCertificate.Export(X509ContentType.Pfx, httpsPassword));
}

var launcher = new StringBuilder()
    .AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"$env:ConnectionStrings__Database={Quote(databaseConnection)}")
    .AppendLine("$env:Database__Provider='SqlServer'")
    .AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"$env:Security__DataProtectionKeyDirectory={Quote(dataProtectionPath)}")
    .AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"$env:Security__TokenDigestPepperBase64={Quote(tokenPepper)}")
    .AppendLine("$env:Security__HumanAuthentication__RequireMfa='false'")
    .AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"$env:Security__PlatformBootstrapTokenSha256Base64={Quote(platformBootstrapDigest)}")
    .AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"$env:Security__DeviceCertificateAuthority__PfxPath={Quote(deviceCaPath)}")
    .AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"$env:Security__DeviceCertificateAuthority__PfxPassword={Quote(deviceCaPassword)}")
    .AppendLine("$env:Security__DeviceCertificateAuthority__IssuedLifetimeDays='90'")
    .AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"$env:Security__LicenseSigningKey__PrivateKeyPath={Quote(leaseKeyPath)}")
    .AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"$env:Security__LicenseSigningKey__Password={Quote(leasePassword)}")
    .AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"$env:ContentStorage__RootDirectory={Quote(contentPath)}")
    .AppendLine("$env:ContentScanning__ClamAv__Host='127.0.0.1'")
    .AppendLine("$env:ContentScanning__ClamAv__Port='3310'");
if (!string.IsNullOrWhiteSpace(httpsHost))
{
    launcher
        .AppendLine("$env:ASPNETCORE_ENVIRONMENT='Development'")
        .AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"$env:ASPNETCORE_URLS='https://0.0.0.0:{httpsPort}'")
        .AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"$env:Kestrel__Certificates__Default__Path={Quote(httpsPfxPath)}")
        .AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"$env:Kestrel__Certificates__Default__Password={Quote(httpsPassword!)}");
}
launcher.AppendLine("dotnet run --no-launch-profile --project src/DisplayControl.Api");
File.WriteAllText(launcherPath, launcher.ToString());

Console.WriteLine($"Development material created under {outputRoot}");
Console.WriteLine($"Run the ignored launcher: & '{launcherPath}'");
Console.WriteLine($"One-time platform bootstrap token (store temporarily, then discard): {platformBootstrapToken}");
if (!string.IsNullOrWhiteSpace(httpsHost))
{
    Console.WriteLine($"Field-test API URL: https://{httpsHost}:{httpsPort}");
    Console.WriteLine($"Copy this public CA certificate to the Pi: {httpsCaPath}");
}

static Dictionary<string, string> ParseArguments(string[] values)
{
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var index = 0; index < values.Length; index += 2)
    {
        if (index + 1 >= values.Length || !values[index].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException("Arguments must use --name value pairs.");
        }
        result.Add(values[index][2..], values[index + 1]);
    }
    return result;
}

static string Required(IReadOnlyDictionary<string, string> values, string name) =>
    values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new ArgumentException($"Missing required argument --{name}.");

static string RandomSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

static string Quote(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
