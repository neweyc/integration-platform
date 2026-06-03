using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlPlane.Infrastructure.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(AppDbContext))]
    [Migration("20260602040000_AddWebhookDeliveries")]
    public partial class AddWebhookDeliveries : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "webhook_deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IntegrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    WorkItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_deliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_webhook_deliveries_integrations_IntegrationId",
                        column: x => x.IntegrationId,
                        principalTable: "integrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_webhook_deliveries_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_webhook_deliveries_TenantId_IntegrationId_ReceivedAt",
                table: "webhook_deliveries",
                columns: new[] { "TenantId", "IntegrationId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_webhook_deliveries_IntegrationId",
                table: "webhook_deliveries",
                column: "IntegrationId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "webhook_deliveries");
        }
    }
}
