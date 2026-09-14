using DisplayControl.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisplayControl.Infrastructure.Persistence.Migrations;

[DbContext(typeof(DisplayControlDbContext))]
[Migration("20260815204700_NotificationDeliveryPolicy")]
public partial class NotificationDeliveryPolicy : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE POLICY identity_notifications_delivery_select ON app.identity_notifications
                FOR SELECT
                USING (current_setting('app.notification_delivery', true) = 'true');

            CREATE POLICY identity_notifications_delivery_update ON app.identity_notifications
                FOR UPDATE
                USING (current_setting('app.notification_delivery', true) = 'true')
                WITH CHECK (current_setting('app.notification_delivery', true) = 'true');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP POLICY IF EXISTS identity_notifications_delivery_update ON app.identity_notifications;
            DROP POLICY IF EXISTS identity_notifications_delivery_select ON app.identity_notifications;
            """);
    }
}
