using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisplayControl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IdentityNotificationProtectionScheme : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "protection_scheme",
                schema: "app",
                table: "identity_notifications",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "protection_scheme",
                schema: "app",
                table: "identity_notifications");
        }
    }
}
