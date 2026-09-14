using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisplayControl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContentAndAssignmentCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "content_assets",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    media_kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    lifecycle_state = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    archived_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
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
                name: "license_events",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    license_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    actor_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    before_json = table.Column<string>(type: "jsonb", nullable: false),
                    after_json = table.Column<string>(type: "jsonb", nullable: false),
                    source_device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    destination_device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
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
                name: "playlists",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    archived_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
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
                name: "system_key_metadata",
                schema: "app",
                columns: table => new
                {
                    key_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    algorithm = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    public_key_der = table.Column<byte[]>(type: "bytea", nullable: false),
                    activates_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    retires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_key_metadata", x => x.key_id);
                });

            migrationBuilder.CreateTable(
                name: "content_versions",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    byte_length = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<byte[]>(type: "bytea", nullable: false),
                    detected_mime_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    original_display_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    media_metadata_json = table.Column<string>(type: "jsonb", nullable: false),
                    scan_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    scan_engine_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    rejection_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_content_versions", x => x.id);
                    table.UniqueConstraint("ak_content_versions_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_content_versions_byte_length", "byte_length >= 0");
                    table.CheckConstraint("ck_content_versions_sha256", "octet_length(sha256) = 32");
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
                name: "device_group_members",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    added_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    added_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
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
                name: "playlist_versions",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    playlist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    name_snapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description_snapshot = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    publication_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    published_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
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
                name: "device_assignments",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    playlist_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    starts_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ends_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    presentation_time_zone = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    published_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    published_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    playlist_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    starts_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ends_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    presentation_time_zone = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    published_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    published_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    playlist_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    duration_milliseconds = table.Column<int>(type: "integer", nullable: true),
                    loop_video = table.Column<bool>(type: "boolean", nullable: false),
                    presentation_json = table.Column<string>(type: "jsonb", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    source_device_assignment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_group_assignment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    starts_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ends_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    manifest_sha256 = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    superseded_by_desired_state_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_desired_states", x => x.id);
                    table.UniqueConstraint("ak_desired_states_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_desired_states_sha256", "octet_length(manifest_sha256) = 32");
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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    desired_state_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    media_kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    byte_length = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<byte[]>(type: "bytea", nullable: false),
                    duration_milliseconds = table.Column<int>(type: "integer", nullable: true),
                    loop_video = table.Column<bool>(type: "boolean", nullable: false),
                    playback_json = table.Column<string>(type: "jsonb", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_desired_state_assets", x => x.id);
                    table.UniqueConstraint("ak_desired_state_assets_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_desired_state_assets_byte_length", "byte_length >= 0");
                    table.CheckConstraint("ck_desired_state_assets_duration", "duration_milliseconds IS NULL OR duration_milliseconds > 0");
                    table.CheckConstraint("ck_desired_state_assets_position", "position >= 0");
                    table.CheckConstraint("ck_desired_state_assets_sha256", "octet_length(sha256) = 32");
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
                name: "ix_license_events_license_occurred",
                schema: "app",
                table: "license_events",
                columns: new[] { "tenant_id", "license_id", "occurred_at_utc" });

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

            migrationBuilder.Sql(
                """
                ALTER TABLE app.content_assets ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.content_assets FORCE ROW LEVEL SECURITY;
                CREATE POLICY content_assets_tenant_isolation ON app.content_assets
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

                ALTER TABLE app.content_versions ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.content_versions FORCE ROW LEVEL SECURITY;
                CREATE POLICY content_versions_tenant_isolation ON app.content_versions
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

                ALTER TABLE app.playlists ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.playlists FORCE ROW LEVEL SECURITY;
                CREATE POLICY playlists_tenant_isolation ON app.playlists
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

                ALTER TABLE app.playlist_versions ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.playlist_versions FORCE ROW LEVEL SECURITY;
                CREATE POLICY playlist_versions_tenant_isolation ON app.playlist_versions
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

                ALTER TABLE app.playlist_items ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.playlist_items FORCE ROW LEVEL SECURITY;
                CREATE POLICY playlist_items_tenant_isolation ON app.playlist_items
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

                ALTER TABLE app.device_groups ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.device_groups FORCE ROW LEVEL SECURITY;
                CREATE POLICY device_groups_tenant_isolation ON app.device_groups
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

                ALTER TABLE app.device_group_members ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.device_group_members FORCE ROW LEVEL SECURITY;
                CREATE POLICY device_group_members_tenant_isolation ON app.device_group_members
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

                ALTER TABLE app.device_assignments ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.device_assignments FORCE ROW LEVEL SECURITY;
                CREATE POLICY device_assignments_tenant_isolation ON app.device_assignments
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

                ALTER TABLE app.group_assignments ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.group_assignments FORCE ROW LEVEL SECURITY;
                CREATE POLICY group_assignments_tenant_isolation ON app.group_assignments
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

                ALTER TABLE app.desired_states ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.desired_states FORCE ROW LEVEL SECURITY;
                CREATE POLICY desired_states_tenant_isolation ON app.desired_states
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

                ALTER TABLE app.desired_state_assets ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.desired_state_assets FORCE ROW LEVEL SECURITY;
                CREATE POLICY desired_state_assets_tenant_isolation ON app.desired_state_assets
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

                ALTER TABLE app.license_events ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.license_events FORCE ROW LEVEL SECURITY;
                CREATE POLICY license_events_tenant_isolation ON app.license_events
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "desired_state_assets",
                schema: "app");

            migrationBuilder.DropTable(
                name: "device_group_members",
                schema: "app");

            migrationBuilder.DropTable(
                name: "license_events",
                schema: "app");

            migrationBuilder.DropTable(
                name: "playlist_items",
                schema: "app");

            migrationBuilder.DropTable(
                name: "system_key_metadata",
                schema: "app");

            migrationBuilder.DropTable(
                name: "desired_states",
                schema: "app");

            migrationBuilder.DropTable(
                name: "content_versions",
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
                name: "device_groups",
                schema: "app");

            migrationBuilder.DropTable(
                name: "playlist_versions",
                schema: "app");

            migrationBuilder.DropTable(
                name: "playlists",
                schema: "app");
        }
    }
}
