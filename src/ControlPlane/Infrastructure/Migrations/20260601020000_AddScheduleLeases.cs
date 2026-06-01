using System;
using ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlPlane.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260601020000_AddScheduleLeases")]
    public partial class AddScheduleLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
    }
}
