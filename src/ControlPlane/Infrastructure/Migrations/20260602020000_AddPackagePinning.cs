using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlPlane.Infrastructure.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(AppDbContext))]
    [Migration("20260602020000_AddPackagePinning")]
    public partial class AddPackagePinning : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Pinned package on integrations (null = dev-mode local path fallback)
            migrationBuilder.AddColumn<Guid>(
                name: "PackageId",
                table: "integrations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_integrations_PackageId",
                table: "integrations",
                column: "PackageId");

            migrationBuilder.AddForeignKey(
                name: "FK_integrations_assembly_packages_PackageId",
                table: "integrations",
                column: "PackageId",
                principalTable: "assembly_packages",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Package version snapshot on execution records
            migrationBuilder.AddColumn<Guid>(
                name: "PackageId",
                table: "execution_records",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PackageName",
                table: "execution_records",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PackageVersion",
                table: "execution_records",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_integrations_assembly_packages_PackageId",
                table: "integrations");

            migrationBuilder.DropIndex(
                name: "IX_integrations_PackageId",
                table: "integrations");

            migrationBuilder.DropColumn(name: "PackageId", table: "integrations");
            migrationBuilder.DropColumn(name: "PackageId", table: "execution_records");
            migrationBuilder.DropColumn(name: "PackageName", table: "execution_records");
            migrationBuilder.DropColumn(name: "PackageVersion", table: "execution_records");
        }
    }
}
