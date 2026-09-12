-- ---------------------------------------------------------------------------
-- Two database roles, so that changing the schema and serving requests are
-- different privileges.
--
-- WHY THIS EXISTS
--
-- Migrations need the right to create and drop tables. Serving a request needs
-- the right to read and write rows. One role holding both means any
-- SQL-injection defect anywhere in the Platform is a defect that can drop a
-- table, and the audit trail's append-only guarantee -- which is a REVOKE, not a
-- promise -- can be granted straight back by the same connection that is meant
-- to be bound by it.
--
-- Splitting them costs one extra connection string and removes a whole class of
-- consequence. It changes nothing about the code: both roles connect to the same
-- database, and the Platform behaves identically.
--
-- HOW TO USE IT
--
--   psql "$SUPERUSER_URL" -v app_password="'...'" -v migrator_password="'...'" \
--        -f scripts/database-roles.sql
--
-- Passwords are passed in, never written here. Nothing in this repository holds
-- a credential (docs/security/secrets-management.md).
--
-- Then configure the Platform with both:
--
--   CCP_ConnectionStrings__Platform=...Username=ccp_app;Password=...
--   CCP_ConnectionStrings__PlatformMigrations=...Username=ccp_migrator;Password=...
--   CCP_Database__ApplicationRole=ccp_app
--
-- The third is what tells a migration which role to grant to. Without it a
-- migration grants privileges to whoever ran it -- which, once the roles are
-- split, is the migrator, and the application would be left with no privileges
-- at all on the audit schema.
--
-- SAFE TO RE-RUN. Every statement is idempotent.
-- ---------------------------------------------------------------------------

\set ON_ERROR_STOP on

-- --- The roles -------------------------------------------------------------

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'ccp_migrator') THEN
        CREATE ROLE ccp_migrator LOGIN;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'ccp_app') THEN
        CREATE ROLE ccp_app LOGIN;
    END IF;
END $$;

ALTER ROLE ccp_migrator WITH PASSWORD :migrator_password;
ALTER ROLE ccp_app      WITH PASSWORD :app_password;

-- NOINHERIT on neither and membership between neither. ccp_app must not be able
-- to become ccp_migrator by any route, or the split is decoration.

-- --- The database ----------------------------------------------------------

DO $$
DECLARE
    db text := current_database();
BEGIN
    EXECUTE format('GRANT CONNECT ON DATABASE %I TO ccp_migrator, ccp_app', db);

    -- Nobody else. PostgreSQL grants CONNECT to PUBLIC by default, which makes
    -- every role on the server a role that can reach this database.
    EXECUTE format('REVOKE ALL ON DATABASE %I FROM PUBLIC', db);
END $$;

-- --- The schemas -----------------------------------------------------------
--
-- Created here rather than left to the first migration, so that they are owned
-- by the migrator from the start. A schema created by a superuser and then
-- written to by the migrator would leave the migrator unable to change what it
-- had created.

DO $$
DECLARE
    module text;
    schemas text[] := ARRAY[
        'kernel', 'identity', 'organization', 'authz', 'security', 'audit',
        'workflow', 'notifications', 'documents', 'integrations', 'configuration'
    ];
BEGIN
    FOREACH module IN ARRAY schemas LOOP
        EXECUTE format('CREATE SCHEMA IF NOT EXISTS %I AUTHORIZATION ccp_migrator', module);

        -- The application may use the schema and work with what is in it. It may
        -- not create anything: that is the whole point.
        EXECUTE format('GRANT USAGE ON SCHEMA %I TO ccp_app', module);
        EXECUTE format('REVOKE CREATE ON SCHEMA %I FROM ccp_app', module);
        EXECUTE format('REVOKE ALL ON SCHEMA %I FROM PUBLIC', module);

        -- What exists now.
        EXECUTE format(
            'GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %I TO ccp_app', module);
        EXECUTE format(
            'GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA %I TO ccp_app', module);

        -- And what the next migration creates. Without this, every deployment
        -- that adds a table is followed by an application that cannot read it,
        -- and the failure arrives after the release rather than during it.
        EXECUTE format(
            'ALTER DEFAULT PRIVILEGES FOR ROLE ccp_migrator IN SCHEMA %I '
            'GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO ccp_app', module);
        EXECUTE format(
            'ALTER DEFAULT PRIVILEGES FOR ROLE ccp_migrator IN SCHEMA %I '
            'GRANT USAGE, SELECT ON SEQUENCES TO ccp_app', module);
    END LOOP;
END $$;

-- --- The audit trail is narrower still --------------------------------------
--
-- Append-only, enforced by the database rather than promised by the code
-- (ARCHITECTURE.md §15.4). The migration that creates the trail applies this
-- too; it is repeated here so that a database prepared by this script is correct
-- before a single migration has run.

DO $$
BEGIN
    EXECUTE 'REVOKE ALL ON ALL TABLES IN SCHEMA audit FROM ccp_app';
    EXECUTE 'GRANT INSERT, SELECT ON ALL TABLES IN SCHEMA audit TO ccp_app';
    EXECUTE 'ALTER DEFAULT PRIVILEGES FOR ROLE ccp_migrator IN SCHEMA audit '
            'GRANT INSERT, SELECT ON TABLES TO ccp_app';
    EXECUTE 'ALTER DEFAULT PRIVILEGES FOR ROLE ccp_migrator IN SCHEMA audit '
            'REVOKE UPDATE, DELETE ON TABLES FROM ccp_app';
END $$;

-- --- What was actually granted ----------------------------------------------
--
-- Printed, because a privilege script whose output nobody reads is a privilege
-- script nobody knows the result of. The application must appear with no
-- CREATE anywhere, and with no UPDATE or DELETE on audit.

SELECT
    table_schema,
    grantee,
    string_agg(DISTINCT privilege_type, ', ' ORDER BY privilege_type) AS privileges
FROM information_schema.table_privileges
WHERE grantee IN ('ccp_app', 'ccp_migrator')
GROUP BY table_schema, grantee
ORDER BY table_schema, grantee;
