using DisplayControl.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisplayControl.SqlServerMigrations.Migrations;

[DbContext(typeof(DisplayControlDbContext))]
[Migration("20260911143000_SqlServerWorkerSecurity")]
public sealed class SqlServerWorkerSecurity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP SECURITY POLICY app.policy_identity_notifications;");
        migrationBuilder.Sql("DROP SECURITY POLICY app.policy_device_heartbeats;");
        migrationBuilder.Sql("DROP SECURITY POLICY app.policy_audit_events;");

        migrationBuilder.Sql(
            """
            CREATE FUNCTION app.fn_identity_notification_access(@tenant_id uniqueidentifier)
            RETURNS TABLE WITH SCHEMABINDING
            AS
            RETURN SELECT 1 AS allowed
            WHERE TRY_CONVERT(uniqueidentifier, SESSION_CONTEXT(N'tenant_id')) = @tenant_id
               OR TRY_CONVERT(int, SESSION_CONTEXT(N'platform_catalog')) = 1
               OR TRY_CONVERT(int, SESSION_CONTEXT(N'notification_delivery')) = 1;
            """);
        migrationBuilder.Sql(
            """
            CREATE FUNCTION app.fn_retention_access(@tenant_id uniqueidentifier)
            RETURNS TABLE WITH SCHEMABINDING
            AS
            RETURN SELECT 1 AS allowed
            WHERE TRY_CONVERT(uniqueidentifier, SESSION_CONTEXT(N'tenant_id')) = @tenant_id
               OR TRY_CONVERT(int, SESSION_CONTEXT(N'platform_catalog')) = 1
               OR TRY_CONVERT(int, SESSION_CONTEXT(N'data_retention')) = 1;
            """);

        migrationBuilder.Sql(
            """
            CREATE SECURITY POLICY app.policy_identity_notifications
            ADD FILTER PREDICATE app.fn_identity_notification_access(tenant_id) ON app.identity_notifications,
            ADD BLOCK PREDICATE app.fn_identity_notification_access(tenant_id) ON app.identity_notifications AFTER INSERT,
            ADD BLOCK PREDICATE app.fn_identity_notification_access(tenant_id) ON app.identity_notifications AFTER UPDATE;
            """);
        foreach (var table in new[] { "device_heartbeats", "audit_events" })
        {
            migrationBuilder.Sql($"""
                CREATE SECURITY POLICY app.policy_{table}
                ADD FILTER PREDICATE app.fn_retention_access(tenant_id) ON app.{table},
                ADD BLOCK PREDICATE app.fn_retention_access(tenant_id) ON app.{table} AFTER INSERT,
                ADD BLOCK PREDICATE app.fn_retention_access(tenant_id) ON app.{table} AFTER UPDATE;
                """);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP SECURITY POLICY app.policy_identity_notifications;");
        migrationBuilder.Sql("DROP SECURITY POLICY app.policy_device_heartbeats;");
        migrationBuilder.Sql("DROP SECURITY POLICY app.policy_audit_events;");
        migrationBuilder.Sql("DROP FUNCTION app.fn_identity_notification_access;");
        migrationBuilder.Sql("DROP FUNCTION app.fn_retention_access;");

        foreach (var table in new[] { "identity_notifications", "device_heartbeats", "audit_events" })
        {
            migrationBuilder.Sql($"""
                CREATE SECURITY POLICY app.policy_{table}
                ADD FILTER PREDICATE app.fn_tenant_access(tenant_id) ON app.{table},
                ADD BLOCK PREDICATE app.fn_tenant_access(tenant_id) ON app.{table} AFTER INSERT,
                ADD BLOCK PREDICATE app.fn_tenant_access(tenant_id) ON app.{table} AFTER UPDATE;
                """);
        }
    }
}
