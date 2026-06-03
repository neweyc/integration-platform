using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlPlane.Infrastructure.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(AppDbContext))]
    [Migration("20260603030000_AddWorkItemIntegrationIndex")]
    public partial class AddWorkItemIntegrationIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // FK index for work_items.IntegrationId — implied by the model relationship but
            // never created by the hand-written AddWorkItems migration.
            migrationBuilder.CreateIndex(
                name: "IX_work_items_IntegrationId",
                table: "work_items",
                column: "IntegrationId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_work_items_IntegrationId",
                table: "work_items");
        }
    }
}
