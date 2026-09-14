using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisplayControl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MfaReplayGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "last_accepted_time_step",
                schema: "app",
                table: "user_mfa_secrets",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_mfa_secrets_last_step",
                schema: "app",
                table: "user_mfa_secrets",
                sql: "last_accepted_time_step IS NULL OR last_accepted_time_step >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_user_mfa_secrets_last_step",
                schema: "app",
                table: "user_mfa_secrets");

            migrationBuilder.DropColumn(
                name: "last_accepted_time_step",
                schema: "app",
                table: "user_mfa_secrets");
        }
    }
}
