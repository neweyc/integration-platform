using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlPlane.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSecretReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Reference",
                table: "secrets",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Reference",
                table: "secrets");
        }
    }
}
