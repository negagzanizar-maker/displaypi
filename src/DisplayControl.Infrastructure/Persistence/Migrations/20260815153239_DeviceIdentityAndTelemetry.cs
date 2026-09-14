using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisplayControl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeviceIdentityAndTelemetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "device_certificates",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    certificate_serial_number = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    thumbprint_sha256 = table.Column<byte[]>(type: "bytea", nullable: false),
                    subject_public_key_info_sha256 = table.Column<byte[]>(type: "bytea", nullable: false),
                    not_before_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    not_after_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    issued_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    rotated_from_certificate_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revocation_reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_certificates", x => x.id);
                    table.UniqueConstraint("ak_device_certificates_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_device_certificates_issuance", "issued_at_utc >= not_before_utc AND issued_at_utc < not_after_utc");
                    table.CheckConstraint("ck_device_certificates_revocation", "state <> 'Revoked' OR (revoked_at_utc IS NOT NULL AND revocation_reason_code IS NOT NULL)");
                    table.CheckConstraint("ck_device_certificates_spki", "octet_length(subject_public_key_info_sha256) = 32");
                    table.CheckConstraint("ck_device_certificates_thumbprint", "octet_length(thumbprint_sha256) = 32");
                    table.CheckConstraint("ck_device_certificates_validity", "not_after_utc > not_before_utc");
                    table.ForeignKey(
                        name: "fk_device_certificates_devices_tenant",
                        columns: x => new { x.tenant_id, x.device_id },
                        principalSchema: "app",
                        principalTable: "devices",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_device_certificates_rotation_tenant",
                        columns: x => new { x.tenant_id, x.rotated_from_certificate_id },
                        principalSchema: "app",
                        principalTable: "device_certificates",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_device_certificates_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "app",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "device_heartbeats",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    reported_sent_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    received_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    server_observed_ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    inventory_json = table.Column<string>(type: "jsonb", nullable: false),
                    applied_desired_state_version = table.Column<long>(type: "bigint", nullable: true),
                    player_state_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    free_disk_bytes = table.Column<long>(type: "bigint", nullable: true),
                    last_error_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_heartbeats", x => x.id);
                    table.UniqueConstraint("ak_device_heartbeats_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_device_heartbeats_free_disk", "free_disk_bytes IS NULL OR free_disk_bytes >= 0");
                    table.CheckConstraint("ck_device_heartbeats_sequence", "sequence > 0");
                    table.ForeignKey(
                        name: "fk_device_heartbeats_devices_tenant",
                        columns: x => new { x.tenant_id, x.device_id },
                        principalSchema: "app",
                        principalTable: "devices",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_device_heartbeats_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "app",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "device_network_interfaces",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    interface_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    mac_address_normalized = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    local_addresses_json = table.Column<string>(type: "jsonb", nullable: false),
                    observed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_network_interfaces", x => x.id);
                    table.UniqueConstraint("ak_device_network_interfaces_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_device_network_interfaces_devices_tenant",
                        columns: x => new { x.tenant_id, x.device_id },
                        principalSchema: "app",
                        principalTable: "devices",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_device_network_interfaces_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "app",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "device_synchronization_events",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    desired_state_version = table.Column<long>(type: "bigint", nullable: true),
                    event_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    result_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    safe_details_json = table.Column<string>(type: "jsonb", nullable: false),
                    reported_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    received_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_synchronization_events", x => x.id);
                    table.UniqueConstraint("ak_device_synchronization_events_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_device_synchronization_events_version", "desired_state_version IS NULL OR desired_state_version > 0");
                    table.ForeignKey(
                        name: "fk_device_synchronization_events_devices_tenant",
                        columns: x => new { x.tenant_id, x.device_id },
                        principalSchema: "app",
                        principalTable: "devices",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_device_synchronization_events_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "app",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "enrollment_tokens",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_digest = table.Column<byte[]>(type: "bytea", nullable: false),
                    expected_device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expected_serial_number_normalized = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    consumed_by_device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_attempt_count = table.Column<int>(type: "integer", nullable: false),
                    last_failed_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_enrollment_tokens", x => x.id);
                    table.UniqueConstraint("ak_enrollment_tokens_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_enrollment_tokens_consumption", "(consumed_at_utc IS NULL) = (consumed_by_device_id IS NULL)");
                    table.CheckConstraint("ck_enrollment_tokens_digest", "octet_length(token_digest) = 32");
                    table.CheckConstraint("ck_enrollment_tokens_expected_device", "expected_device_id IS NULL OR consumed_by_device_id IS NULL OR expected_device_id = consumed_by_device_id");
                    table.CheckConstraint("ck_enrollment_tokens_expiry", "expires_at_utc > created_at_utc");
                    table.CheckConstraint("ck_enrollment_tokens_failed_attempts", "failed_attempt_count >= 0 AND failed_attempt_count <= 10");
                    table.CheckConstraint("ck_enrollment_tokens_terminal_state", "NOT (consumed_at_utc IS NOT NULL AND revoked_at_utc IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_enrollment_tokens_consuming_devices_tenant",
                        columns: x => new { x.tenant_id, x.consumed_by_device_id },
                        principalSchema: "app",
                        principalTable: "devices",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_enrollment_tokens_expected_devices_tenant",
                        columns: x => new { x.tenant_id, x.expected_device_id },
                        principalSchema: "app",
                        principalTable: "devices",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_enrollment_tokens_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "app",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_device_certificates_device_state",
                schema: "app",
                table: "device_certificates",
                columns: new[] { "tenant_id", "device_id", "state" });

            migrationBuilder.CreateIndex(
                name: "IX_device_certificates_tenant_id_rotated_from_certificate_id",
                schema: "app",
                table: "device_certificates",
                columns: new[] { "tenant_id", "rotated_from_certificate_id" });

            migrationBuilder.CreateIndex(
                name: "ux_device_certificates_serial",
                schema: "app",
                table: "device_certificates",
                column: "certificate_serial_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_device_certificates_thumbprint",
                schema: "app",
                table: "device_certificates",
                column: "thumbprint_sha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_device_heartbeats_device_received",
                schema: "app",
                table: "device_heartbeats",
                columns: new[] { "tenant_id", "device_id", "received_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_device_heartbeats_device_sequence",
                schema: "app",
                table: "device_heartbeats",
                columns: new[] { "tenant_id", "device_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_device_network_interfaces_device_name",
                schema: "app",
                table: "device_network_interfaces",
                columns: new[] { "tenant_id", "device_id", "interface_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_device_synchronization_events_device_received",
                schema: "app",
                table: "device_synchronization_events",
                columns: new[] { "tenant_id", "device_id", "received_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_enrollment_tokens_tenant_expiry",
                schema: "app",
                table: "enrollment_tokens",
                columns: new[] { "tenant_id", "expires_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_enrollment_tokens_tenant_id_consumed_by_device_id",
                schema: "app",
                table: "enrollment_tokens",
                columns: new[] { "tenant_id", "consumed_by_device_id" });

            migrationBuilder.CreateIndex(
                name: "IX_enrollment_tokens_tenant_id_expected_device_id",
                schema: "app",
                table: "enrollment_tokens",
                columns: new[] { "tenant_id", "expected_device_id" });

            migrationBuilder.CreateIndex(
                name: "ux_enrollment_tokens_digest",
                schema: "app",
                table: "enrollment_tokens",
                column: "token_digest",
                unique: true);

            migrationBuilder.Sql(
                """
                ALTER TABLE app.device_certificates ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.device_certificates FORCE ROW LEVEL SECURITY;
                CREATE POLICY device_certificates_tenant_isolation ON app.device_certificates
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

                ALTER TABLE app.device_heartbeats ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.device_heartbeats FORCE ROW LEVEL SECURITY;
                CREATE POLICY device_heartbeats_tenant_isolation ON app.device_heartbeats
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

                ALTER TABLE app.device_network_interfaces ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.device_network_interfaces FORCE ROW LEVEL SECURITY;
                CREATE POLICY device_network_interfaces_tenant_isolation ON app.device_network_interfaces
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

                ALTER TABLE app.device_synchronization_events ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.device_synchronization_events FORCE ROW LEVEL SECURITY;
                CREATE POLICY device_synchronization_events_tenant_isolation ON app.device_synchronization_events
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

                ALTER TABLE app.enrollment_tokens ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.enrollment_tokens FORCE ROW LEVEL SECURITY;
                CREATE POLICY enrollment_tokens_tenant_isolation ON app.enrollment_tokens
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_certificates",
                schema: "app");

            migrationBuilder.DropTable(
                name: "device_heartbeats",
                schema: "app");

            migrationBuilder.DropTable(
                name: "device_network_interfaces",
                schema: "app");

            migrationBuilder.DropTable(
                name: "device_synchronization_events",
                schema: "app");

            migrationBuilder.DropTable(
                name: "enrollment_tokens",
                schema: "app");
        }
    }
}
