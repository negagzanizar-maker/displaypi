using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DisplayControl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HumanIdentityCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "identity_roles",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "identity_users",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    account_state = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_password_changed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identity_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "identity_role_claims",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
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
                name: "identity_user_claims",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
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
                    login_provider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    provider_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
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
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false)
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
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    intended_role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    token_digest = table.Column<byte[]>(type: "bytea", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    consumed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invitations", x => x.id);
                    table.UniqueConstraint("ak_invitations_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_invitations_consumption", "(consumed_at_utc IS NULL) = (consumed_by_user_id IS NULL)");
                    table.CheckConstraint("ck_invitations_digest", "octet_length(token_digest) = 32");
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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    state = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    invited_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accepted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
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
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    protected_secret = table.Column<byte[]>(type: "bytea", nullable: false),
                    protection_scheme = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_mfa_secrets", x => x.user_id);
                    table.CheckConstraint("ck_user_mfa_secrets_payload", "octet_length(protected_secret) > 0");
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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_digest = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_recovery_codes", x => x.id);
                    table.CheckConstraint("ck_user_recovery_codes_digest", "octet_length(code_digest) = 32");
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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_key_digest = table.Column<byte[]>(type: "bytea", nullable: false),
                    selected_tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    security_stamp_digest = table.Column<byte[]>(type: "bytea", nullable: false),
                    mfa_satisfied = table.Column<bool>(type: "boolean", nullable: false),
                    mfa_satisfied_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    idle_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    absolute_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revocation_reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent_digest = table.Column<byte[]>(type: "bytea", nullable: true),
                    source_address_digest = table.Column<byte[]>(type: "bytea", nullable: true),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_sessions", x => x.id);
                    table.CheckConstraint("ck_user_sessions_expiry", "idle_expires_at_utc > created_at_utc AND absolute_expires_at_utc >= idle_expires_at_utc");
                    table.CheckConstraint("ck_user_sessions_key_digest", "octet_length(session_key_digest) = 32");
                    table.CheckConstraint("ck_user_sessions_mfa", "(mfa_satisfied = FALSE AND mfa_satisfied_at_utc IS NULL) OR (mfa_satisfied = TRUE AND mfa_satisfied_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_user_sessions_source_address_digest", "source_address_digest IS NULL OR octet_length(source_address_digest) = 32");
                    table.CheckConstraint("ck_user_sessions_stamp_digest", "octet_length(security_stamp_digest) = 32");
                    table.CheckConstraint("ck_user_sessions_user_agent_digest", "user_agent_digest IS NULL OR octet_length(user_agent_digest) = 32");
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
                unique: true);

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
                name: "ix_identity_users_normalized_email",
                schema: "app",
                table: "identity_users",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "ux_identity_users_normalized_user_name",
                schema: "app",
                table: "identity_users",
                column: "normalized_user_name",
                unique: true);

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

            migrationBuilder.Sql(
                """
                ALTER TABLE app.invitations ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.invitations FORCE ROW LEVEL SECURITY;
                CREATE POLICY invitations_tenant_isolation ON app.invitations
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

                ALTER TABLE app.tenant_memberships ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.tenant_memberships FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_memberships_tenant_isolation ON app.tenant_memberships
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
                name: "identity_roles",
                schema: "app");

            migrationBuilder.DropTable(
                name: "identity_users",
                schema: "app");
        }
    }
}
