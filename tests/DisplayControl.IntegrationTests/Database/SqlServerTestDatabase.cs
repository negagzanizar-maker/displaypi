using DisplayControl.Application.Tenancy;
using DisplayControl.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace DisplayControl.IntegrationTests.Database;

internal sealed class SqlServerTestDatabase(string databaseName) : IAsyncDisposable
{
    public const string MigrationsAssembly = "DisplayControl.SqlServerMigrations";
    private const string ContainerPassword = "SqlServer_Container_Test_123!";
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU16-ubuntu-22.04")
        .WithPassword(ContainerPassword)
        .Build();

    public string OwnerConnectionString { get; private set; } = string.Empty;

    public async Task StartAsync()
    {
        await _container.StartAsync();

        await using var master = new SqlConnection(_container.GetConnectionString());
        await master.OpenAsync();
        await using (var createDatabase = new SqlCommand($"CREATE DATABASE [{databaseName}]", master))
        {
            await createDatabase.ExecuteNonQueryAsync();
        }

        OwnerConnectionString = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = databaseName,
            TrustServerCertificate = true
        }.ConnectionString;
    }

    public DbContextOptions<DisplayControlDbContext> CreateOwnerOptions() =>
        new DbContextOptionsBuilder<DisplayControlDbContext>()
            .UseSqlServer(
                OwnerConnectionString,
                sqlServer => sqlServer.MigrationsAssembly(MigrationsAssembly))
            .Options;

    public async Task MigrateAsync()
    {
        await using var context = new DisplayControlDbContext(CreateOwnerOptions(), NullTenant.Instance);
        await context.Database.MigrateAsync();
    }

    public async Task<string> ProvisionRuntimeLoginAsync(
        string login,
        string password,
        string? databaseGrantSql = null)
    {
        var escapedLogin = EscapeIdentifier(login);
        var escapedPassword = password.Replace("'", "''", StringComparison.Ordinal);

        await using (var master = new SqlConnection(_container.GetConnectionString()))
        {
            await master.OpenAsync();
            await using var createLogin = new SqlCommand(
                $"CREATE LOGIN [{escapedLogin}] WITH PASSWORD = '{escapedPassword}', CHECK_POLICY = OFF, CHECK_EXPIRATION = OFF;",
                master);
            await createLogin.ExecuteNonQueryAsync();
        }

        await using (var database = new SqlConnection(OwnerConnectionString))
        {
            await database.OpenAsync();
            var grants = databaseGrantSql ??
                $"""
                CREATE USER [{escapedLogin}] FOR LOGIN [{escapedLogin}];
                ALTER ROLE [db_datareader] ADD MEMBER [{escapedLogin}];
                ALTER ROLE [db_datawriter] ADD MEMBER [{escapedLogin}];
                GRANT EXECUTE ON SCHEMA::app TO [{escapedLogin}];
                """;
            await using var grant = new SqlCommand(grants, database);
            await grant.ExecuteNonQueryAsync();
        }

        return new SqlConnectionStringBuilder(OwnerConnectionString)
        {
            UserID = login,
            Password = password,
            IntegratedSecurity = false,
            Pooling = true
        }.ConnectionString;
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    private static string EscapeIdentifier(string value) => value.Replace("]", "]]", StringComparison.Ordinal);

    private sealed class NullTenant : ICurrentTenant
    {
        public static NullTenant Instance { get; } = new();
        public Guid? TenantId => null;
    }
}
