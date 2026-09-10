using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCP.Modules.Notifications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessedEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "processed_events",
                schema: "notifications",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processed_events", x => new { x.event_id, x.reason });
                });

            migrationBuilder.CreateIndex(
                name: "ix_processed_events_processed_at",
                schema: "notifications",
                table: "processed_events",
                column: "processed_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "processed_events",
                schema: "notifications");
        }
    }
}
