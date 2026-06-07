using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlPlane.Infrastructure.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(AppDbContext))]
    [Migration("20260607010000_AddTriggerDeclaredDefaults")]
    public partial class AddTriggerDeclaredDefaults : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeclaredCronExpression",
                table: "integration_triggers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // Default true is the universal code default for Enabled, so an existing operator-disabled
            // trigger reads as a preserved override rather than being re-enabled on the next redeploy.
            migrationBuilder.AddColumn<bool>(
                name: "DeclaredEnabled",
                table: "integration_triggers",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            // Seed the declared cron from the current active cron so existing scheduled triggers do not
            // start out reporting spurious drift. A genuine pre-existing cron override (rare on this young
            // schema) is adopted as the baseline rather than lost.
            migrationBuilder.Sql(
                "UPDATE integration_triggers SET \"DeclaredCronExpression\" = \"CronExpression\";");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeclaredCronExpression",
                table: "integration_triggers");

            migrationBuilder.DropColumn(
                name: "DeclaredEnabled",
                table: "integration_triggers");
        }
    }
}
