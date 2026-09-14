using System.Diagnostics.CodeAnalysis;
using DisplayControl.Application.Tenancy;
using Microsoft.Data.SqlClient;

namespace DisplayControl.IntegrationTests.Database;

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "xUnit invokes IAsyncLifetime.DisposeAsync after the test class completes.")]
public sealed class SqlServerRlsTests : IAsyncLifetime
{
    private const string RuntimeLogin = "display_control_runtime_test";
    private const string RuntimePassword = "SqlServer_Runtime_Test_123!";
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly string[] ProtectedTables =
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

    private readonly SqlServerTestDatabase _database = new("display_control_rls_tests");
    private string _runtimeConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _database.StartAsync();
        await _database.MigrateAsync();
        _runtimeConnectionString = await _database.ProvisionRuntimeLoginAsync(RuntimeLogin, RuntimePassword);

        await using var ownerConnection = new SqlConnection(_database.OwnerConnectionString);
        await ownerConnection.OpenAsync();
        await SetPlatformCatalogAsync(ownerConnection);
        await SeedTwoTenantsAsync(ownerConnection);
    }

    public Task DisposeAsync() => _database.DisposeAsync().AsTask();

    [Fact]
    public async Task RuntimeLoginIsNotPrivilegedAndEveryTenantTableHasThreePredicates()
    {
        await using var connection = new SqlConnection(_database.OwnerConnectionString);
        await connection.OpenAsync();

        const string privilegeSql =
            """
            SELECT
                ISNULL(IS_SRVROLEMEMBER(N'sysadmin', @login), 0),
                CASE WHEN EXISTS (
                    SELECT 1
                    FROM sys.database_role_members drm
                    JOIN sys.database_principals role_principal ON role_principal.principal_id = drm.role_principal_id
                    JOIN sys.database_principals member_principal ON member_principal.principal_id = drm.member_principal_id
                    WHERE role_principal.name = N'db_owner' AND member_principal.name = @login
                ) THEN 1 ELSE 0 END;
            """;
        await using (var privilegeCommand = new SqlCommand(privilegeSql, connection))
        {
            privilegeCommand.Parameters.AddWithValue("login", RuntimeLogin);
            await using var reader = await privilegeCommand.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(0, reader.GetInt32(0));
            Assert.Equal(0, reader.GetInt32(1));
        }

        const string policySql =
            """
            SELECT t.name, COUNT_BIG(*)
            FROM sys.security_predicates predicate_definition
            JOIN sys.security_policies policy_definition
              ON policy_definition.object_id = predicate_definition.object_id
            JOIN sys.tables t ON t.object_id = predicate_definition.target_object_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE s.name = N'app' AND policy_definition.is_enabled = 1
            GROUP BY t.name
            ORDER BY t.name;
            """;
        await using var policyCommand = new SqlCommand(policySql, connection);
        await using var policyReader = await policyCommand.ExecuteReaderAsync();
        var policies = new Dictionary<string, long>(StringComparer.Ordinal);
        while (await policyReader.ReadAsync())
        {
            policies.Add(policyReader.GetString(0), policyReader.GetInt64(1));
        }

        Assert.Equal(ProtectedTables.Order(StringComparer.Ordinal), policies.Keys.Order(StringComparer.Ordinal));
        Assert.All(policies, policy => Assert.Equal(3, policy.Value));
    }

    [Fact]
    public async Task MissingAndCrossTenantContextCannotReadOrWriteDevices()
    {
        SqlConnection.ClearAllPools();
        await using (var noContextConnection = new SqlConnection(_runtimeConnectionString))
        {
            await noContextConnection.OpenAsync();
            Assert.Equal(0, await CountDevicesAsync(noContextConnection));
        }

        await using (var tenantAConnection = new SqlConnection(_runtimeConnectionString))
        {
            await tenantAConnection.OpenAsync();
            await SetTenantAsync(tenantAConnection, TenantA);
            Assert.Equal(1, await CountDevicesAsync(tenantAConnection));

            var exception = await Assert.ThrowsAsync<SqlException>(() =>
                InsertDeviceAsync(tenantAConnection, TenantB, Guid.NewGuid(), "Forbidden B device"));
            Assert.Equal(33504, exception.Number);
        }

        await using (var reusedWithoutContext = new SqlConnection(_runtimeConnectionString))
        {
            await reusedWithoutContext.OpenAsync();
            Assert.Equal(0, await CountDevicesAsync(reusedWithoutContext));
        }

        await using (var tenantBConnection = new SqlConnection(_runtimeConnectionString))
        {
            await tenantBConnection.OpenAsync();
            await SetTenantAsync(tenantBConnection, TenantB);
            Assert.Equal(1, await CountDevicesAsync(tenantBConnection));
        }
    }

    private static async Task SeedTwoTenantsAsync(SqlConnection connection)
    {
        const string tenantSql =
            """
            INSERT INTO app.tenants
                (id, name, slug, time_zone, state, created_at_utc, updated_at_utc, concurrency_token)
            VALUES
                (@tenantA, N'Tenant A', N'tenant-a', N'UTC', N'Active', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), NEWID()),
                (@tenantB, N'Tenant B', N'tenant-b', N'UTC', N'Active', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), NEWID());
            """;
        await using (var tenantCommand = new SqlCommand(tenantSql, connection))
        {
            tenantCommand.Parameters.AddWithValue("tenantA", TenantA);
            tenantCommand.Parameters.AddWithValue("tenantB", TenantB);
            await tenantCommand.ExecuteNonQueryAsync();
        }

        await InsertDeviceAsync(connection, TenantA, Guid.NewGuid(), "Tenant A device");
        await InsertDeviceAsync(connection, TenantB, Guid.NewGuid(), "Tenant B device");
    }

    private static async Task SetTenantAsync(SqlConnection connection, Guid tenantId)
    {
        const string sql =
            "EXEC sys.sp_set_session_context @key=N'tenant_id', @value=@tenantId, @read_only=0";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenantId", tenantId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SetPlatformCatalogAsync(SqlConnection connection)
    {
        const string sql =
            "EXEC sys.sp_set_session_context @key=N'platform_catalog', @value=1, @read_only=0";
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> CountDevicesAsync(SqlConnection connection)
    {
        await using var command = new SqlCommand("SELECT COUNT_BIG(*) FROM app.devices", connection);
        var count = (long)(await command.ExecuteScalarAsync() ?? 0L);
        return checked((int)count);
    }

    private static async Task InsertDeviceAsync(
        SqlConnection connection,
        Guid tenantId,
        Guid deviceId,
        string displayName)
    {
        const string sql =
            """
            INSERT INTO app.devices
                (id, tenant_id, display_name, state, created_at_utc, updated_at_utc, concurrency_token)
            VALUES
                (@id, @tenantId, @displayName, N'PendingEnrollment', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), NEWID());
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", deviceId);
        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("displayName", displayName);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class NullTenant : ICurrentTenant
    {
        public static NullTenant Instance { get; } = new();
        public Guid? TenantId => null;
    }
}
