using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisplayControl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IdentityNotificationOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "identity_notifications",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notification_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    normalized_recipient_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    protected_payload = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    last_safe_error_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    concurrency_token = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identity_notifications", x => x.id);
                    table.CheckConstraint("ck_identity_notifications_attempts", "attempt_count >= 0");
                    table.CheckConstraint("ck_identity_notifications_payload", "octet_length(protected_payload) > 0 AND octet_length(protected_payload) <= 16384");
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

            migrationBuilder.CreateIndex(
                name: "ix_identity_notifications_pending",
                schema: "app",
                table: "identity_notifications",
                columns: new[] { "processed_at_utc", "next_attempt_at_utc" });

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

            migrationBuilder.Sql(
                """
                ALTER TABLE app.identity_notifications ENABLE ROW LEVEL SECURITY;
                ALTER TABLE app.identity_notifications FORCE ROW LEVEL SECURITY;
                CREATE POLICY identity_notifications_tenant_isolation ON app.identity_notifications
                    FOR INSERT
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                        OR (
                            tenant_id IS NULL
                            AND NULLIF(current_setting('app.tenant_id', true), '') IS NULL
                        )
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identity_notifications",
                schema: "app");
        }
    }
}
