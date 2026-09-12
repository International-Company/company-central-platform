using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCP.Modules.Audit.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Grants the audit trail's append-only privileges to the role that will
    /// actually use them.
    /// <para>
    /// <b>The original grant named <c>current_user</c>, and that was right for
    /// exactly as long as there was one role.</b> Once migrating and serving are
    /// separate roles — which is the point of separating them — <c>current_user</c>
    /// during a migration is the migrator. The append-only grant would land on
    /// the role that never writes an audit event, and the application would be
    /// left with no privilege on the audit schema at all: not append-only, but
    /// unable to append.
    /// </para>
    /// <para>
    /// So the role is told to the migration instead of assumed by it. The
    /// migrator publishes <c>ccp.application_role</c> on its session from
    /// configuration; with nothing configured the setting is absent and this
    /// falls back to <c>current_user</c>, which is the single-role deployment
    /// behaving exactly as before.
    /// </para>
    /// </summary>
    public partial class AuditPrivilegesForApplicationRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    application_role text :=
                        coalesce(nullif(current_setting('ccp.application_role', true), ''), current_user);
                BEGIN
                    -- USAGE first. Without it the grants below are privileges on
                    -- tables the role cannot reach, which reads as correct in
                    -- every privilege listing and fails on the first insert.
                    EXECUTE format('GRANT USAGE ON SCHEMA audit TO %I', application_role);

                    EXECUTE format(
                        'REVOKE ALL ON ALL TABLES IN SCHEMA audit FROM %I', application_role);
                    EXECUTE format(
                        'GRANT INSERT, SELECT ON ALL TABLES IN SCHEMA audit TO %I', application_role);

                    -- Partitions are created month by month by the maintenance
                    -- job, long after this runs, so the default privileges are
                    -- what actually carry the rule forward. FOR ROLE current_user
                    -- because they apply to objects this role creates, and this
                    -- role is the one that creates them.
                    EXECUTE format(
                        'ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA audit '
                        'GRANT INSERT, SELECT ON TABLES TO %I', current_user, application_role);
                    EXECUTE format(
                        'ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA audit '
                        'REVOKE UPDATE, DELETE ON TABLES FROM %I', current_user, application_role);
                EXCEPTION
                    WHEN undefined_object THEN
                        -- The configured role does not exist on this server. Said
                        -- plainly rather than swallowed: it is a deployment that
                        -- named a role nobody created.
                        RAISE WARNING
                            'The role named by Database:ApplicationRole does not exist. '
                            'Audit privileges were not applied to it.';
                    WHEN insufficient_privilege THEN
                        RAISE WARNING
                            'Could not set privileges on the audit schema. Append-only is NOT '
                            'enforced at the database. See scripts/database-roles.sql.';
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing. Reversing this would mean guessing which privileges the
            // role held before, and guessing wrong in the direction of granting
            // more is how an append-only trail stops being one.
        }
    }
}
