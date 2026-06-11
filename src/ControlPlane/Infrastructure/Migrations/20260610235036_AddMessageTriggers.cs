using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlPlane.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MessageId",
                table: "work_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeclaredSubject",
                table: "integration_triggers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Subject",
                table: "integration_triggers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Environment = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: true),
                    SourceExecutionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_messages_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_work_items_MessageId",
                table: "work_items",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_integration_triggers_TenantId_Subject",
                table: "integration_triggers",
                columns: new[] { "TenantId", "Subject" });

            migrationBuilder.CreateIndex(
                name: "IX_messages_TenantId_Environment_Subject_PublishedAt",
                table: "messages",
                columns: new[] { "TenantId", "Environment", "Subject", "PublishedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_work_items_messages_MessageId",
                table: "work_items",
                column: "MessageId",
                principalTable: "messages",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_work_items_messages_MessageId",
                table: "work_items");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropIndex(
                name: "IX_work_items_MessageId",
                table: "work_items");

            migrationBuilder.DropIndex(
                name: "IX_integration_triggers_TenantId_Subject",
                table: "integration_triggers");

            migrationBuilder.DropColumn(
                name: "MessageId",
                table: "work_items");

            migrationBuilder.DropColumn(
                name: "DeclaredSubject",
                table: "integration_triggers");

            migrationBuilder.DropColumn(
                name: "Subject",
                table: "integration_triggers");
        }
    }
}
