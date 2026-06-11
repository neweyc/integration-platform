using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlPlane.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationAndPackageRuntime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Runtime",
                table: "integrations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "dotnet");

            migrationBuilder.AddColumn<string>(
                name: "Runtime",
                table: "assembly_packages",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "dotnet");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Runtime",
                table: "integrations");

            migrationBuilder.DropColumn(
                name: "Runtime",
                table: "assembly_packages");
        }
    }
}
