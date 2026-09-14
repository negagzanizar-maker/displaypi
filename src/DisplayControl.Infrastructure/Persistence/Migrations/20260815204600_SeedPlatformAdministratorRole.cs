using DisplayControl.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisplayControl.Infrastructure.Persistence.Migrations;

[DbContext(typeof(DisplayControlDbContext))]
[Migration("20260815204600_SeedPlatformAdministratorRole")]
public partial class SeedPlatformAdministratorRole : Migration
{
    public static readonly Guid PlatformAdministratorRoleId =
        Guid.Parse("f0000000-0000-0000-0000-000000000001");

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            $"""
            INSERT INTO app.identity_roles (id, name, normalized_name, concurrency_stamp)
            VALUES ('{PlatformAdministratorRoleId}', 'PlatformAdministrator', 'PLATFORMADMINISTRATOR', 'migration-seed-v1')
            ON CONFLICT DO NOTHING;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            $"""
            DELETE FROM app.identity_roles role
            WHERE role.id = '{PlatformAdministratorRoleId}'
              AND NOT EXISTS (
                  SELECT 1
                  FROM app.identity_user_roles user_role
                  WHERE user_role.role_id = role.id);
            """);
    }
}
