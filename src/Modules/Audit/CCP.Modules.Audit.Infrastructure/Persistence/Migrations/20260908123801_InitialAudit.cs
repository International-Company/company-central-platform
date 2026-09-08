using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCP.Modules.Audit.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            // EF Core cannot express declarative partitioning, so the table is
            // created by hand. It matters enough to be worth the hand-written
            // SQL: an audit table is the one table in the system designed to
            // grow forever, and retention by DROP of a whole partition is the
            // only removal that never edits a row (ARCHITECTURE.md §15.4).
            //
            // occurred_at is part of the primary key because PostgreSQL requires
            // the partition key to appear in every unique constraint on a
            // partitioned table.
            migrationBuilder.Sql("""
                CREATE TABLE audit.audit_events (
                    id                   uuid                     NOT NULL,
                    occurred_at          timestamp with time zone NOT NULL,
                    application          character varying(64)    NOT NULL,
                    module               character varying(64)    NOT NULL,
                    action               character varying(128)   NOT NULL,
                    resource_type        character varying(128),
                    resource_id          character varying(128),
                    actor_user_id        uuid,
                    actor_username       character varying(64),
                    on_behalf_of_user_id uuid,
                    ip_address           character varying(45),
                    user_agent           character varying(512),
                    device_id            character varying(128),
                    request_id           character varying(128),
                    correlation_id       character varying(128),
                    result               integer                  NOT NULL,
                    old_value            jsonb,
                    new_value            jsonb,
                    metadata             jsonb,
                    CONSTRAINT pk_audit_events PRIMARY KEY (id, occurred_at)
                ) PARTITION BY RANGE (occurred_at);
                """);

            // A default partition catches anything outside the months that
            // exist. Without it an insert with no matching partition is
            // rejected outright, and losing an audit event because a
            // maintenance job was late is worse than storing it in the wrong
            // file. Rows here are a signal that maintenance is behind.
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS audit.audit_events_default
                PARTITION OF audit.audit_events DEFAULT;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_audit_action_time",
                schema: "audit",
                table: "audit_events",
                columns: new[] { "action", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_actor_time",
                schema: "audit",
                table: "audit_events",
                columns: new[] { "actor_user_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_application_module_time",
                schema: "audit",
                table: "audit_events",
                columns: new[] { "application", "module", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_correlation",
                schema: "audit",
                table: "audit_events",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_resource_time",
                schema: "audit",
                table: "audit_events",
                columns: new[] { "resource_type", "resource_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_result_time",
                schema: "audit",
                table: "audit_events",
                columns: new[] { "result", "occurred_at" });

            // ---------------------------------------------------------------
            // Append-only, enforced by the database rather than promised by the
            // code (ARCHITECTURE.md §15.4).
            //
            // A rule that only lives in the application is a rule that survives
            // exactly as long as nobody opens psql. This revokes the privilege
            // itself: the role the application connects as can INSERT and SELECT
            // here and can do nothing else, so an UPDATE or DELETE fails at the
            // database no matter what issues it.
            //
            // Written defensively because the migration may run as a role that
            // is not the owner, or on a platform where the application role has
            // another name. A failure to tighten privileges must not stop the
            // schema being created - it is reported instead, and the deployment
            // checklist covers it (docs/audit/README.md).
            // ---------------------------------------------------------------
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    application_role text := current_user;
                BEGIN
                    EXECUTE format(
                        'REVOKE ALL ON ALL TABLES IN SCHEMA audit FROM %I', application_role);
                    EXECUTE format(
                        'GRANT INSERT, SELECT ON ALL TABLES IN SCHEMA audit TO %I', application_role);
                    EXECUTE format(
                        'ALTER DEFAULT PRIVILEGES IN SCHEMA audit GRANT INSERT, SELECT ON TABLES TO %I',
                        application_role);
                EXCEPTION WHEN insufficient_privilege THEN
                    RAISE WARNING
                        'Could not restrict privileges on the audit schema. Append-only is NOT '
                        'enforced at the database. Grant INSERT and SELECT only, manually.';
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS audit.audit_events CASCADE;");
        }
    }
}
