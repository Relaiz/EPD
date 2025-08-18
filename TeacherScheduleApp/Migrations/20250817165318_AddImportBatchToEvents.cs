using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeacherScheduleApp.Migrations
{
    /// <inheritdoc />
    public partial class AddImportBatchToEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImportBatchId",
                table: "Events",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImportLabel",
                table: "Events",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImportBatchId",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "ImportLabel",
                table: "Events");
        }
    }
}
