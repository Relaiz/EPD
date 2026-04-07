using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeacherScheduleApp.Migrations
{
    /// <inheritdoc />
    public partial class AddDaySettingsIsManualOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsManualOverride",
                table: "DaySettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsManualOverride",
                table: "DaySettings");
        }
    }
}
