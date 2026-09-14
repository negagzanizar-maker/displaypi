using DisplayControl.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisplayControl.Infrastructure.Persistence.Migrations;

[DbContext(typeof(DisplayControlDbContext))]
[Migration("20260815204500_PlatformTenantCatalogPolicy")]
public partial class PlatformTenantCatalogPolicy : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE POLICY tenants_platform_catalog_select ON app.tenants
                FOR SELECT
                USING (current_setting('app.platform_catalog', true) = 'true');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP POLICY IF EXISTS tenants_platform_catalog_select ON app.tenants;");
    }
}
