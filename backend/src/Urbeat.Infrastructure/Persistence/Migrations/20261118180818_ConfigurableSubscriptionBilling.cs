using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urbeat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConfigurableSubscriptionBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AnnualDiscountPercentSnapshot",
                table: "SellerSubscriptions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BillingPeriod",
                table: "SellerSubscriptions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "PlanDescriptionSnapshot",
                table: "SellerSubscriptions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "PlanLevelSnapshot",
                table: "SellerSubscriptions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TrialDaysSnapshot",
                table: "SellerSubscriptions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "TrialEndsAtUtc",
                table: "SellerSubscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AnnualDiscountPercent",
                table: "Plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "AnnualEnabled",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Level",
                table: "Plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "MonthlyEnabled",
                table: "Plans",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            // Preserva a ordem comercial percebida (preço) como nível inicial. O
            // administrador pode reordenar depois; nenhum plano existente é removido.
            migrationBuilder.Sql(
                """
                WITH ranked AS (
                    SELECT "Id", ROW_NUMBER() OVER (ORDER BY "Amount", "Name", "Id") - 1 AS level
                    FROM "Plans"
                )
                UPDATE "Plans" AS p
                SET "Level" = ranked.level
                FROM ranked
                WHERE p."Id" = ranked."Id";
                """);

            // Assinaturas existentes recebem snapshot do plano vinculado. Datas são
            // mantidas em UTC e nenhum valor comercial atual é sobrescrito.
            migrationBuilder.Sql(
                """
                UPDATE "SellerSubscriptions" AS s
                SET "TrialDaysSnapshot" = COALESCE(p."TrialDays", 0),
                    "PlanDescriptionSnapshot" = COALESCE(p."Description", ''),
                    "AnnualDiscountPercentSnapshot" = COALESCE(p."AnnualDiscountPercent", 0),
                    "PlanLevelSnapshot" = COALESCE(p."Level", 0),
                    "TrialEndsAtUtc" = CASE
                        WHEN COALESCE(p."TrialDays", 0) > 0
                            THEN s."StartDateUtc" + make_interval(days => p."TrialDays")
                        ELSE NULL
                    END
                FROM "Plans" AS p
                WHERE s."PlanId" = p."Id";
                """);

            migrationBuilder.CreateTable(
                name: "SellerPlanSelections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    BillingPeriod = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerPlanSelections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SellerPlanSelections_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SellerSubscriptionChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousPlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    PreviousPlanName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    PreviousPlanAmount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    PreviousBillingPeriod = table.Column<int>(type: "integer", nullable: false),
                    PreviousPlanLevel = table.Column<int>(type: "integer", nullable: false),
                    NewPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    NewPlanName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    NewPlanAmount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    NewBillingPeriod = table.Column<int>(type: "integer", nullable: false),
                    NewPlanLevel = table.Column<int>(type: "integer", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerSubscriptionChanges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SellerSubscriptionCharges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlanNameSnapshot = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    BillingPeriod = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DueDateUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BillingPeriodStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BillingPeriodEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerSubscriptionCharges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionGatewayConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Environment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ApiBaseUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    WebhookUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ApiKeySecretReference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    WebhookSecretReference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionGatewayConfigurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionGatewayConfigurations_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Plans_Level",
                table: "Plans",
                column: "Level");

            migrationBuilder.CreateIndex(
                name: "IX_SellerPlanSelections_PlanId",
                table: "SellerPlanSelections",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_SellerPlanSelections_UserId",
                table: "SellerPlanSelections",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SellerSubscriptionChanges_SellerUserId",
                table: "SellerSubscriptionChanges",
                column: "SellerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SellerSubscriptionChanges_SubscriptionId",
                table: "SellerSubscriptionChanges",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_SellerSubscriptionCharges_DueDateUtc",
                table: "SellerSubscriptionCharges",
                column: "DueDateUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SellerSubscriptionCharges_IdempotencyKey",
                table: "SellerSubscriptionCharges",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SellerSubscriptionCharges_SellerUserId",
                table: "SellerSubscriptionCharges",
                column: "SellerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionGatewayConfigurations_PlanId",
                table: "SubscriptionGatewayConfigurations",
                column: "PlanId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SellerPlanSelections");

            migrationBuilder.DropTable(
                name: "SellerSubscriptionChanges");

            migrationBuilder.DropTable(
                name: "SellerSubscriptionCharges");

            migrationBuilder.DropTable(
                name: "SubscriptionGatewayConfigurations");

            migrationBuilder.DropIndex(
                name: "IX_Plans_Level",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "AnnualDiscountPercentSnapshot",
                table: "SellerSubscriptions");

            migrationBuilder.DropColumn(
                name: "BillingPeriod",
                table: "SellerSubscriptions");

            migrationBuilder.DropColumn(
                name: "PlanDescriptionSnapshot",
                table: "SellerSubscriptions");

            migrationBuilder.DropColumn(
                name: "PlanLevelSnapshot",
                table: "SellerSubscriptions");

            migrationBuilder.DropColumn(
                name: "TrialDaysSnapshot",
                table: "SellerSubscriptions");

            migrationBuilder.DropColumn(
                name: "TrialEndsAtUtc",
                table: "SellerSubscriptions");

            migrationBuilder.DropColumn(
                name: "AnnualDiscountPercent",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "AnnualEnabled",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "Level",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "MonthlyEnabled",
                table: "Plans");
        }
    }
}
