using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SecureRefreshTokenRotationAndPaymentAttempt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Legacy rows stored the raw token, which cannot be converted to a hash. Purge them so
            // no plaintext token survives and the new unique index on TokenHash cannot collide on
            // the empty default value. This is an intentional global logout: every existing session
            // must re-authenticate after this migration is applied (operational note: coordinate the
            // rollout with users, since refresh tokens are invalidated cluster-wide).
            migrationBuilder.Sql("DELETE FROM \"RefreshTokens\";");

            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_Token",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "Token",
                table: "RefreshTokens");

            migrationBuilder.AddColumn<string>(
                name: "TokenHash",
                table: "RefreshTokens",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Attempt",
                table: "Payments",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "TokenHash",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "Attempt",
                table: "Payments");

            // The plaintext token cannot be recovered from the SHA-256 hash stored by the Up
            // migration, so this rollback restores a structurally valid schema with invalid tokens
            // (every session must re-authenticate). The column is added nullable and each row is
            // backfilled with a unique placeholder derived from its id before it is made NOT NULL,
            // so the unique index below never collides — even when multiple rows exist.
            migrationBuilder.AddColumn<string>(
                name: "Token",
                table: "RefreshTokens",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE \"RefreshTokens\" SET \"Token\" = 'invalid-' || \"Id\"::text;");

            migrationBuilder.AlterColumn<string>(
                name: "Token",
                table: "RefreshTokens",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_Token",
                table: "RefreshTokens",
                column: "Token",
                unique: true);
        }
    }
}
