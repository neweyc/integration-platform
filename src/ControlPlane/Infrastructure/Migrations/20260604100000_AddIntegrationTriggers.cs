using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlPlane.Infrastructure.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(AppDbContext))]
    [Migration("20260604100000_AddIntegrationTriggers")]
    public partial class AddIntegrationTriggers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "integration_triggers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IntegrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CronExpression = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    EncryptedWebhookSecret = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_triggers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_integration_triggers_integrations_IntegrationId",
                        column: x => x.IntegrationId,
                        principalTable: "integrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_integration_triggers_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                INSERT INTO "integration_triggers"
                    ("Id", "TenantId", "IntegrationId", "Type", "Name", "Slug", "Enabled", "CronExpression", "EncryptedWebhookSecret", "CreatedAt", "UpdatedAt")
                SELECT
                    (substr(md5("Id"::text || ':' || "TriggerType"), 1, 8) || '-' ||
                     substr(md5("Id"::text || ':' || "TriggerType"), 9, 4) || '-' ||
                     substr(md5("Id"::text || ':' || "TriggerType"), 13, 4) || '-' ||
                     substr(md5("Id"::text || ':' || "TriggerType"), 17, 4) || '-' ||
                     substr(md5("Id"::text || ':' || "TriggerType"), 21, 12))::uuid,
                    "TenantId",
                    "Id",
                    "TriggerType",
                    CASE "TriggerType" WHEN 'Scheduled' THEN 'Schedule' WHEN 'Webhook' THEN 'Webhook' ELSE "TriggerType" END,
                    lower("TriggerType"),
                    "Status" = 'Enabled',
                    CASE WHEN "TriggerType" = 'Scheduled' THEN "CronExpression" ELSE NULL END,
                    CASE WHEN "TriggerType" = 'Webhook' THEN "EncryptedWebhookSecret" ELSE NULL END,
                    "CreatedAt",
                    "UpdatedAt"
                FROM "integrations"
                WHERE "TriggerType" IN ('Scheduled', 'Webhook');
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "IntegrationTriggerId",
                table: "webhook_deliveries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IntegrationTriggerId",
                table: "work_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IntegrationTriggerId",
                table: "integration_schedule_states",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "integration_schedule_states" s
                SET "IntegrationTriggerId" = t."Id"
                FROM "integration_triggers" t
                WHERE t."IntegrationId" = s."IntegrationId"
                  AND t."Type" = 'Scheduled';

                UPDATE "work_items" w
                SET "IntegrationTriggerId" = t."Id"
                FROM "integration_triggers" t
                WHERE t."IntegrationId" = w."IntegrationId"
                  AND t."Type" = w."TriggerSource"
                  AND w."TriggerSource" IN ('Scheduled', 'Webhook');

                UPDATE "webhook_deliveries" d
                SET "IntegrationTriggerId" = t."Id"
                FROM "integration_triggers" t
                WHERE t."IntegrationId" = d."IntegrationId"
                  AND t."Type" = 'Webhook';
                """);

            migrationBuilder.DropIndex(
                name: "IX_integration_schedule_states_IntegrationId",
                table: "integration_schedule_states");

            migrationBuilder.DropIndex(
                name: "IX_work_items_TenantId_IntegrationId_DeliveryId",
                table: "work_items");

            migrationBuilder.AlterColumn<Guid>(
                name: "IntegrationTriggerId",
                table: "webhook_deliveries",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "IntegrationTriggerId",
                table: "integration_schedule_states",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.DropColumn(name: "TriggerType", table: "integrations");
            migrationBuilder.DropColumn(name: "CronExpression", table: "integrations");
            migrationBuilder.DropColumn(name: "EncryptedWebhookSecret", table: "integrations");

            migrationBuilder.CreateIndex(
                name: "IX_integration_triggers_IntegrationId",
                table: "integration_triggers",
                column: "IntegrationId");

            migrationBuilder.CreateIndex(
                name: "IX_integration_triggers_TenantId_IntegrationId_Slug",
                table: "integration_triggers",
                columns: new[] { "TenantId", "IntegrationId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_integration_triggers_TenantId_Type_Enabled",
                table: "integration_triggers",
                columns: new[] { "TenantId", "Type", "Enabled" });

            migrationBuilder.CreateIndex(
                name: "IX_integration_schedule_states_IntegrationTriggerId",
                table: "integration_schedule_states",
                column: "IntegrationTriggerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_webhook_deliveries_IntegrationTriggerId",
                table: "webhook_deliveries",
                column: "IntegrationTriggerId");

            migrationBuilder.CreateIndex(
                name: "IX_webhook_deliveries_TenantId_IntegrationTriggerId_ReceivedAt",
                table: "webhook_deliveries",
                columns: new[] { "TenantId", "IntegrationTriggerId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_work_items_IntegrationTriggerId",
                table: "work_items",
                column: "IntegrationTriggerId");

            migrationBuilder.CreateIndex(
                name: "IX_work_items_TenantId_IntegrationTriggerId_DeliveryId",
                table: "work_items",
                columns: new[] { "TenantId", "IntegrationTriggerId", "DeliveryId" },
                unique: true,
                filter: "\"IntegrationTriggerId\" IS NOT NULL AND \"DeliveryId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_work_items_TenantId_IntegrationTriggerId_Status",
                table: "work_items",
                columns: new[] { "TenantId", "IntegrationTriggerId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_integration_schedule_states_integration_triggers_IntegrationTriggerId",
                table: "integration_schedule_states",
                column: "IntegrationTriggerId",
                principalTable: "integration_triggers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_webhook_deliveries_integration_triggers_IntegrationTriggerId",
                table: "webhook_deliveries",
                column: "IntegrationTriggerId",
                principalTable: "integration_triggers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_work_items_integration_triggers_IntegrationTriggerId",
                table: "work_items",
                column: "IntegrationTriggerId",
                principalTable: "integration_triggers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_integration_schedule_states_integration_triggers_IntegrationTriggerId", table: "integration_schedule_states");
            migrationBuilder.DropForeignKey(name: "FK_webhook_deliveries_integration_triggers_IntegrationTriggerId", table: "webhook_deliveries");
            migrationBuilder.DropForeignKey(name: "FK_work_items_integration_triggers_IntegrationTriggerId", table: "work_items");

            migrationBuilder.DropIndex(name: "IX_integration_schedule_states_IntegrationTriggerId", table: "integration_schedule_states");
            migrationBuilder.DropIndex(name: "IX_integration_triggers_IntegrationId", table: "integration_triggers");
            migrationBuilder.DropIndex(name: "IX_integration_triggers_TenantId_IntegrationId_Slug", table: "integration_triggers");
            migrationBuilder.DropIndex(name: "IX_integration_triggers_TenantId_Type_Enabled", table: "integration_triggers");
            migrationBuilder.DropIndex(name: "IX_webhook_deliveries_IntegrationTriggerId", table: "webhook_deliveries");
            migrationBuilder.DropIndex(name: "IX_webhook_deliveries_TenantId_IntegrationTriggerId_ReceivedAt", table: "webhook_deliveries");
            migrationBuilder.DropIndex(name: "IX_work_items_IntegrationTriggerId", table: "work_items");
            migrationBuilder.DropIndex(name: "IX_work_items_TenantId_IntegrationTriggerId_DeliveryId", table: "work_items");
            migrationBuilder.DropIndex(name: "IX_work_items_TenantId_IntegrationTriggerId_Status", table: "work_items");

            migrationBuilder.DropTable(name: "integration_triggers");

            migrationBuilder.AddColumn<string>(name: "TriggerType", table: "integrations", type: "text", nullable: false, defaultValue: "Manual");
            migrationBuilder.AddColumn<string>(name: "CronExpression", table: "integrations", type: "character varying(100)", maxLength: 100, nullable: true);
            migrationBuilder.AddColumn<string>(name: "EncryptedWebhookSecret", table: "integrations", type: "text", nullable: true);

            migrationBuilder.DropColumn(name: "IntegrationTriggerId", table: "webhook_deliveries");
            migrationBuilder.DropColumn(name: "IntegrationTriggerId", table: "work_items");
            migrationBuilder.DropColumn(name: "IntegrationTriggerId", table: "integration_schedule_states");

            migrationBuilder.CreateIndex(
                name: "IX_integration_schedule_states_IntegrationId",
                table: "integration_schedule_states",
                column: "IntegrationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_work_items_TenantId_IntegrationId_DeliveryId",
                table: "work_items",
                columns: new[] { "TenantId", "IntegrationId", "DeliveryId" },
                unique: true,
                filter: "\"DeliveryId\" IS NOT NULL");
        }
    }
}
