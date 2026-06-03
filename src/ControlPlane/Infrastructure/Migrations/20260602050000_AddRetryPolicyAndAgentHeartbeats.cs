using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlPlane.Infrastructure.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(AppDbContext))]
    [Migration("20260602050000_AddRetryPolicyAndAgentHeartbeats")]
    public partial class AddRetryPolicyAndAgentHeartbeats : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RetryMaxAttempts",
                table: "integrations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RetryBackoffSeconds",
                table: "integrations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AttemptNumber",
                table: "work_items",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentExecutionId",
                table: "work_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RootExecutionId",
                table: "work_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AttemptNumber",
                table: "execution_records",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentExecutionId",
                table: "execution_records",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RootExecutionId",
                table: "execution_records",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "agent_heartbeats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentTokenId = table.Column<Guid>(type: "uuid", nullable: false),
                    Environment = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Hostname = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CurrentConcurrency = table.Column<int>(type: "integer", nullable: false),
                    MaxConcurrency = table.Column<int>(type: "integer", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_heartbeats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agent_heartbeats_agent_tokens_AgentTokenId",
                        column: x => x.AgentTokenId,
                        principalTable: "agent_tokens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_heartbeats_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_heartbeats_AgentTokenId",
                table: "agent_heartbeats",
                column: "AgentTokenId");

            migrationBuilder.CreateIndex(
                name: "IX_agent_heartbeats_TenantId_AgentTokenId",
                table: "agent_heartbeats",
                columns: new[] { "TenantId", "AgentTokenId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agent_heartbeats_TenantId_Environment_LastSeenAt",
                table: "agent_heartbeats",
                columns: new[] { "TenantId", "Environment", "LastSeenAt" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "agent_heartbeats");

            migrationBuilder.DropColumn(name: "RetryMaxAttempts", table: "integrations");
            migrationBuilder.DropColumn(name: "RetryBackoffSeconds", table: "integrations");
            migrationBuilder.DropColumn(name: "AttemptNumber", table: "work_items");
            migrationBuilder.DropColumn(name: "ParentExecutionId", table: "work_items");
            migrationBuilder.DropColumn(name: "RootExecutionId", table: "work_items");
            migrationBuilder.DropColumn(name: "AttemptNumber", table: "execution_records");
            migrationBuilder.DropColumn(name: "ParentExecutionId", table: "execution_records");
            migrationBuilder.DropColumn(name: "RootExecutionId", table: "execution_records");
        }
    }
}
