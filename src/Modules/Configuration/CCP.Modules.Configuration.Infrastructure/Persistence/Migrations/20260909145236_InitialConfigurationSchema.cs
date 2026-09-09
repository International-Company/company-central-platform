using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCP.Modules.Configuration.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialConfigurationSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "configuration");

            migrationBuilder.CreateTable(
                name: "changes",
                schema: "configuration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    scope = table.Column<int>(type: "integer", nullable: false),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: true),
                    old_value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    new_value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_changes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "definitions",
                schema: "configuration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    application_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    value_type = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    default_value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    is_sensitive = table.Column<bool>(type: "boolean", nullable: false),
                    minimum = table.Column<long>(type: "bigint", nullable: true),
                    maximum = table.Column<long>(type: "bigint", nullable: true),
                    allowed_values = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
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
                name: "feature_flags",
                schema: "configuration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    application_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    targeted_roles = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    targeted_units = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feature_flags", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "values",
                schema: "configuration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    scope = table.Column<int>(type: "integer", nullable: false),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: true),
                    value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_values", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "version",
                schema: "configuration",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_version", x => x.id);
                });

            migrationBuilder.InsertData(
                schema: "configuration",
                table: "version",
                columns: new[] { "id", "updated_at", "version" },
                values: new object[] { 1, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 1L });

            migrationBuilder.CreateIndex(
                name: "ix_changes_actor",
                schema: "configuration",
                table: "changes",
                columns: new[] { "changed_by", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_changes_key",
                schema: "configuration",
                table: "changes",
                columns: new[] { "key", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_definitions_application",
                schema: "configuration",
                table: "definitions",
                column: "application_code");

            migrationBuilder.CreateIndex(
                name: "ux_definitions_key",
                schema: "configuration",
                table: "definitions",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_feature_flags_key",
                schema: "configuration",
                table: "feature_flags",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_values_key_scope",
                schema: "configuration",
                table: "values",
                columns: new[] { "key", "scope", "scope_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "changes",
                schema: "configuration");

            migrationBuilder.DropTable(
                name: "definitions",
                schema: "configuration");

            migrationBuilder.DropTable(
                name: "feature_flags",
                schema: "configuration");

            migrationBuilder.DropTable(
                name: "values",
                schema: "configuration");

            migrationBuilder.DropTable(
                name: "version",
                schema: "configuration");
        }
    }
}
