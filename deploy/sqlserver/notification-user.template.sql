-- Run in the application database after creating the dedicated notification login.
IF DATABASE_PRINCIPAL_ID(N'$(NotificationUser)') IS NULL
    CREATE USER [$(NotificationUser)] FOR LOGIN [$(NotificationUser)];

GRANT SELECT, UPDATE ON OBJECT::app.identity_notifications TO [$(NotificationUser)];
DENY INSERT, DELETE, ALTER, CONTROL ON OBJECT::app.identity_notifications TO [$(NotificationUser)];
