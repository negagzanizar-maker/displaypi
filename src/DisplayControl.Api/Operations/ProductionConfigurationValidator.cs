using Microsoft.Data.SqlClient;

namespace DisplayControl.Api.Operations;

public static class ProductionConfigurationValidator
{
    public static void Validate(IConfiguration configuration, string databaseConnectionString)
    {
        ValidateAllowedHosts(configuration["AllowedHosts"]);
        ValidateDatabaseTransport(databaseConnectionString);

        var dataProtectionDirectory = RequiredAbsolutePath(
            configuration,
            "Security:DataProtectionKeyDirectory",
            mustExist: false);
        var contentRoot = RequiredAbsolutePath(
            configuration,
            "ContentStorage:RootDirectory",
            mustExist: false);
        var dataProtectionCertificate = RequiredAbsolutePath(
            configuration,
            "Security:DataProtectionCertificate:PfxPath",
            mustExist: true);
        var deviceCertificateAuthority = RequiredAbsolutePath(
            configuration,
            "Security:DeviceCertificateAuthority:PfxPath",
            mustExist: true);
        var licenseSigningKey = RequiredAbsolutePath(
            configuration,
            "Security:LicenseSigningKey:PrivateKeyPath",
            mustExist: true);
        var publicTlsCertificate = RequiredAbsolutePath(
            configuration,
            "Kestrel:Certificates:Default:Path",
            mustExist: true);

        RequireSecret(configuration, "Security:DataProtectionCertificate:PfxPassword");
        RequireSecret(configuration, "Security:DeviceCertificateAuthority:PfxPassword");
        RequireSecret(configuration, "Security:LicenseSigningKey:Password");
        RequireSecret(configuration, "Kestrel:Certificates:Default:Password");
        ValidateOptionalWorkerDatabase(
            configuration,
            "Notifications:DeliveryEnabled",
            "NotificationDatabase");
        ValidateOptionalWorkerDatabase(
            configuration,
            "Operations:Retention:Enabled",
            "MaintenanceDatabase");
        RequireDistinctPaths(
            dataProtectionDirectory,
            contentRoot,
            dataProtectionCertificate,
            deviceCertificateAuthority,
            licenseSigningKey,
            publicTlsCertificate);
    }

    private static void ValidateAllowedHosts(string? allowedHosts)
    {
        var values = (allowedHosts ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length == 0 || values.Any(value =>
                value is "*" or "+" ||
                value.Contains('*') ||
                value.Contains('/') ||
                value.Any(char.IsWhiteSpace)))
        {
            throw new InvalidOperationException(
                "Production AllowedHosts must explicitly list the public administration and device hosts.");
        }
    }

    private static void ValidateDatabaseTransport(string connectionString)
    {
        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException("The production database connection string is invalid.", exception);
        }

        if (!builder.Encrypt || builder.TrustServerCertificate)
        {
            throw new InvalidOperationException(
                "Production SQL Server must use Encrypt=true and TrustServerCertificate=false.");
        }
    }

    private static void ValidateOptionalWorkerDatabase(
        IConfiguration configuration,
        string enabledKey,
        string connectionStringName)
    {
        if (!configuration.GetValue<bool>(enabledKey))
        {
            return;
        }

        var connectionString = configuration.GetConnectionString(connectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Enabled production worker requires ConnectionStrings:{connectionStringName}.");
        }

        ValidateDatabaseTransport(connectionString);
    }

    private static string RequiredAbsolutePath(
        IConfiguration configuration,
        string key,
        bool mustExist)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
        {
            throw new InvalidOperationException($"Production setting {key} must be an absolute path.");
        }

        var fullPath = Path.GetFullPath(value);
        if (mustExist && !File.Exists(fullPath))
        {
            throw new InvalidOperationException($"Production file configured by {key} does not exist.");
        }

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static void RequireSecret(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value) ||
            value.StartsWith("REPLACE", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Production secret {key} is required and cannot be a placeholder.");
        }
    }

    private static void RequireDistinctPaths(params string[] paths)
    {
        for (var left = 0; left < paths.Length; left++)
        {
            for (var right = left + 1; right < paths.Length; right++)
            {
                if (PathsOverlap(paths[left], paths[right]))
                {
                    throw new InvalidOperationException(
                        "Production content, Data Protection, certificate, and signing-key paths must be isolated from one another.");
                }
            }
        }
    }

    private static bool PathsOverlap(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(left, right, comparison) ||
            left.StartsWith(right + Path.DirectorySeparatorChar, comparison) ||
            right.StartsWith(left + Path.DirectorySeparatorChar, comparison);
    }
}
