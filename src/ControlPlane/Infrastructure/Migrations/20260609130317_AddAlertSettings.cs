using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlPlane.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "integration_alert_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IntegrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EmailEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    EmailRecipients = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    WebhookEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    WebhookUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    WebhookEncryptedSecret = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_alert_settings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_integration_alert_settings_integrations_IntegrationId",
                        column: x => x.IntegrationId,
                        principalTable: "integrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_integration_alert_settings_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tenant_alert_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmailEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    EmailRecipients = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SmtpHost = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    SmtpPort = table.Column<int>(type: "integer", nullable: false, defaultValue: 587),
                    SmtpUseStartTls = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    SmtpUsername = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    SmtpEncryptedPassword = table.Column<string>(type: "text", nullable: true),
                    SmtpFromAddress = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    SmtpFromName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    WebhookEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    WebhookUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    WebhookEncryptedSecret = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_alert_settings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tenant_alert_settings_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_integration_alert_settings_IntegrationId",
                table: "integration_alert_settings",
                column: "IntegrationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_integration_alert_settings_TenantId",
                table: "integration_alert_settings",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_alert_settings_TenantId",
                table: "tenant_alert_settings",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "integration_alert_settings");

            migrationBuilder.DropTable(
                name: "tenant_alert_settings");
        }
    }
}
