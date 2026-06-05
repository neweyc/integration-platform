using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlPlane.Infrastructure.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(AppDbContext))]
    [Migration("20260604110000_AddTriggerEvents")]
    public partial class AddTriggerEvents : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trigger_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IntegrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    IntegrationTriggerId = table.Column<Guid>(type: "uuid", nullable: true),
                    AdapterKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EventKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Outcome = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    WorkItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    MetadataJson = table.Column<string>(type: "text", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trigger_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_trigger_events_integration_triggers_IntegrationTriggerId",
                        column: x => x.IntegrationTriggerId,
                        principalTable: "integration_triggers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_trigger_events_integrations_IntegrationId",
                        column: x => x.IntegrationId,
                        principalTable: "integrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_trigger_events_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_trigger_events_IntegrationId",
                table: "trigger_events",
                column: "IntegrationId");

            migrationBuilder.CreateIndex(
                name: "IX_trigger_events_IntegrationTriggerId",
                table: "trigger_events",
                column: "IntegrationTriggerId");

            migrationBuilder.CreateIndex(
                name: "IX_trigger_events_TenantId_AdapterKey_Outcome_ReceivedAt",
                table: "trigger_events",
                columns: new[] { "TenantId", "AdapterKey", "Outcome", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_trigger_events_TenantId_IntegrationId_ReceivedAt",
                table: "trigger_events",
                columns: new[] { "TenantId", "IntegrationId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_trigger_events_TenantId_IntegrationTriggerId_ReceivedAt",
                table: "trigger_events",
                columns: new[] { "TenantId", "IntegrationTriggerId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_trigger_events_TenantId_Source_EventKey",
                table: "trigger_events",
                columns: new[] { "TenantId", "Source", "EventKey" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "trigger_events");
        }
    }
}
