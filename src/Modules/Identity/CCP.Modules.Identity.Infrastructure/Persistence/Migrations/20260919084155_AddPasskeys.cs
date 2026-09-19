using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCP.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPasskeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "webauthn_challenges",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ceremony = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    redeemed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webauthn_challenges", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "webauthn_credentials",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credential_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    public_key = table.Column<byte[]>(type: "bytea", nullable: false),
                    algorithm = table.Column<int>(type: "integer", nullable: false),
                    sign_count = table.Column<long>(type: "bigint", nullable: false),
                    authenticator_guid = table.Column<Guid>(type: "uuid", nullable: true),
                    user_verified = table.Column<bool>(type: "boolean", nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webauthn_credentials", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_webauthn_challenges_expires_at",
                schema: "identity",
                table: "webauthn_challenges",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_webauthn_challenges_value",
                schema: "identity",
                table: "webauthn_challenges",
                column: "value",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_webauthn_credentials_credential_id",
                schema: "identity",
                table: "webauthn_credentials",
                column: "credential_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_webauthn_credentials_user_id_revoked_at",
                schema: "identity",
                table: "webauthn_credentials",
                columns: new[] { "user_id", "revoked_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "webauthn_challenges",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "webauthn_credentials",
                schema: "identity");
        }
    }
}
