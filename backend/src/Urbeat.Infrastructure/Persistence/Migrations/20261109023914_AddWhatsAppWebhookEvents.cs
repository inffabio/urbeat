using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppWebhookEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WhatsAppWebhookEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    EventKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EventType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    MessageId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    PhoneNumberId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    SenderWaId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    EventTimestamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProcessingError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppWebhookEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppWebhookEvents_MessageId",
                table: "WhatsAppWebhookEvents",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppWebhookEvents_Provider_EventKey",
                table: "WhatsAppWebhookEvents",
                columns: new[] { "Provider", "EventKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppWebhookEvents_ReceivedAtUtc",
                table: "WhatsAppWebhookEvents",
                column: "ReceivedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WhatsAppWebhookEvents");
        }
    }
}
