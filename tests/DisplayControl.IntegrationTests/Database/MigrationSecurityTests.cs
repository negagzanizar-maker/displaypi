using DisplayControl.Application.Tenancy;
using DisplayControl.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DisplayControl.IntegrationTests.Database;

public sealed class MigrationSecurityTests
{
    private static readonly string[] TenantTables =
    [
        "audit_events",
        "content_assets",
        "content_versions",
        "desired_state_assets",
        "desired_states",
        "device_assignments",
        "device_certificates",
        "device_group_members",
        "device_groups",
        "device_heartbeats",
        "device_network_interfaces",
        "device_synchronization_events",
        "devices",
        "enrollment_tokens",
        "group_assignments",
        "identity_notifications",
        "invitations",
        "license_events",
        "licenses",
        "outbox_messages",
        "playlist_items",
        "playlist_versions",
        "playlists",
        "tenant_memberships"
    ];

    [Fact]
    public void MigrationScriptCreatesFailClosedSqlServerSecurityPolicies()
    {
        var options = new DbContextOptionsBuilder<DisplayControlDbContext>()
            .UseSqlServer(
                "Server=127.0.0.1,1433;Database=offline_migration_check;User Id=unused;Password=unused;TrustServerCertificate=True",
                sqlServer => sqlServer.MigrationsAssembly(SqlServerTestDatabase.MigrationsAssembly))
            .Options;

        using var context = new DisplayControlDbContext(options, NullTenant.Instance);
        var migrator = context.GetService<IMigrator>();
        var script = migrator.GenerateScript(options: MigrationsSqlGenerationOptions.Default);
        var mappedTenantTables = context.Model.GetEntityTypes()
            .Where(entityType => entityType.FindProperty("TenantId") is not null)
            .Select(entityType => entityType.GetTableName())
            .Where(tableName => tableName is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(TenantTables.Order(StringComparer.Ordinal), mappedTenantTables);
        Assert.Contains("CREATE FUNCTION app.fn_tenant_access", script, StringComparison.Ordinal);
        Assert.Contains("SESSION_CONTEXT", script, StringComparison.Ordinal);
        Assert.Contains("tenant_id", script, StringComparison.Ordinal);
        Assert.Contains("platform_catalog", script, StringComparison.Ordinal);
        Assert.Contains("CREATE FUNCTION app.fn_identity_notification_access", script, StringComparison.Ordinal);
        Assert.Contains("notification_delivery", script, StringComparison.Ordinal);
        Assert.Contains("CREATE FUNCTION app.fn_retention_access", script, StringComparison.Ordinal);
        Assert.Contains("data_retention", script, StringComparison.Ordinal);

        foreach (var table in TenantTables)
        {
            Assert.Contains($"CREATE SECURITY POLICY app.policy_{table}", script, StringComparison.Ordinal);
            Assert.Contains($"FILTER PREDICATE app.fn_tenant_access(tenant_id) ON app.{table}", script, StringComparison.Ordinal);
            Assert.Contains($"BLOCK PREDICATE app.fn_tenant_access(tenant_id) ON app.{table} AFTER INSERT", script, StringComparison.Ordinal);
            Assert.Contains($"BLOCK PREDICATE app.fn_tenant_access(tenant_id) ON app.{table} AFTER UPDATE", script, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("policy_system_key_metadata", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitialMigrationAppliesCurrentHeartbeatIdempotencySchema()
    {
        await using var database = new SqlServerTestDatabase("display_control_migration_tests");
        await database.StartAsync();
        await database.MigrateAsync();

        await using var connection = new SqlConnection(database.OwnerConnectionString);
        await connection.OpenAsync();
        const string sql =
            """
            SELECT c.name, TYPE_NAME(c.user_type_id), c.max_length, c.is_nullable
            FROM sys.columns c
            JOIN sys.tables t ON t.object_id = c.object_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE s.name = N'app'
              AND t.name = N'device_heartbeats'
              AND c.name IN (N'boot_id', N'request_sha256', N'response_json')
            ORDER BY c.name;
            """;
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new Dictionary<string, (string Type, short Length, bool Nullable)>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0), (reader.GetString(1), reader.GetInt16(2), reader.GetBoolean(3)));
        }

        Assert.Equal(3, columns.Count);
        Assert.Equal(("uniqueidentifier", (short)16, false), columns["boot_id"]);
        Assert.Equal(("varbinary", (short)-1, false), columns["request_sha256"]);
        Assert.Equal(("nvarchar", (short)-1, true), columns["response_json"]);
    }

    private sealed class NullTenant : ICurrentTenant
    {
        public static NullTenant Instance { get; } = new();
        public Guid? TenantId => null;
    }
}
