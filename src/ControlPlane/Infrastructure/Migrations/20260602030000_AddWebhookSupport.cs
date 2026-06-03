using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlPlane.Infrastructure.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(AppDbContext))]
    [Migration("20260602030000_AddWebhookSupport")]
    public partial class AddWebhookSupport : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AES-encrypted webhook secret on integrations (only set for Webhook trigger type)
            migrationBuilder.AddColumn<string>(
                name: "EncryptedWebhookSecret",
                table: "integrations",
                type: "text",
                nullable: true);

            // Delivery ID on work items for idempotent webhook processing
            migrationBuilder.AddColumn<string>(
                name: "DeliveryId",
                table: "work_items",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            // Unique partial index enforces idempotent webhook delivery: a tenant cannot
            // enqueue two work items for the same delivery ID, even under concurrency.
            migrationBuilder.CreateIndex(
                name: "IX_work_items_TenantId_DeliveryId",
                table: "work_items",
                columns: new[] { "TenantId", "DeliveryId" },
                unique: true,
                filter: "\"DeliveryId\" IS NOT NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_work_items_TenantId_DeliveryId",
                table: "work_items");

            migrationBuilder.DropColumn(name: "EncryptedWebhookSecret", table: "integrations");
            migrationBuilder.DropColumn(name: "DeliveryId", table: "work_items");
        }
    }
}
