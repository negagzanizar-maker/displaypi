using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisplayControl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecoveryCodeReplayGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "concurrency_token",
                schema: "app",
                table: "user_recovery_codes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "concurrency_token",
                schema: "app",
                table: "user_recovery_codes");
        }
    }
}
