using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeacherScheduleApp.Migrations
{
    /// <inheritdoc />
    public partial class InitialNewSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FullName = table.Column<string>(type: "TEXT", nullable: false),
                    Department = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportBatches",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: true),
                    ImportedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DaySettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EmployeeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<string>(type: "TEXT", nullable: false),
                    ArrivalTime = table.Column<string>(type: "TEXT", nullable: false),
                    DepartureTime = table.Column<string>(type: "TEXT", nullable: false),
                    LunchStart = table.Column<string>(type: "TEXT", nullable: false),
                    LunchEnd = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DaySettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DaySettings_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SemesterSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EmployeeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Semester = table.Column<int>(type: "INTEGER", nullable: false),
                    GlobalStartTime = table.Column<string>(type: "TEXT", nullable: false),
                    GlobalEndTime = table.Column<string>(type: "TEXT", nullable: false),
                    MinBreakDuration = table.Column<string>(type: "TEXT", nullable: false),
                    MaxBreakDuration = table.Column<string>(type: "TEXT", nullable: false),
                    AutoEventNamePreLunch = table.Column<string>(type: "TEXT", nullable: false),
                    AutoEventNameLunch = table.Column<string>(type: "TEXT", nullable: false),
                    AutoEventNamePostLunch = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SemesterSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SemesterSettings_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EmployeeId = table.Column<int>(type: "INTEGER", nullable: false),
                    ParentEventId = table.Column<int>(type: "INTEGER", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    StartTime = table.Column<string>(type: "TEXT", nullable: false),
                    EndTime = table.Column<string>(type: "TEXT", nullable: false),
                    AllDay = table.Column<bool>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    EventType = table.Column<int>(type: "INTEGER", nullable: false),
                    IsAutoGenerated = table.Column<bool>(type: "INTEGER", nullable: false),
                    HasCollision = table.Column<bool>(type: "INTEGER", nullable: false),
                    AutoGeneratedForDate = table.Column<string>(type: "TEXT", nullable: true),
                    ImportBatchId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Events_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Events_Events_ParentEventId",
                        column: x => x.ParentEventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Events_ImportBatches_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "WeekdaySettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SemesterSettingsId = table.Column<int>(type: "INTEGER", nullable: false),
                    DayOfWeek = table.Column<int>(type: "INTEGER", nullable: false),
                    ArrivalTime = table.Column<string>(type: "TEXT", nullable: false),
                    DepartureTime = table.Column<string>(type: "TEXT", nullable: false),
                    LunchStart = table.Column<string>(type: "TEXT", nullable: false),
                    LunchEnd = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeekdaySettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WeekdaySettings_SemesterSettings_SemesterSettingsId",
                        column: x => x.SemesterSettingsId,
                        principalTable: "SemesterSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DaySettings_EmployeeId_Date",
                table: "DaySettings",
                columns: new[] { "EmployeeId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_AutoGeneratedForDate",
                table: "Events",
                column: "AutoGeneratedForDate");

            migrationBuilder.CreateIndex(
                name: "IX_Events_EmployeeId",
                table: "Events",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Events_ImportBatchId",
                table: "Events",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Events_ParentEventId",
                table: "Events",
                column: "ParentEventId");

            migrationBuilder.CreateIndex(
                name: "IX_Events_StartTime",
                table: "Events",
                column: "StartTime");

            migrationBuilder.CreateIndex(
                name: "IX_SemesterSettings_EmployeeId_Year_Semester",
                table: "SemesterSettings",
                columns: new[] { "EmployeeId", "Year", "Semester" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WeekdaySettings_SemesterSettingsId_DayOfWeek",
                table: "WeekdaySettings",
                columns: new[] { "SemesterSettingsId", "DayOfWeek" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DaySettings");

            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "WeekdaySettings");

            migrationBuilder.DropTable(
                name: "ImportBatches");

            migrationBuilder.DropTable(
                name: "SemesterSettings");

            migrationBuilder.DropTable(
                name: "Employees");
        }
    }
}
