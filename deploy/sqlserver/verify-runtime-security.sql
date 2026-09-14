-- Run through the exact ConnectionStrings__Database login before starting the API.
IF IS_SRVROLEMEMBER(N'sysadmin') = 1
    THROW 51000, 'Runtime login must not be sysadmin.', 1;
IF IS_ROLEMEMBER(N'db_owner') = 1
    THROW 51001, 'Runtime user must not be db_owner.', 1;
IF HAS_PERMS_BY_NAME(N'app.identity_notifications', N'OBJECT', N'SELECT') = 1
    THROW 51002, 'Runtime user must not read protected notification payloads.', 1;
IF HAS_PERMS_BY_NAME(N'app.audit_events', N'OBJECT', N'DELETE') = 1 OR
   HAS_PERMS_BY_NAME(N'app.device_heartbeats', N'OBJECT', N'DELETE') = 1 OR
   HAS_PERMS_BY_NAME(N'app.license_events', N'OBJECT', N'DELETE') = 1
    THROW 51003, 'Runtime user must not delete append-oriented evidence.', 1;
IF EXISTS (
    SELECT 1
    FROM sys.tables t
    JOIN sys.schemas s ON s.schema_id = t.schema_id
    JOIN sys.columns c ON c.object_id = t.object_id AND c.name = N'tenant_id'
    WHERE s.name = N'app'
      AND NOT EXISTS (
          SELECT 1
          FROM sys.security_predicates p
          JOIN sys.security_policies policy_definition ON policy_definition.object_id = p.object_id
          WHERE p.target_object_id = t.object_id AND policy_definition.is_enabled = 1))
    THROW 51004, 'Every tenant table must have an enabled SQL Server security policy.', 1;

SELECT ORIGINAL_LOGIN() AS verified_runtime_login,
       DB_NAME() AS verified_database,
       CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) AS sql_server_version;
