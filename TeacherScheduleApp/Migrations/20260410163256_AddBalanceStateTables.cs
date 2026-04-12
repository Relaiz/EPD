using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeacherScheduleApp.Migrations
{
    /// <inheritdoc />
    public partial class AddBalanceStateTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BalanceSelfTrims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EmployeeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Day = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Edge = table.Column<int>(type: "INTEGER", nullable: false),
                    Minutes = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BalanceSelfTrims", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BalanceTransfers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EmployeeId = table.Column<int>(type: "INTEGER", nullable: false),
                    FromDay = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ToDay = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Edge = table.Column<int>(type: "INTEGER", nullable: false),
                    Minutes = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BalanceTransfers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BalanceSelfTrims_EmployeeId_Day_Edge",
                table: "BalanceSelfTrims",
                columns: new[] { "EmployeeId", "Day", "Edge" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BalanceTransfers_EmployeeId_FromDay_ToDay_Edge",
                table: "BalanceTransfers",
                columns: new[] { "EmployeeId", "FromDay", "ToDay", "Edge" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BalanceSelfTrims");

            migrationBuilder.DropTable(
                name: "BalanceTransfers");
        }
    }
}
