using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCP.Modules.Authorization.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ApplicationCredentialsAndGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "application_assignments",
                schema: "authz",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_type = table.Column<int>(type: "integer", nullable: false),
                    scope_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    granted_by = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_application_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_application_assignments_applications_application_id",
                        column: x => x.application_id,
                        principalSchema: "authz",
                        principalTable: "applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_application_assignments_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "authz",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "application_credentials",
                schema: "authz",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    secret_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by = table.Column<Guid>(type: "uuid", nullable: true),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_application_credentials", x => x.id);
                    table.ForeignKey(
                        name: "fk_application_credentials_applications_application_id",
                        column: x => x.application_id,
                        principalSchema: "authz",
                        principalTable: "applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_application_assignments_application_role",
                schema: "authz",
                table: "application_assignments",
                columns: new[] { "application_id", "role_id" });

            migrationBuilder.CreateIndex(
                name: "ix_application_assignments_role_id",
                schema: "authz",
                table: "application_assignments",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_application_credentials_application",
                schema: "authz",
                table: "application_credentials",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "ux_application_credentials_client_id",
                schema: "authz",
                table: "application_credentials",
                column: "client_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "application_assignments",
                schema: "authz");

            migrationBuilder.DropTable(
                name: "application_credentials",
                schema: "authz");
        }
    }
}
