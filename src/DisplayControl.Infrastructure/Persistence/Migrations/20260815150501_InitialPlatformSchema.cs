using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisplayControl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPlatformSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "app");

            migrationBuilder.CreateTable(
                name: "tenants",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    time_zone = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    state = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_events",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    target_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: true),
                    outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    details_json = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_events", x => x.id);
                    table.UniqueConstraint("ak_audit_events_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_audit_events_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "app",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "devices",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    serial_number_normalized = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    hostname = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: true),
                    os_description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    architecture = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    agent_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    player_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    disk_capacity_bytes = table.Column<long>(type: "bigint", nullable: true),
                    last_seen_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    applied_manifest_version = table.Column<long>(type: "bigint", nullable: true),
                    playback_health_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_devices", x => x.id);
                    table.UniqueConstraint("ak_devices_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_devices_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "app",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_type = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    last_safe_error = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                    table.UniqueConstraint("ak_outbox_messages_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_outbox_messages_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "app",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "licenses",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valid_from_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    control_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    latest_issued_lease_expiry_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    transfer_destination_device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_licenses", x => x.id);
                    table.UniqueConstraint("ak_licenses_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_licenses_window", "expires_at_utc > valid_from_utc");
                    table.ForeignKey(
                        name: "fk_licenses_devices_tenant",
                        columns: x => new { x.tenant_id, x.device_id },
                        principalSchema: "app",
                        principalTable: "devices",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_licenses_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "app",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_tenant_occurred",
                schema: "app",
                table: "audit_events",
                columns: new[] { "tenant_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_devices_tenant_serial",
                schema: "app",
                table: "devices",
                columns: new[] { "tenant_id", "serial_number_normalized" },
                unique: true,
                filter: "serial_number_normalized IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_licenses_tenant_device",
                schema: "app",
                table: "licenses",
                columns: new[] { "tenant_id", "device_id" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_pending",
                schema: "app",
                table: "outbox_messages",
                columns: new[] { "processed_at_utc", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_tenants_slug",
                schema: "app",
                table: "tenants",
                column: "slug",
                unique: true);

            migrationBuilder.Sql(
                """
                ALTER TABLE app.tenants ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.tenants FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenants_tenant_isolation ON app.tenants
                    USING (id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

                ALTER TABLE app.devices ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.devices FORCE ROW LEVEL SECURITY;
                CREATE POLICY devices_tenant_isolation ON app.devices
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

                ALTER TABLE app.licenses ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.licenses FORCE ROW LEVEL SECURITY;
                CREATE POLICY licenses_tenant_isolation ON app.licenses
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

                ALTER TABLE app.audit_events ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.audit_events FORCE ROW LEVEL SECURITY;
                CREATE POLICY audit_events_tenant_isolation ON app.audit_events
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

                ALTER TABLE app.outbox_messages ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.outbox_messages FORCE ROW LEVEL SECURITY;
                CREATE POLICY outbox_messages_tenant_isolation ON app.outbox_messages
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_events",
                schema: "app");

            migrationBuilder.DropTable(
                name: "licenses",
                schema: "app");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "app");

            migrationBuilder.DropTable(
                name: "devices",
                schema: "app");

            migrationBuilder.DropTable(
                name: "tenants",
                schema: "app");
        }
    }
}
