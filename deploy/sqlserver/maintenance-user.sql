-- The retention worker intentionally requires this exact login name.
IF DATABASE_PRINCIPAL_ID(N'display_control_maintenance') IS NULL
    CREATE USER [display_control_maintenance] FOR LOGIN [display_control_maintenance];

GRANT SELECT, DELETE ON OBJECT::app.device_heartbeats TO [display_control_maintenance];
GRANT SELECT, DELETE ON OBJECT::app.audit_events TO [display_control_maintenance];
DENY INSERT, UPDATE, ALTER, CONTROL ON OBJECT::app.device_heartbeats TO [display_control_maintenance];
DENY INSERT, UPDATE, ALTER, CONTROL ON OBJECT::app.audit_events TO [display_control_maintenance];
