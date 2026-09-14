using DisplayControl.Api.Operations;
using Microsoft.Extensions.Configuration;

namespace DisplayControl.IntegrationTests.Security;

public sealed class ProductionConfigurationValidatorTests
{
    [Fact]
    public void AcceptsExplicitHostsVerifiedDatabaseTlsAndSeparatedSecurityPaths()
    {
        using var files = new ProductionPathFixture();
        var configuration = files.CreateConfiguration("admin.example.test;devices.example.test");

        ProductionConfigurationValidator.Validate(configuration, VerifiedDatabaseConnectionString);
    }

    [Fact]
    public void RejectsWildcardHostsAndUnverifiedDatabaseTransport()
    {
        using var files = new ProductionPathFixture();
        var wildcard = files.CreateConfiguration("*");
        Assert.Throws<InvalidOperationException>(() =>
            ProductionConfigurationValidator.Validate(wildcard, VerifiedDatabaseConnectionString));

        var explicitHosts = files.CreateConfiguration("admin.example.test;devices.example.test");
        Assert.Throws<InvalidOperationException>(() =>
            ProductionConfigurationValidator.Validate(
                explicitHosts,
                "Server=tcp:db.example.test,1433;Database=display;User Id=runtime;Password=test;Encrypt=False;TrustServerCertificate=True"));
    }

    [Fact]
    public void RejectsSecurityMaterialStoredInsideTheContentDirectory()
    {
        using var files = new ProductionPathFixture();
        var configuration = files.CreateConfiguration(
            "admin.example.test",
            dataProtectionDirectory: Path.Combine(files.ContentRoot, "data-protection"));

        Assert.Throws<InvalidOperationException>(() =>
            ProductionConfigurationValidator.Validate(configuration, VerifiedDatabaseConnectionString));
    }

    [Fact]
    public void RejectsWildcardSubdomainsAndPlaceholderSecrets()
    {
        using var files = new ProductionPathFixture();
        var wildcard = files.CreateConfiguration("*.example.test");
        Assert.Throws<InvalidOperationException>(() =>
            ProductionConfigurationValidator.Validate(wildcard, VerifiedDatabaseConnectionString));

        var placeholder = files.CreateConfiguration(
            "admin.example.test",
            dataProtectionCertificatePassword: "REPLACE_WITH_SECRET");
        Assert.Throws<InvalidOperationException>(() =>
            ProductionConfigurationValidator.Validate(placeholder, VerifiedDatabaseConnectionString));
    }

    private const string VerifiedDatabaseConnectionString =
        "Server=tcp:db.example.test,1433;Database=display;User Id=runtime;Password=test;Encrypt=True;TrustServerCertificate=False";

    private sealed class ProductionPathFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "display-control-production-validation",
            Guid.NewGuid().ToString("N"));

        public ProductionPathFixture()
        {
            ContentRoot = Path.Combine(_root, "content");
            Directory.CreateDirectory(ContentRoot);
            Directory.CreateDirectory(Path.Combine(_root, "data-protection"));
            Directory.CreateDirectory(Path.Combine(_root, "secrets"));
            File.WriteAllText(Path.Combine(_root, "secrets", "data-protection.pfx"), "fixture");
            File.WriteAllText(Path.Combine(_root, "secrets", "device-ca.pfx"), "fixture");
            File.WriteAllText(Path.Combine(_root, "secrets", "lease.pem"), "fixture");
            File.WriteAllText(Path.Combine(_root, "secrets", "public-tls.pfx"), "fixture");
        }

        public string ContentRoot { get; }

        public IConfiguration CreateConfiguration(
            string allowedHosts,
            string? dataProtectionDirectory = null,
            string dataProtectionCertificatePassword = "fixture-password") =>
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = allowedHosts,
                ["ContentStorage:RootDirectory"] = ContentRoot,
                ["Security:DataProtectionKeyDirectory"] =
                    dataProtectionDirectory ?? Path.Combine(_root, "data-protection"),
                ["Security:DataProtectionCertificate:PfxPath"] =
                    Path.Combine(_root, "secrets", "data-protection.pfx"),
                ["Security:DataProtectionCertificate:PfxPassword"] = dataProtectionCertificatePassword,
                ["Security:DeviceCertificateAuthority:PfxPath"] =
                    Path.Combine(_root, "secrets", "device-ca.pfx"),
                ["Security:DeviceCertificateAuthority:PfxPassword"] = "fixture-password",
                ["Security:LicenseSigningKey:PrivateKeyPath"] = Path.Combine(_root, "secrets", "lease.pem"),
                ["Security:LicenseSigningKey:Password"] = "fixture-password",
                ["Kestrel:Certificates:Default:Path"] = Path.Combine(_root, "secrets", "public-tls.pfx"),
                ["Kestrel:Certificates:Default:Password"] = "fixture-password"
            }).Build();

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
