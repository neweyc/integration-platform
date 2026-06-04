using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlPlane.Infrastructure.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(AppDbContext))]
    [Migration("20260604090000_ScopeWebhookDeliveryIdToIntegration")]
    public partial class ScopeWebhookDeliveryIdToIntegration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_work_items_TenantId_DeliveryId",
                table: "work_items");

            migrationBuilder.CreateIndex(
                name: "IX_work_items_TenantId_IntegrationId_DeliveryId",
                table: "work_items",
                columns: new[] { "TenantId", "IntegrationId", "DeliveryId" },
                unique: true,
                filter: "\"DeliveryId\" IS NOT NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_work_items_TenantId_IntegrationId_DeliveryId",
                table: "work_items");

            migrationBuilder.CreateIndex(
                name: "IX_work_items_TenantId_DeliveryId",
                table: "work_items",
                columns: new[] { "TenantId", "DeliveryId" },
                unique: true,
                filter: "\"DeliveryId\" IS NOT NULL");
        }
    }
}
