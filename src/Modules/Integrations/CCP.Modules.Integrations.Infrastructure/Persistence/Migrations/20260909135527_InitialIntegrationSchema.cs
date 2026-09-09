using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCP.Modules.Integrations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialIntegrationSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "integrations");

            migrationBuilder.CreateTable(
                name: "call_log",
                schema: "integrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    endpoint_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    request_payload = table.Column<string>(type: "text", nullable: false),
                    response_payload = table.Column<string>(type: "text", nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: true),
                    outcome = table.Column<int>(type: "integer", nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    duration_ms = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_call_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "providers",
                schema: "integrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    base_address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    credential_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    timeout = table.Column<TimeSpan>(type: "interval", nullable: false),
                    max_retries = table.Column<int>(type: "integer", nullable: false),
                    failures_before_breaking = table.Column<int>(type: "integer", nullable: false),
                    break_duration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    max_concurrent_calls = table.Column<int>(type: "integer", nullable: false),
                    redacted_fields = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_providers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "webhook_receipts",
                schema: "integrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    signature = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_receipts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "endpoints",
                schema: "integrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    path_template = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_endpoints", x => x.id);
                    table.ForeignKey(
                        name: "fk_endpoints_providers_provider_id",
                        column: x => x.provider_id,
                        principalSchema: "integrations",
                        principalTable: "providers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_call_log_correlation",
                schema: "integrations",
                table: "call_log",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_call_log_provider",
                schema: "integrations",
                table: "call_log",
                columns: new[] { "provider_code", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_call_log_started",
                schema: "integrations",
                table: "call_log",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "ux_endpoints_provider_key",
                schema: "integrations",
                table: "endpoints",
                columns: new[] { "provider_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_providers_code",
                schema: "integrations",
                table: "providers",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_webhook_receipts_received",
                schema: "integrations",
                table: "webhook_receipts",
                column: "received_at");

            migrationBuilder.CreateIndex(
                name: "ux_webhook_receipts_signature",
                schema: "integrations",
                table: "webhook_receipts",
                columns: new[] { "provider_code", "signature" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "call_log",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "endpoints",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "webhook_receipts",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "providers",
                schema: "integrations");
        }
    }
}
