using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisplayControl.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class InitialSqlServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "app");

            migrationBuilder.CreateTable(
                name: "identity_roles",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    normalized_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "system_key_metadata",
                schema: "app",
                columns: table => new
                {
                    key_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    algorithm = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    purpose = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    public_key_der = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    activates_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    retires_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_key_metadata", x => x.key_id);
                });

            migrationBuilder.CreateTable(
                name: "tenants",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    slug = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    time_zone = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    state = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    concurrency_token = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "identity_role_claims",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    role_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    claim_type = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    claim_value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_role_claims", x => x.id);
                    table.ForeignKey(
                        name: "FK_identity_role_claims_identity_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "app",
                        principalTable: "identity_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "audit_events",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    actor_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    actor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    action = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    target_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    target_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    reason_code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    correlation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    details_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
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
                name: "content_assets",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    media_kind = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    lifecycle_state = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    archived_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    concurrency_token = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_content_assets", x => x.id);
                    table.UniqueConstraint("ak_content_assets_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_content_assets_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "app",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "device_groups",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    concurrency_token = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_groups", x => x.id);
                    table.UniqueConstraint("ak_device_groups_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_device_groups_tenants",
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
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    state = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    serial_number_normalized = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    hostname = table.Column<string>(type: "nvarchar(253)", maxLength: 253, nullable: true),
                    os_description = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    architecture = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    agent_version = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    player_version = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    disk_capacity_bytes = table.Column<long>(type: "bigint", nullable: true),
                    last_seen_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    applied_manifest_version = table.Column<long>(type: "bigint", nullable: true),
                    playback_health_code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    concurrency_token = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
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
                name: "identity_users",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    account_state = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    home_tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    last_password_changed_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    user_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    normalized_email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    email_confirmed = table.Column<bool>(type: "bit", nullable: false),
                    password_hash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    security_stamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    phone_number = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "bit", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "bit", nullable: false),
                    lockout_end_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "bit", nullable: false),
                    access_failed_count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_users", x => x.id);
                    table.ForeignKey(
                        name: "fk_identity_users_home_tenants",
                        column: x => x.home_tenant_id,
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
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    message_type = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    payload_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    last_safe_error = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
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
                name: "playlists",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    archived_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    concurrency_token = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_playlists", x => x.id);
                    table.UniqueConstraint("ak_playlists_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_playlists_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "app",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "content_versions",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    content_asset_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version_number = table.Column<int>(type: "int", nullable: false),
                    storage_key = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    byte_length = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    detected_mime_type = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    original_display_file_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    media_metadata_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    scan_state = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    scan_engine_version = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    rejection_code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    approved_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_content_versions", x => x.id);
                    table.UniqueConstraint("ak_content_versions_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_content_versions_byte_length", "byte_length >= 0");
                    table.CheckConstraint("ck_content_versions_sha256", "DATALENGTH(sha256) = 32");
                    table.CheckConstraint("ck_content_versions_version", "version_number > 0");
                    table.ForeignKey(
                        name: "fk_content_versions_assets_tenant",
                        columns: x => new { x.tenant_id, x.content_asset_id },
                        principalSchema: "app",
                        principalTable: "content_assets",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_content_versions_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "app",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "device_certificates",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    certificate_serial_number = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    thumbprint_sha256 = table.Column<byte[]>(type: "varbinary(900)", nullable: false),
                    subject_public_key_info_sha256 = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    certificate_der = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    not_before_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    not_after_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    state = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    issued_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    rotated_from_certificate_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    revocation_reason_code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_certificates", x => x.id);
                    table.UniqueConstraint("ak_device_certificates_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_device_certificates_der", "certificate_der IS NULL OR (DATALENGTH(certificate_der) >= 100 AND DATALENGTH(certificate_der) <= 16384)");
                    table.CheckConstraint("ck_device_certificates_issuance", "issued_at_utc >= not_before_utc AND issued_at_utc < not_after_utc");
                    table.CheckConstraint("ck_device_certificates_revocation", "state <> 'Revoked' OR (revoked_at_utc IS NOT NULL AND revocation_reason_code IS NOT NULL)");
                    table.CheckConstraint("ck_device_certificates_spki", "DATALENGTH(subject_public_key_info_sha256) = 32");
                    table.CheckConstraint("ck_device_certificates_thumbprint", "DATALENGTH(thumbprint_sha256) = 32");
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
                name: "device_group_members",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    device_group_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    added_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    added_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_group_members", x => x.id);
                    table.UniqueConstraint("ak_device_group_members_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_device_group_members_devices_tenant",
                        columns: x => new { x.tenant_id, x.device_id },
                        principalSchema: "app",
                        principalTable: "devices",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_device_group_members_groups_tenant",
                        columns: x => new { x.tenant_id, x.device_group_id },
                        principalSchema: "app",
                        principalTable: "device_groups",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_device_group_members_tenants",
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
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    boot_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    request_sha256 = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    response_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    reported_sent_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    received_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    server_observed_ip = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    inventory_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    applied_desired_state_version = table.Column<long>(type: "bigint", nullable: true),
                    player_state_code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    free_disk_bytes = table.Column<long>(type: "bigint", nullable: true),
                    last_error_code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    correlation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
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
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    interface_name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    mac_address_normalized = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    local_addresses_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    observed_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
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
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    desired_state_version = table.Column<long>(type: "bigint", nullable: true),
                    event_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    result_code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    safe_details_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    reported_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    received_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
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
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    token_digest = table.Column<byte[]>(type: "varbinary(900)", nullable: false),
                    expected_device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    expected_serial_number_normalized = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    consumed_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    consumed_by_device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    failed_attempt_count = table.Column<int>(type: "int", nullable: false),
                    last_failed_attempt_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    concurrency_token = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_enrollment_tokens", x => x.id);
                    table.UniqueConstraint("ak_enrollment_tokens_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_enrollment_tokens_consumption", "(consumed_at_utc IS NULL AND consumed_by_device_id IS NULL) OR (consumed_at_utc IS NOT NULL AND consumed_by_device_id IS NOT NULL)");
                    table.CheckConstraint("ck_enrollment_tokens_digest", "DATALENGTH(token_digest) = 32");
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

            migrationBuilder.CreateTable(
                name: "licenses",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    valid_from_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    control_state = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    latest_issued_lease_expiry_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    transfer_destination_device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    concurrency_token = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "identity_notifications",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    notification_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    normalized_recipient_email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    protection_scheme = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    protected_payload = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    next_attempt_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    failed_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    last_safe_error_code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    concurrency_token = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identity_notifications", x => x.id);
                    table.CheckConstraint("ck_identity_notifications_attempts", "attempt_count >= 0");
                    table.CheckConstraint("ck_identity_notifications_payload", "DATALENGTH(protected_payload) > 0 AND DATALENGTH(protected_payload) <= 16384");
                    table.ForeignKey(
                        name: "fk_identity_notifications_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "app",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_identity_notifications_users",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "identity_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "identity_user_claims",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    claim_type = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    claim_value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "FK_identity_user_claims_identity_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "identity_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "identity_user_logins",
                schema: "app",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    provider_key = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    provider_display_name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_user_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "FK_identity_user_logins_identity_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "identity_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "identity_user_roles",
                schema: "app",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    role_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_user_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "FK_identity_user_roles_identity_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "app",
                        principalTable: "identity_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_identity_user_roles_identity_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "identity_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "identity_user_tokens",
                schema: "app",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    login_provider = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "FK_identity_user_tokens_identity_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "identity_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "invitations",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    normalized_email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    intended_role = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    token_digest = table.Column<byte[]>(type: "varbinary(900)", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    consumed_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    consumed_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    concurrency_token = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invitations", x => x.id);
                    table.UniqueConstraint("ak_invitations_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_invitations_consumption", "(consumed_at_utc IS NULL AND consumed_by_user_id IS NULL) OR (consumed_at_utc IS NOT NULL AND consumed_by_user_id IS NOT NULL)");
                    table.CheckConstraint("ck_invitations_digest", "DATALENGTH(token_digest) = 32");
                    table.CheckConstraint("ck_invitations_expiry", "expires_at_utc > created_at_utc");
                    table.CheckConstraint("ck_invitations_terminal_state", "NOT (consumed_at_utc IS NOT NULL AND revoked_at_utc IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_invitations_consuming_users",
                        column: x => x.consumed_by_user_id,
                        principalSchema: "app",
                        principalTable: "identity_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invitations_creators",
                        column: x => x.created_by_user_id,
                        principalSchema: "app",
                        principalTable: "identity_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invitations_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "app",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tenant_memberships",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    role = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    state = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    invited_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    accepted_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    concurrency_token = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_memberships", x => x.id);
                    table.UniqueConstraint("ak_tenant_memberships_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_tenant_memberships_inviters",
                        column: x => x.invited_by_user_id,
                        principalSchema: "app",
                        principalTable: "identity_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tenant_memberships_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "app",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tenant_memberships_users",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "identity_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_mfa_secrets",
                schema: "app",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    protected_secret = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    protection_scheme = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    confirmed_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    last_accepted_time_step = table.Column<long>(type: "bigint", nullable: true),
                    concurrency_token = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_mfa_secrets", x => x.user_id);
                    table.CheckConstraint("ck_user_mfa_secrets_last_step", "last_accepted_time_step IS NULL OR last_accepted_time_step >= 0");
                    table.CheckConstraint("ck_user_mfa_secrets_payload", "DATALENGTH(protected_secret) > 0");
                    table.ForeignKey(
                        name: "fk_user_mfa_secrets_users",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "identity_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_recovery_codes",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code_digest = table.Column<byte[]>(type: "varbinary(900)", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    used_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    concurrency_token = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_recovery_codes", x => x.id);
                    table.CheckConstraint("ck_user_recovery_codes_digest", "DATALENGTH(code_digest) = 32");
                    table.ForeignKey(
                        name: "fk_user_recovery_codes_users",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "identity_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_sessions",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    session_key_digest = table.Column<byte[]>(type: "varbinary(900)", nullable: false),
                    selected_tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    security_stamp_digest = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    mfa_satisfied = table.Column<bool>(type: "bit", nullable: false),
                    mfa_satisfied_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    last_seen_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    idle_expires_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    absolute_expires_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    revocation_reason_code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    user_agent_digest = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    source_address_digest = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    concurrency_token = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_sessions", x => x.id);
                    table.CheckConstraint("ck_user_sessions_expiry", "idle_expires_at_utc > created_at_utc AND absolute_expires_at_utc >= idle_expires_at_utc");
                    table.CheckConstraint("ck_user_sessions_key_digest", "DATALENGTH(session_key_digest) = 32");
                    table.CheckConstraint("ck_user_sessions_mfa", "(mfa_satisfied = 0 AND mfa_satisfied_at_utc IS NULL) OR (mfa_satisfied = 1 AND mfa_satisfied_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_user_sessions_source_address_digest", "source_address_digest IS NULL OR DATALENGTH(source_address_digest) = 32");
                    table.CheckConstraint("ck_user_sessions_stamp_digest", "DATALENGTH(security_stamp_digest) = 32");
                    table.CheckConstraint("ck_user_sessions_user_agent_digest", "user_agent_digest IS NULL OR DATALENGTH(user_agent_digest) = 32");
                    table.ForeignKey(
                        name: "fk_user_sessions_selected_tenants",
                        column: x => x.selected_tenant_id,
                        principalSchema: "app",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_sessions_users",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "identity_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "playlist_versions",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    playlist_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version_number = table.Column<int>(type: "int", nullable: false),
                    name_snapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    description_snapshot = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    publication_state = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    published_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    published_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_playlist_versions", x => x.id);
                    table.UniqueConstraint("ak_playlist_versions_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_playlist_versions_version", "version_number > 0");
                    table.ForeignKey(
                        name: "fk_playlist_versions_playlists_tenant",
                        columns: x => new { x.tenant_id, x.playlist_id },
                        principalSchema: "app",
                        principalTable: "playlists",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_playlist_versions_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "app",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "license_events",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    license_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    event_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    actor_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    actor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    before_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    after_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    source_device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    destination_device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_license_events", x => x.id);
                    table.UniqueConstraint("ak_license_events_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_license_events_licenses_tenant",
                        columns: x => new { x.tenant_id, x.license_id },
                        principalSchema: "app",
                        principalTable: "licenses",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_license_events_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "app",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "device_assignments",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    playlist_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_enabled = table.Column<bool>(type: "bit", nullable: false),
                    priority = table.Column<int>(type: "int", nullable: false),
                    starts_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ends_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    presentation_time_zone = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    published_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    published_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    concurrency_token = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_assignments", x => x.id);
                    table.UniqueConstraint("ak_device_assignments_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_device_assignments_window", "starts_at_utc IS NULL OR ends_at_utc IS NULL OR ends_at_utc > starts_at_utc");
                    table.ForeignKey(
                        name: "fk_device_assignments_devices_tenant",
                        columns: x => new { x.tenant_id, x.device_id },
                        principalSchema: "app",
                        principalTable: "devices",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_device_assignments_playlist_versions_tenant",
                        columns: x => new { x.tenant_id, x.playlist_version_id },
                        principalSchema: "app",
                        principalTable: "playlist_versions",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_device_assignments_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "app",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "group_assignments",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    device_group_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    playlist_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_enabled = table.Column<bool>(type: "bit", nullable: false),
                    priority = table.Column<int>(type: "int", nullable: false),
                    starts_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ends_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    presentation_time_zone = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    published_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    published_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    concurrency_token = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_group_assignments", x => x.id);
                    table.UniqueConstraint("ak_group_assignments_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_group_assignments_window", "starts_at_utc IS NULL OR ends_at_utc IS NULL OR ends_at_utc > starts_at_utc");
                    table.ForeignKey(
                        name: "fk_group_assignments_groups_tenant",
                        columns: x => new { x.tenant_id, x.device_group_id },
                        principalSchema: "app",
                        principalTable: "device_groups",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_group_assignments_playlist_versions_tenant",
                        columns: x => new { x.tenant_id, x.playlist_version_id },
                        principalSchema: "app",
                        principalTable: "playlist_versions",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_group_assignments_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "app",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "playlist_items",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    playlist_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    content_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    position = table.Column<int>(type: "int", nullable: false),
                    duration_milliseconds = table.Column<int>(type: "int", nullable: true),
                    loop_video = table.Column<bool>(type: "bit", nullable: false),
                    presentation_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_playlist_items", x => x.id);
                    table.UniqueConstraint("ak_playlist_items_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_playlist_items_duration", "duration_milliseconds IS NULL OR duration_milliseconds > 0");
                    table.CheckConstraint("ck_playlist_items_position", "position >= 0");
                    table.ForeignKey(
                        name: "fk_playlist_items_content_versions_tenant",
                        columns: x => new { x.tenant_id, x.content_version_id },
                        principalSchema: "app",
                        principalTable: "content_versions",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_playlist_items_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "app",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_playlist_items_versions_tenant",
                        columns: x => new { x.tenant_id, x.playlist_version_id },
                        principalSchema: "app",
                        principalTable: "playlist_versions",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "desired_states",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    source_device_assignment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    source_group_assignment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    starts_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ends_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    manifest_sha256 = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    published_at_utc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    superseded_by_desired_state_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_desired_states", x => x.id);
                    table.UniqueConstraint("ak_desired_states_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_desired_states_sha256", "DATALENGTH(manifest_sha256) = 32");
                    table.CheckConstraint("ck_desired_states_source", "(source_device_assignment_id IS NOT NULL AND source_group_assignment_id IS NULL) OR (source_device_assignment_id IS NULL AND source_group_assignment_id IS NOT NULL)");
                    table.CheckConstraint("ck_desired_states_version", "version > 0");
                    table.CheckConstraint("ck_desired_states_window", "starts_at_utc IS NULL OR ends_at_utc IS NULL OR ends_at_utc > starts_at_utc");
                    table.ForeignKey(
                        name: "fk_desired_states_device_assignments_tenant",
                        columns: x => new { x.tenant_id, x.source_device_assignment_id },
                        principalSchema: "app",
                        principalTable: "device_assignments",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_desired_states_devices_tenant",
                        columns: x => new { x.tenant_id, x.device_id },
                        principalSchema: "app",
                        principalTable: "devices",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_desired_states_group_assignments_tenant",
                        columns: x => new { x.tenant_id, x.source_group_assignment_id },
                        principalSchema: "app",
                        principalTable: "group_assignments",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_desired_states_superseded_by_tenant",
                        columns: x => new { x.tenant_id, x.superseded_by_desired_state_id },
                        principalSchema: "app",
                        principalTable: "desired_states",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_desired_states_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "app",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "desired_state_assets",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    desired_state_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    content_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    position = table.Column<int>(type: "int", nullable: false),
                    media_kind = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    byte_length = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    duration_milliseconds = table.Column<int>(type: "int", nullable: true),
                    loop_video = table.Column<bool>(type: "bit", nullable: false),
                    playback_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_desired_state_assets", x => x.id);
                    table.UniqueConstraint("ak_desired_state_assets_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_desired_state_assets_byte_length", "byte_length >= 0");
                    table.CheckConstraint("ck_desired_state_assets_duration", "duration_milliseconds IS NULL OR duration_milliseconds > 0");
                    table.CheckConstraint("ck_desired_state_assets_position", "position >= 0");
                    table.CheckConstraint("ck_desired_state_assets_sha256", "DATALENGTH(sha256) = 32");
                    table.ForeignKey(
                        name: "fk_desired_state_assets_content_versions_tenant",
                        columns: x => new { x.tenant_id, x.content_version_id },
                        principalSchema: "app",
                        principalTable: "content_versions",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_desired_state_assets_states_tenant",
                        columns: x => new { x.tenant_id, x.desired_state_id },
                        principalSchema: "app",
                        principalTable: "desired_states",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_desired_state_assets_tenants",
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
                name: "ix_content_assets_tenant_title",
                schema: "app",
                table: "content_assets",
                columns: new[] { "tenant_id", "title" });

            migrationBuilder.CreateIndex(
                name: "ux_content_versions_asset_version",
                schema: "app",
                table: "content_versions",
                columns: new[] { "tenant_id", "content_asset_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_content_versions_tenant_storage_key",
                schema: "app",
                table: "content_versions",
                columns: new[] { "tenant_id", "storage_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_desired_state_assets_tenant_id_content_version_id",
                schema: "app",
                table: "desired_state_assets",
                columns: new[] { "tenant_id", "content_version_id" });

            migrationBuilder.CreateIndex(
                name: "ux_desired_state_assets_state_position",
                schema: "app",
                table: "desired_state_assets",
                columns: new[] { "tenant_id", "desired_state_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_desired_states_tenant_id_source_device_assignment_id",
                schema: "app",
                table: "desired_states",
                columns: new[] { "tenant_id", "source_device_assignment_id" });

            migrationBuilder.CreateIndex(
                name: "IX_desired_states_tenant_id_source_group_assignment_id",
                schema: "app",
                table: "desired_states",
                columns: new[] { "tenant_id", "source_group_assignment_id" });

            migrationBuilder.CreateIndex(
                name: "IX_desired_states_tenant_id_superseded_by_desired_state_id",
                schema: "app",
                table: "desired_states",
                columns: new[] { "tenant_id", "superseded_by_desired_state_id" });

            migrationBuilder.CreateIndex(
                name: "ux_desired_states_device_version",
                schema: "app",
                table: "desired_states",
                columns: new[] { "tenant_id", "device_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_device_assignments_resolution",
                schema: "app",
                table: "device_assignments",
                columns: new[] { "tenant_id", "device_id", "is_enabled", "priority" });

            migrationBuilder.CreateIndex(
                name: "IX_device_assignments_tenant_id_playlist_version_id",
                schema: "app",
                table: "device_assignments",
                columns: new[] { "tenant_id", "playlist_version_id" });

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
                name: "IX_device_group_members_tenant_id_device_id",
                schema: "app",
                table: "device_group_members",
                columns: new[] { "tenant_id", "device_id" });

            migrationBuilder.CreateIndex(
                name: "ux_device_group_members_group_device",
                schema: "app",
                table: "device_group_members",
                columns: new[] { "tenant_id", "device_group_id", "device_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_device_groups_tenant_name",
                schema: "app",
                table: "device_groups",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_device_heartbeats_device_received",
                schema: "app",
                table: "device_heartbeats",
                columns: new[] { "tenant_id", "device_id", "received_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_device_heartbeats_device_boot_sequence",
                schema: "app",
                table: "device_heartbeats",
                columns: new[] { "tenant_id", "device_id", "boot_id", "sequence" },
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
                name: "ux_devices_tenant_serial",
                schema: "app",
                table: "devices",
                columns: new[] { "tenant_id", "serial_number_normalized" },
                unique: true,
                filter: "serial_number_normalized IS NOT NULL");

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

            migrationBuilder.CreateIndex(
                name: "ix_group_assignments_resolution",
                schema: "app",
                table: "group_assignments",
                columns: new[] { "tenant_id", "device_group_id", "is_enabled", "priority" });

            migrationBuilder.CreateIndex(
                name: "IX_group_assignments_tenant_id_playlist_version_id",
                schema: "app",
                table: "group_assignments",
                columns: new[] { "tenant_id", "playlist_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_identity_notifications_pending",
                schema: "app",
                table: "identity_notifications",
                columns: new[] { "processed_at_utc", "failed_at_utc", "next_attempt_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_identity_notifications_tenant_id",
                schema: "app",
                table: "identity_notifications",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_identity_notifications_user_id",
                schema: "app",
                table: "identity_notifications",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_identity_role_claims_role_id",
                schema: "app",
                table: "identity_role_claims",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ux_identity_roles_normalized_name",
                schema: "app",
                table: "identity_roles",
                column: "normalized_name",
                unique: true,
                filter: "[normalized_name] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_identity_user_claims_user_id",
                schema: "app",
                table: "identity_user_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_identity_user_logins_user_id",
                schema: "app",
                table: "identity_user_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_identity_user_roles_role_id",
                schema: "app",
                table: "identity_user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_identity_users_home_tenant",
                schema: "app",
                table: "identity_users",
                column: "home_tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_identity_users_normalized_email",
                schema: "app",
                table: "identity_users",
                column: "normalized_email",
                unique: true,
                filter: "[normalized_email] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_identity_users_normalized_user_name",
                schema: "app",
                table: "identity_users",
                column: "normalized_user_name",
                unique: true,
                filter: "[normalized_user_name] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_invitations_consumed_by_user_id",
                schema: "app",
                table: "invitations",
                column: "consumed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_invitations_created_by_user_id",
                schema: "app",
                table: "invitations",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_invitations_digest",
                schema: "app",
                table: "invitations",
                column: "token_digest",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_invitations_tenant_pending_email",
                schema: "app",
                table: "invitations",
                columns: new[] { "tenant_id", "normalized_email" },
                unique: true,
                filter: "consumed_at_utc IS NULL AND revoked_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_license_events_license_occurred",
                schema: "app",
                table: "license_events",
                columns: new[] { "tenant_id", "license_id", "occurred_at_utc" });

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
                name: "IX_playlist_items_tenant_id_content_version_id",
                schema: "app",
                table: "playlist_items",
                columns: new[] { "tenant_id", "content_version_id" });

            migrationBuilder.CreateIndex(
                name: "ux_playlist_items_version_position",
                schema: "app",
                table: "playlist_items",
                columns: new[] { "tenant_id", "playlist_version_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_playlist_versions_playlist_version",
                schema: "app",
                table: "playlist_versions",
                columns: new[] { "tenant_id", "playlist_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_playlists_tenant_name",
                schema: "app",
                table: "playlists",
                columns: new[] { "tenant_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_system_keys_purpose_activation",
                schema: "app",
                table: "system_key_metadata",
                columns: new[] { "purpose", "activates_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_tenant_memberships_invited_by_user_id",
                schema: "app",
                table: "tenant_memberships",
                column: "invited_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_tenant_memberships_single_current_tenant",
                schema: "app",
                table: "tenant_memberships",
                column: "user_id",
                unique: true,
                filter: "state <> 'Removed'");

            migrationBuilder.CreateIndex(
                name: "ux_tenant_memberships_tenant_user",
                schema: "app",
                table: "tenant_memberships",
                columns: new[] { "tenant_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_tenants_slug",
                schema: "app",
                table: "tenants",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_user_recovery_codes_user_digest",
                schema: "app",
                table: "user_recovery_codes",
                columns: new[] { "user_id", "code_digest" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_sessions_selected_tenant_id",
                schema: "app",
                table: "user_sessions",
                column: "selected_tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_user_active",
                schema: "app",
                table: "user_sessions",
                columns: new[] { "user_id", "revoked_at_utc", "absolute_expires_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_user_sessions_key_digest",
                schema: "app",
                table: "user_sessions",
                column: "session_key_digest",
                unique: true);
            migrationBuilder.InsertData(
                schema: "app",
                table: "identity_roles",
                columns: new[] { "id", "name", "normalized_name", "concurrency_stamp" },
                values: new object[] { Guid.Parse("f0000000-0000-0000-0000-000000000001"), "PlatformAdministrator", "PLATFORMADMINISTRATOR", "migration-seed-v1" });

            migrationBuilder.Sql(
                """
                CREATE FUNCTION app.fn_tenant_access(@tenant_id uniqueidentifier)
                RETURNS TABLE WITH SCHEMABINDING
                AS
                RETURN SELECT 1 AS allowed
                WHERE TRY_CONVERT(uniqueidentifier, SESSION_CONTEXT(N'tenant_id')) = @tenant_id
                   OR TRY_CONVERT(int, SESSION_CONTEXT(N'platform_catalog')) = 1;
                """);

            foreach (var table in new[]
            {
                "audit_events", "content_assets", "content_versions", "desired_state_assets", "desired_states",
                "device_assignments", "device_certificates", "device_group_members", "device_groups",
                "device_heartbeats", "device_network_interfaces", "device_synchronization_events", "devices",
                "enrollment_tokens", "group_assignments", "identity_notifications", "invitations", "license_events",
                "licenses", "outbox_messages", "playlist_items", "playlist_versions", "playlists", "tenant_memberships"
            })
            {
                migrationBuilder.Sql($"""
                    CREATE SECURITY POLICY app.policy_{table}
                    ADD FILTER PREDICATE app.fn_tenant_access(tenant_id) ON app.{table},
                    ADD BLOCK PREDICATE app.fn_tenant_access(tenant_id) ON app.{table} AFTER INSERT,
                    ADD BLOCK PREDICATE app.fn_tenant_access(tenant_id) ON app.{table} AFTER UPDATE;
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[]
            {
                "audit_events", "content_assets", "content_versions", "desired_state_assets", "desired_states",
                "device_assignments", "device_certificates", "device_group_members", "device_groups",
                "device_heartbeats", "device_network_interfaces", "device_synchronization_events", "devices",
                "enrollment_tokens", "group_assignments", "identity_notifications", "invitations", "license_events",
                "licenses", "outbox_messages", "playlist_items", "playlist_versions", "playlists", "tenant_memberships"
            })
            {
                migrationBuilder.Sql($"DROP SECURITY POLICY app.policy_{table};");
            }

            migrationBuilder.Sql("DROP FUNCTION app.fn_tenant_access;");
            migrationBuilder.DropTable(
                name: "audit_events",
                schema: "app");

            migrationBuilder.DropTable(
                name: "desired_state_assets",
                schema: "app");

            migrationBuilder.DropTable(
                name: "device_certificates",
                schema: "app");

            migrationBuilder.DropTable(
                name: "device_group_members",
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

            migrationBuilder.DropTable(
                name: "identity_notifications",
                schema: "app");

            migrationBuilder.DropTable(
                name: "identity_role_claims",
                schema: "app");

            migrationBuilder.DropTable(
                name: "identity_user_claims",
                schema: "app");

            migrationBuilder.DropTable(
                name: "identity_user_logins",
                schema: "app");

            migrationBuilder.DropTable(
                name: "identity_user_roles",
                schema: "app");

            migrationBuilder.DropTable(
                name: "identity_user_tokens",
                schema: "app");

            migrationBuilder.DropTable(
                name: "invitations",
                schema: "app");

            migrationBuilder.DropTable(
                name: "license_events",
                schema: "app");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "app");

            migrationBuilder.DropTable(
                name: "playlist_items",
                schema: "app");

            migrationBuilder.DropTable(
                name: "system_key_metadata",
                schema: "app");

            migrationBuilder.DropTable(
                name: "tenant_memberships",
                schema: "app");

            migrationBuilder.DropTable(
                name: "user_mfa_secrets",
                schema: "app");

            migrationBuilder.DropTable(
                name: "user_recovery_codes",
                schema: "app");

            migrationBuilder.DropTable(
                name: "user_sessions",
                schema: "app");

            migrationBuilder.DropTable(
                name: "desired_states",
                schema: "app");

            migrationBuilder.DropTable(
                name: "identity_roles",
                schema: "app");

            migrationBuilder.DropTable(
                name: "licenses",
                schema: "app");

            migrationBuilder.DropTable(
                name: "content_versions",
                schema: "app");

            migrationBuilder.DropTable(
                name: "identity_users",
                schema: "app");

            migrationBuilder.DropTable(
                name: "device_assignments",
                schema: "app");

            migrationBuilder.DropTable(
                name: "group_assignments",
                schema: "app");

            migrationBuilder.DropTable(
                name: "content_assets",
                schema: "app");

            migrationBuilder.DropTable(
                name: "devices",
                schema: "app");

            migrationBuilder.DropTable(
                name: "device_groups",
                schema: "app");

            migrationBuilder.DropTable(
                name: "playlist_versions",
                schema: "app");

            migrationBuilder.DropTable(
                name: "playlists",
                schema: "app");

            migrationBuilder.DropTable(
                name: "tenants",
                schema: "app");
        }
    }
}
