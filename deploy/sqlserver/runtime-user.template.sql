-- Run in the application database as the migration owner after creating the login.
-- Replace $(RuntimeUser) with a validated SQL identifier through sqlcmd -v.
IF DATABASE_PRINCIPAL_ID(N'$(RuntimeUser)') IS NULL
    CREATE USER [$(RuntimeUser)] FOR LOGIN [$(RuntimeUser)];

GRANT SELECT ON OBJECT::app.identity_role_claims TO [$(RuntimeUser)];
GRANT SELECT ON OBJECT::app.identity_roles TO [$(RuntimeUser)];
GRANT SELECT ON OBJECT::app.system_key_metadata TO [$(RuntimeUser)];
GRANT SELECT, INSERT, UPDATE ON OBJECT::app.tenants TO [$(RuntimeUser)];

GRANT SELECT, INSERT ON OBJECT::app.audit_events TO [$(RuntimeUser)];
GRANT SELECT, INSERT ON OBJECT::app.device_heartbeats TO [$(RuntimeUser)];
GRANT SELECT, INSERT ON OBJECT::app.device_synchronization_events TO [$(RuntimeUser)];
GRANT SELECT, INSERT ON OBJECT::app.license_events TO [$(RuntimeUser)];

GRANT SELECT, INSERT, UPDATE ON OBJECT::app.content_assets TO [$(RuntimeUser)];
GRANT SELECT, INSERT, UPDATE ON OBJECT::app.content_versions TO [$(RuntimeUser)];
GRANT SELECT, INSERT, UPDATE ON OBJECT::app.desired_state_assets TO [$(RuntimeUser)];
GRANT SELECT, INSERT, UPDATE ON OBJECT::app.desired_states TO [$(RuntimeUser)];
GRANT SELECT, INSERT, UPDATE ON OBJECT::app.device_certificates TO [$(RuntimeUser)];
GRANT SELECT, INSERT, UPDATE ON OBJECT::app.devices TO [$(RuntimeUser)];
GRANT SELECT, INSERT, UPDATE ON OBJECT::app.enrollment_tokens TO [$(RuntimeUser)];
GRANT SELECT, INSERT, UPDATE ON OBJECT::app.identity_users TO [$(RuntimeUser)];
GRANT SELECT, INSERT, UPDATE ON OBJECT::app.invitations TO [$(RuntimeUser)];
GRANT SELECT, INSERT, UPDATE ON OBJECT::app.licenses TO [$(RuntimeUser)];
GRANT SELECT, INSERT, UPDATE ON OBJECT::app.playlist_versions TO [$(RuntimeUser)];
GRANT SELECT, INSERT, UPDATE ON OBJECT::app.playlists TO [$(RuntimeUser)];
GRANT SELECT, INSERT, UPDATE ON OBJECT::app.tenant_memberships TO [$(RuntimeUser)];

GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::app.device_assignments TO [$(RuntimeUser)];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::app.device_group_members TO [$(RuntimeUser)];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::app.device_groups TO [$(RuntimeUser)];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::app.device_network_interfaces TO [$(RuntimeUser)];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::app.group_assignments TO [$(RuntimeUser)];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::app.identity_user_claims TO [$(RuntimeUser)];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::app.identity_user_logins TO [$(RuntimeUser)];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::app.identity_user_roles TO [$(RuntimeUser)];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::app.identity_user_tokens TO [$(RuntimeUser)];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::app.outbox_messages TO [$(RuntimeUser)];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::app.playlist_items TO [$(RuntimeUser)];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::app.user_mfa_secrets TO [$(RuntimeUser)];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::app.user_recovery_codes TO [$(RuntimeUser)];
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::app.user_sessions TO [$(RuntimeUser)];

GRANT INSERT ON OBJECT::app.identity_notifications TO [$(RuntimeUser)];
DENY SELECT, UPDATE, DELETE ON OBJECT::app.identity_notifications TO [$(RuntimeUser)];
DENY DELETE ON OBJECT::app.audit_events TO [$(RuntimeUser)];
DENY DELETE ON OBJECT::app.device_heartbeats TO [$(RuntimeUser)];
DENY DELETE ON OBJECT::app.license_events TO [$(RuntimeUser)];
DENY ALTER ON SCHEMA::app TO [$(RuntimeUser)];
