using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisplayControl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NotificationDeadLetter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_identity_notifications_pending",
                schema: "app",
                table: "identity_notifications");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "failed_at_utc",
                schema: "app",
                table: "identity_notifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_identity_notifications_pending",
                schema: "app",
                table: "identity_notifications",
                columns: new[] { "processed_at_utc", "failed_at_utc", "next_attempt_at_utc" });

            migrationBuilder.Sql(
                """
                CREATE POLICY device_heartbeats_retention_select ON app.device_heartbeats
                    FOR SELECT
                    USING (current_user = 'display_control_maintenance' AND current_setting('app.data_retention', true) = 'true');

                CREATE POLICY device_heartbeats_retention_delete ON app.device_heartbeats
                    FOR DELETE
                    USING (current_user = 'display_control_maintenance' AND current_setting('app.data_retention', true) = 'true');

                CREATE POLICY audit_events_retention_select ON app.audit_events
                    FOR SELECT
                    USING (current_user = 'display_control_maintenance' AND current_setting('app.data_retention', true) = 'true');

                CREATE POLICY audit_events_retention_delete ON app.audit_events
                    FOR DELETE
                    USING (current_user = 'display_control_maintenance' AND current_setting('app.data_retention', true) = 'true');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS audit_events_retention_delete ON app.audit_events;
                DROP POLICY IF EXISTS audit_events_retention_select ON app.audit_events;
                DROP POLICY IF EXISTS device_heartbeats_retention_delete ON app.device_heartbeats;
                DROP POLICY IF EXISTS device_heartbeats_retention_select ON app.device_heartbeats;
                """);

            migrationBuilder.DropIndex(
                name: "ix_identity_notifications_pending",
                schema: "app",
                table: "identity_notifications");

            migrationBuilder.DropColumn(
                name: "failed_at_utc",
                schema: "app",
                table: "identity_notifications");

            migrationBuilder.CreateIndex(
                name: "ix_identity_notifications_pending",
                schema: "app",
                table: "identity_notifications",
                columns: new[] { "processed_at_utc", "next_attempt_at_utc" });
        }
    }
}
