using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisplayControl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IdentityTenantLocator : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_identity_users_normalized_email",
                schema: "app",
                table: "identity_users");

            migrationBuilder.AddColumn<Guid>(
                name: "home_tenant_id",
                schema: "app",
                table: "identity_users",
                type: "uuid",
                nullable: true);

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
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_identity_users_home_tenants",
                schema: "app",
                table: "identity_users",
                column: "home_tenant_id",
                principalSchema: "app",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_identity_users_home_tenants",
                schema: "app",
                table: "identity_users");

            migrationBuilder.DropIndex(
                name: "ix_identity_users_home_tenant",
                schema: "app",
                table: "identity_users");

            migrationBuilder.DropIndex(
                name: "ux_identity_users_normalized_email",
                schema: "app",
                table: "identity_users");

            migrationBuilder.DropColumn(
                name: "home_tenant_id",
                schema: "app",
                table: "identity_users");

            migrationBuilder.CreateIndex(
                name: "ix_identity_users_normalized_email",
                schema: "app",
                table: "identity_users",
                column: "normalized_email");
        }
    }
}
