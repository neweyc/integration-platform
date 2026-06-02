using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlPlane.Infrastructure.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(AppDbContext))]
    [Migration("20260602010000_AddWorkItems")]
    public partial class AddWorkItems : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create work_items table — the unified dispatch queue for all trigger types
            migrationBuilder.CreateTable(
                name: "work_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IntegrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Environment = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TriggerSource = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AvailableAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClaimOwner = table.Column<Guid>(type: "uuid", nullable: true),
                    ClaimExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ManualRunRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    Payload = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_work_items_integrations_IntegrationId",
                        column: x => x.IntegrationId,
                        principalTable: "integrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_work_items_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_work_items_TenantId_IntegrationId_Status",
                table: "work_items",
                columns: new[] { "TenantId", "IntegrationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_work_items_TenantId_Environment_Status_AvailableAt",
                table: "work_items",
                columns: new[] { "TenantId", "Environment", "Status", "AvailableAt" });

            // Add WorkItemId to execution_records
            migrationBuilder.AddColumn<Guid>(
                name: "WorkItemId",
                table: "execution_records",
                type: "uuid",
                nullable: true);

            // Remove lease tracking from integration_schedule_states — leases move to work_items
            migrationBuilder.DropIndex(
                name: "IX_integration_schedule_states_TenantId_LeaseExpiresAt",
                table: "integration_schedule_states");

            migrationBuilder.DropColumn(
                name: "LeaseOwnerId",
                table: "integration_schedule_states");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                table: "integration_schedule_states");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "work_items");

            migrationBuilder.DropColumn(name: "WorkItemId", table: "execution_records");

            migrationBuilder.AddColumn<Guid>(
                name: "LeaseOwnerId",
                table: "integration_schedule_states",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseExpiresAt",
                table: "integration_schedule_states",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_integration_schedule_states_TenantId_LeaseExpiresAt",
                table: "integration_schedule_states",
                columns: new[] { "TenantId", "LeaseExpiresAt" });
        }
    }
}
