using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCP.Modules.Workflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialWorkflowSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "workflow");

            migrationBuilder.CreateTable(
                name: "definitions",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    initial_step_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "instances",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    definition_version = table.Column<int>(type: "integer", nullable: false),
                    application_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    resource_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    resource_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    current_step_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_instances", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tasks",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    assigned_to_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delegated_from_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_with = table.Column<int>(type: "integer", nullable: true),
                    escalated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tasks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "steps",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    service_level = table.Column<TimeSpan>(type: "interval", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    assignee_position_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assignee_role_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assignee_strategy = table.Column<int>(type: "integer", nullable: false),
                    assignee_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assignee_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_steps", x => x.id);
                    table.ForeignKey(
                        name: "fk_steps_definitions_definition_id",
                        column: x => x.definition_id,
                        principalSchema: "workflow",
                        principalTable: "definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "instance_actions",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    action = table.Column<int>(type: "integer", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_instance_actions", x => x.id);
                    table.ForeignKey(
                        name: "fk_instance_actions_instances_instance_id",
                        column: x => x.instance_id,
                        principalSchema: "workflow",
                        principalTable: "instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "transitions",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<int>(type: "integer", nullable: false),
                    target_step_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transitions", x => x.id);
                    table.ForeignKey(
                        name: "fk_transitions_steps_step_id",
                        column: x => x.step_id,
                        principalSchema: "workflow",
                        principalTable: "steps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_definitions_app_code_status",
                schema: "workflow",
                table: "definitions",
                columns: new[] { "application_code", "code", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_definitions_app_code_version",
                schema: "workflow",
                table: "definitions",
                columns: new[] { "application_code", "code", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_instance_actions_instance_time",
                schema: "workflow",
                table: "instance_actions",
                columns: new[] { "instance_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_instances_requested_by",
                schema: "workflow",
                table: "instances",
                column: "requested_by");

            migrationBuilder.CreateIndex(
                name: "ix_instances_resource",
                schema: "workflow",
                table: "instances",
                columns: new[] { "application_code", "resource_type", "resource_id" });

            migrationBuilder.CreateIndex(
                name: "ix_instances_status_started",
                schema: "workflow",
                table: "instances",
                columns: new[] { "status", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ux_steps_definition_key",
                schema: "workflow",
                table: "steps",
                columns: new[] { "definition_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tasks_assignee_status",
                schema: "workflow",
                table: "tasks",
                columns: new[] { "assigned_to_user_id", "status", "assigned_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_due",
                schema: "workflow",
                table: "tasks",
                column: "due_at",
                filter: "status = 1 AND due_at IS NOT NULL AND escalated_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_instance_status",
                schema: "workflow",
                table: "tasks",
                columns: new[] { "instance_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_transitions_step_action",
                schema: "workflow",
                table: "transitions",
                columns: new[] { "step_id", "action" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "instance_actions",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "tasks",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "transitions",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "instances",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "steps",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "definitions",
                schema: "workflow");
        }
    }
}
