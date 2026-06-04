using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlPlane.Infrastructure.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(AppDbContext))]
    [Migration("20260604010000_AddWorkflowDagFoundation")]
    public partial class AddWorkflowDagFoundation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowNodeId",
                table: "work_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowRunId",
                table: "work_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "workflow_definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Environment = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_definitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workflow_definitions_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_nodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IntegrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_nodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workflow_nodes_integrations_IntegrationId",
                        column: x => x.IntegrationId,
                        principalTable: "integrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_nodes_workflow_definitions_WorkflowDefinitionId",
                        column: x => x.WorkflowDefinitionId,
                        principalTable: "workflow_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workflow_runs_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_workflow_runs_workflow_definitions_WorkflowDefinitionId",
                        column: x => x.WorkflowDefinitionId,
                        principalTable: "workflow_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_edges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_edges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workflow_edges_workflow_definitions_WorkflowDefinitionId",
                        column: x => x.WorkflowDefinitionId,
                        principalTable: "workflow_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_workflow_edges_workflow_nodes_FromNodeId",
                        column: x => x.FromNodeId,
                        principalTable: "workflow_nodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_workflow_edges_workflow_nodes_ToNodeId",
                        column: x => x.ToNodeId,
                        principalTable: "workflow_nodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_node_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    WorkItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExecutionRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_node_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workflow_node_runs_work_items_WorkItemId",
                        column: x => x.WorkItemId,
                        principalTable: "work_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_workflow_node_runs_workflow_nodes_WorkflowNodeId",
                        column: x => x.WorkflowNodeId,
                        principalTable: "workflow_nodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_node_runs_workflow_runs_WorkflowRunId",
                        column: x => x.WorkflowRunId,
                        principalTable: "workflow_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_work_items_TenantId_WorkflowRunId_WorkflowNodeId",
                table: "work_items",
                columns: new[] { "TenantId", "WorkflowRunId", "WorkflowNodeId" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_definitions_TenantId_Slug",
                table: "workflow_definitions",
                columns: new[] { "TenantId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_definitions_TenantId",
                table: "workflow_definitions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_edges_FromNodeId",
                table: "workflow_edges",
                column: "FromNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_edges_TenantId_WorkflowDefinitionId_FromNodeId_ToNodeId",
                table: "workflow_edges",
                columns: new[] { "TenantId", "WorkflowDefinitionId", "FromNodeId", "ToNodeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_edges_ToNodeId",
                table: "workflow_edges",
                column: "ToNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_edges_WorkflowDefinitionId",
                table: "workflow_edges",
                column: "WorkflowDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_node_runs_TenantId_WorkflowRunId_WorkflowNodeId",
                table: "workflow_node_runs",
                columns: new[] { "TenantId", "WorkflowRunId", "WorkflowNodeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_node_runs_WorkflowNodeId",
                table: "workflow_node_runs",
                column: "WorkflowNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_node_runs_WorkflowRunId",
                table: "workflow_node_runs",
                column: "WorkflowRunId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_node_runs_WorkItemId",
                table: "workflow_node_runs",
                column: "WorkItemId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_nodes_IntegrationId",
                table: "workflow_nodes",
                column: "IntegrationId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_nodes_TenantId_WorkflowDefinitionId_Key",
                table: "workflow_nodes",
                columns: new[] { "TenantId", "WorkflowDefinitionId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_nodes_WorkflowDefinitionId",
                table: "workflow_nodes",
                column: "WorkflowDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_runs_TenantId_WorkflowDefinitionId_StartedAt",
                table: "workflow_runs",
                columns: new[] { "TenantId", "WorkflowDefinitionId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_runs_TenantId",
                table: "workflow_runs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_runs_WorkflowDefinitionId",
                table: "workflow_runs",
                column: "WorkflowDefinitionId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "workflow_edges");
            migrationBuilder.DropTable(name: "workflow_node_runs");
            migrationBuilder.DropTable(name: "workflow_nodes");
            migrationBuilder.DropTable(name: "workflow_runs");
            migrationBuilder.DropTable(name: "workflow_definitions");

            migrationBuilder.DropIndex(
                name: "IX_work_items_TenantId_WorkflowRunId_WorkflowNodeId",
                table: "work_items");

            migrationBuilder.DropColumn(name: "WorkflowNodeId", table: "work_items");
            migrationBuilder.DropColumn(name: "WorkflowRunId", table: "work_items");
        }
    }
}
