using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCP.Modules.Documents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialDocumentSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "documents");

            migrationBuilder.CreateTable(
                name: "access_log",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<int>(type: "integer", nullable: false),
                    was_allowed = table.Column<bool>(type: "boolean", nullable: false),
                    detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "access_rules",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_kind = table.Column<int>(type: "integer", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    includes_sub_units = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "documents",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    current_version_number = table.Column<int>(type: "integer", nullable: false),
                    marked_for_deletion_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    marked_for_deletion_by = table.Column<Guid>(type: "uuid", nullable: true),
                    purge_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    purged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "links",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    resource_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_links", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "versions",
                schema: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    file_name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    content_type = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    size_in_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    object_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    content_removed = table.Column<bool>(type: "boolean", nullable: false),
                    content_removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_versions_documents_document_id",
                        column: x => x.document_id,
                        principalSchema: "documents",
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_access_log_actor",
                schema: "documents",
                table: "access_log",
                columns: new[] { "actor_user_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_access_log_denied",
                schema: "documents",
                table: "access_log",
                column: "was_allowed",
                filter: "was_allowed = false");

            migrationBuilder.CreateIndex(
                name: "ix_access_log_document",
                schema: "documents",
                table: "access_log",
                columns: new[] { "document_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_access_rules_document",
                schema: "documents",
                table: "access_rules",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_rules_subject",
                schema: "documents",
                table: "access_rules",
                columns: new[] { "subject_kind", "subject_id" });

            migrationBuilder.CreateIndex(
                name: "ux_access_rules_document_subject",
                schema: "documents",
                table: "access_rules",
                columns: new[] { "document_id", "subject_kind", "subject_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_documents_owner",
                schema: "documents",
                table: "documents",
                columns: new[] { "owner_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_documents_purge_due",
                schema: "documents",
                table: "documents",
                column: "purge_after",
                filter: "status = 2");

            migrationBuilder.CreateIndex(
                name: "ix_documents_unit",
                schema: "documents",
                table: "documents",
                columns: new[] { "organization_unit_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_links_resource",
                schema: "documents",
                table: "links",
                columns: new[] { "resource_type", "resource_id" });

            migrationBuilder.CreateIndex(
                name: "ux_links_document_resource",
                schema: "documents",
                table: "links",
                columns: new[] { "document_id", "resource_type", "resource_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_versions_document_number",
                schema: "documents",
                table: "versions",
                columns: new[] { "document_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_versions_object_key",
                schema: "documents",
                table: "versions",
                column: "object_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "access_log",
                schema: "documents");

            migrationBuilder.DropTable(
                name: "access_rules",
                schema: "documents");

            migrationBuilder.DropTable(
                name: "links",
                schema: "documents");

            migrationBuilder.DropTable(
                name: "versions",
                schema: "documents");

            migrationBuilder.DropTable(
                name: "documents",
                schema: "documents");
        }
    }
}
