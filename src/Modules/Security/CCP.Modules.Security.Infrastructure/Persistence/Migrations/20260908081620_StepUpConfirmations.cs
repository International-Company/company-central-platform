using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCP.Modules.Security.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StepUpConfirmations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "step_up_confirmations",
                schema: "security",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_step_up_confirmations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_step_up_session_expiry",
                schema: "security",
                table: "step_up_confirmations",
                columns: new[] { "session_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_step_up_user_expiry",
                schema: "security",
                table: "step_up_confirmations",
                columns: new[] { "user_id", "expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "step_up_confirmations",
                schema: "security");
        }
    }
}
