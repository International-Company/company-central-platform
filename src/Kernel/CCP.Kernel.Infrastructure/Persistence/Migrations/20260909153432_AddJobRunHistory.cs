using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCP.Kernel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJobRunHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "job_runs",
                schema: "kernel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_ms = table.Column<double>(type: "double precision", nullable: false),
                    outcome = table.Column<int>(type: "integer", nullable: false),
                    summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    instance = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_runs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_job_runs_job_started_at",
                schema: "kernel",
                table: "job_runs",
                columns: new[] { "job", "started_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_job_runs_started_at",
                schema: "kernel",
                table: "job_runs",
                column: "started_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_runs",
                schema: "kernel");
        }
    }
}
