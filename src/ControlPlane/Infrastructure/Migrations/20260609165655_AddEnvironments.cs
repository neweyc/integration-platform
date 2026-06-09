using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlPlane.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEnvironments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_agent_tokens_TenantId",
                table: "agent_tokens");

            migrationBuilder.CreateTable(
                name: "environments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_environments", x => x.Id);
                    table.UniqueConstraint("AK_environments_TenantId_Name", x => new { x.TenantId, x.Name });
                    table.ForeignKey(
                        name: "FK_environments_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_definitions_TenantId_Environment",
                table: "workflow_definitions",
                columns: new[] { "TenantId", "Environment" });

            migrationBuilder.CreateIndex(
                name: "IX_integrations_TenantId_Environment",
                table: "integrations",
                columns: new[] { "TenantId", "Environment" });

            migrationBuilder.CreateIndex(
                name: "IX_agent_tokens_TenantId_Environment",
                table: "agent_tokens",
                columns: new[] { "TenantId", "Environment" });

            migrationBuilder.CreateIndex(
                name: "IX_environments_TenantId_SortOrder",
                table: "environments",
                columns: new[] { "TenantId", "SortOrder" });

            // --- Data migration: canonicalize existing environment strings, then seed the registry ---
            // Environment names become first-class and case-canonical (lowercase). Lowercase every existing
            // environment column first so the values we seed and the foreign keys we add below all agree.
            // If a tenant happens to hold two rows that only differed by case (e.g. "Production" and
            // "production") that collapse into a duplicate of a unique key, the UPDATE below will fail with
            // a clear Postgres unique-violation naming the offending key — that is the intended collision report.
            migrationBuilder.Sql(@"UPDATE secrets SET ""Environment"" = lower(""Environment"");");
            migrationBuilder.Sql(@"UPDATE integrations SET ""Environment"" = lower(""Environment"");");
            migrationBuilder.Sql(@"UPDATE agent_tokens SET ""Environment"" = lower(""Environment"");");
            migrationBuilder.Sql(@"UPDATE execution_records SET ""Environment"" = lower(""Environment"");");
            migrationBuilder.Sql(@"UPDATE work_items SET ""Environment"" = lower(""Environment"");");
            migrationBuilder.Sql(@"UPDATE manual_run_requests SET ""Environment"" = lower(""Environment"");");
            migrationBuilder.Sql(@"UPDATE agent_heartbeats SET ""Environment"" = lower(""Environment"");");
            migrationBuilder.Sql(@"UPDATE workflow_definitions SET ""Environment"" = lower(""Environment"");");

            // Seed one environment row per distinct (tenant, environment) found in the live-config tables
            // that carry the foreign key, so those keys are satisfied for all existing data. Historical
            // and transient tables (executions, work items, manual runs, heartbeats) are deliberately
            // excluded: an old typo environment that no live configuration uses should not be resurrected
            // into the registry.
            migrationBuilder.Sql(@"
                INSERT INTO environments (""Id"", ""TenantId"", ""Name"", ""DisplayName"", ""Description"", ""SortOrder"", ""IsDefault"", ""CreatedAt"", ""UpdatedAt"")
                SELECT gen_random_uuid(), src.""TenantId"", src.""Environment"", initcap(src.""Environment""), NULL, 0, false, now(), now()
                FROM (
                    SELECT DISTINCT ""TenantId"", ""Environment"" FROM secrets WHERE ""Environment"" <> ''
                    UNION SELECT DISTINCT ""TenantId"", ""Environment"" FROM integrations WHERE ""Environment"" <> ''
                    UNION SELECT DISTINCT ""TenantId"", ""Environment"" FROM agent_tokens WHERE ""Environment"" <> ''
                    UNION SELECT DISTINCT ""TenantId"", ""Environment"" FROM workflow_definitions WHERE ""Environment"" <> ''
                ) src;");

            // Every tenant gets a default 'production' environment even if it had no scoped rows yet,
            // matching how new tenants are seeded going forward.
            migrationBuilder.Sql(@"
                INSERT INTO environments (""Id"", ""TenantId"", ""Name"", ""DisplayName"", ""Description"", ""SortOrder"", ""IsDefault"", ""CreatedAt"", ""UpdatedAt"")
                SELECT gen_random_uuid(), t.""Id"", 'production', 'Production', NULL, 0, true, now(), now()
                FROM tenants t
                WHERE NOT EXISTS (
                    SELECT 1 FROM environments e WHERE e.""TenantId"" = t.""Id"" AND e.""Name"" = 'production'
                );");

            // Mark 'production' as each tenant's default (covers tenants whose production row came from the
            // distinct-value seed above with IsDefault = false).
            migrationBuilder.Sql(@"UPDATE environments SET ""IsDefault"" = true WHERE ""Name"" = 'production';");

            migrationBuilder.AddForeignKey(
                name: "FK_agent_tokens_environments_TenantId_Environment",
                table: "agent_tokens",
                columns: new[] { "TenantId", "Environment" },
                principalTable: "environments",
                principalColumns: new[] { "TenantId", "Name" });

            migrationBuilder.AddForeignKey(
                name: "FK_integrations_environments_TenantId_Environment",
                table: "integrations",
                columns: new[] { "TenantId", "Environment" },
                principalTable: "environments",
                principalColumns: new[] { "TenantId", "Name" });

            migrationBuilder.AddForeignKey(
                name: "FK_secrets_environments_TenantId_Environment",
                table: "secrets",
                columns: new[] { "TenantId", "Environment" },
                principalTable: "environments",
                principalColumns: new[] { "TenantId", "Name" });

            migrationBuilder.AddForeignKey(
                name: "FK_workflow_definitions_environments_TenantId_Environment",
                table: "workflow_definitions",
                columns: new[] { "TenantId", "Environment" },
                principalTable: "environments",
                principalColumns: new[] { "TenantId", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_agent_tokens_environments_TenantId_Environment",
                table: "agent_tokens");

            migrationBuilder.DropForeignKey(
                name: "FK_integrations_environments_TenantId_Environment",
                table: "integrations");

            migrationBuilder.DropForeignKey(
                name: "FK_secrets_environments_TenantId_Environment",
                table: "secrets");

            migrationBuilder.DropForeignKey(
                name: "FK_workflow_definitions_environments_TenantId_Environment",
                table: "workflow_definitions");

            migrationBuilder.DropTable(
                name: "environments");

            migrationBuilder.DropIndex(
                name: "IX_workflow_definitions_TenantId_Environment",
                table: "workflow_definitions");

            migrationBuilder.DropIndex(
                name: "IX_integrations_TenantId_Environment",
                table: "integrations");

            migrationBuilder.DropIndex(
                name: "IX_agent_tokens_TenantId_Environment",
                table: "agent_tokens");

            migrationBuilder.CreateIndex(
                name: "IX_agent_tokens_TenantId",
                table: "agent_tokens",
                column: "TenantId");
        }
    }
}
