using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCP.Modules.Notifications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialNotificationSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notifications");

            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    locale = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    template_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    template_version = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "preferences",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_preferences", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "templates",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    locale = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    variables = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "deliveries",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    notification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    provider_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    provider_response = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deliveries", x => x.id);
                    table.ForeignKey(
                        name: "fk_deliveries_notifications_notification_id",
                        column: x => x.notification_id,
                        principalSchema: "notifications",
                        principalTable: "notifications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_deliveries_notification_attempt",
                schema: "notifications",
                table: "deliveries",
                columns: new[] { "notification_id", "attempt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_pending",
                schema: "notifications",
                table: "notifications",
                columns: new[] { "status", "created_at" },
                filter: "status = 1");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_recipient",
                schema: "notifications",
                table: "notifications",
                columns: new[] { "recipient_user_id", "channel", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_preferences_user_category_channel",
                schema: "notifications",
                table: "preferences",
                columns: new[] { "user_id", "category", "channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_templates_code_locale",
                schema: "notifications",
                table: "templates",
                columns: new[] { "code", "locale" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "deliveries",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "preferences",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "templates",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "notifications",
                schema: "notifications");
        }
    }
}
