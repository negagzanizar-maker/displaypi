using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisplayControl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StoreIssuedDeviceCertificateDer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "certificate_der",
                schema: "app",
                table: "device_certificates",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_device_certificates_der",
                schema: "app",
                table: "device_certificates",
                sql: "certificate_der IS NULL OR (octet_length(certificate_der) >= 100 AND octet_length(certificate_der) <= 16384)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_device_certificates_der",
                schema: "app",
                table: "device_certificates");

            migrationBuilder.DropColumn(
                name: "certificate_der",
                schema: "app",
                table: "device_certificates");
        }
    }
}
