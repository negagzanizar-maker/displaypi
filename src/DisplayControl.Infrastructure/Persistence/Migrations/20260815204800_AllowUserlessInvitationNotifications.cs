using DisplayControl.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisplayControl.Infrastructure.Persistence.Migrations;

[DbContext(typeof(DisplayControlDbContext))]
[Migration("20260815204800_AllowUserlessInvitationNotifications")]
public partial class AllowUserlessInvitationNotifications : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<Guid>(
            name: "user_id",
            schema: "app",
            table: "identity_notifications",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM app.identity_notifications WHERE user_id IS NULL) THEN
                    RAISE EXCEPTION 'Cannot restore required notification user_id while invitation notifications exist';
                END IF;
            END $$;
            """);
        migrationBuilder.AlterColumn<Guid>(
            name: "user_id",
            schema: "app",
            table: "identity_notifications",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);
    }
}
