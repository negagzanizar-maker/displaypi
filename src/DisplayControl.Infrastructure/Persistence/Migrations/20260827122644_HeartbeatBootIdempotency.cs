using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisplayControl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HeartbeatBootIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_device_heartbeats_device_sequence",
                schema: "app",
                table: "device_heartbeats");

            migrationBuilder.AddColumn<Guid>(
                name: "boot_id",
                schema: "app",
                table: "device_heartbeats",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "request_sha256",
                schema: "app",
                table: "device_heartbeats",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "response_json",
                schema: "app",
                table: "device_heartbeats",
                type: "jsonb",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE app.device_heartbeats
                SET boot_id = id,
                    request_sha256 = decode(repeat('00', 32), 'hex')
                WHERE boot_id IS NULL OR request_sha256 IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "boot_id",
                schema: "app",
                table: "device_heartbeats",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "request_sha256",
                schema: "app",
                table: "device_heartbeats",
                type: "bytea",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_device_heartbeats_device_boot_sequence",
                schema: "app",
                table: "device_heartbeats",
                columns: new[] { "tenant_id", "device_id", "boot_id", "sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_device_heartbeats_device_boot_sequence",
                schema: "app",
                table: "device_heartbeats");

            migrationBuilder.DropColumn(
                name: "boot_id",
                schema: "app",
                table: "device_heartbeats");

            migrationBuilder.DropColumn(
                name: "request_sha256",
                schema: "app",
                table: "device_heartbeats");

            migrationBuilder.DropColumn(
                name: "response_json",
                schema: "app",
                table: "device_heartbeats");

            migrationBuilder.CreateIndex(
                name: "ux_device_heartbeats_device_sequence",
                schema: "app",
                table: "device_heartbeats",
                columns: new[] { "tenant_id", "device_id", "sequence" },
                unique: true);
        }
    }
}
