using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCP.Modules.Integrations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "webhook_deliveries",
                schema: "integrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    response_status_code = table.Column<int>(type: "integer", nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_deliveries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "webhook_subscriptions",
                schema: "integrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    endpoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    secret_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    event_types = table.Column<string[]>(type: "text[]", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    consecutive_failures = table.Column<int>(type: "integer", nullable: false),
                    suspended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    suspended_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    last_delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_subscriptions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_deliveries_due",
                schema: "integrations",
                table: "webhook_deliveries",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ux_webhook_deliveries_event",
                schema: "integrations",
                table: "webhook_deliveries",
                columns: new[] { "subscription_id", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_webhook_subscriptions_application",
                schema: "integrations",
                table: "webhook_subscriptions",
                column: "application_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "webhook_deliveries",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "webhook_subscriptions",
                schema: "integrations");
        }
    }
}
