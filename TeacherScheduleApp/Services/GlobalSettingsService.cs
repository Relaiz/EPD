using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TeacherScheduleApp.Data;
using TeacherScheduleApp.Models;

namespace TeacherScheduleApp.Services
{
    public static class GlobalSettingsService
    {
        public static async Task SaveSemesterSettingsAsync(
            int year,
            SemesterType semester,
            SemesterSettings settings,
            int employeeId = 1)
        {
            using var db = new AppDbContext();

            var existing = await db.SemesterSettings
                .Include(x => x.WeekdaySettings)
                .FirstOrDefaultAsync(x =>
                    x.EmployeeId == employeeId &&
                    x.Year == year &&
                    x.Semester == semester);

            if (existing == null)
            {
                existing = new SemesterSettings
                {
                    EmployeeId = employeeId,
                    Year = year,
                    Semester = semester
                };

                db.SemesterSettings.Add(existing);
                await db.SaveChangesAsync();
            }

            existing.GlobalStartTime = settings.GlobalStartTime;
            existing.GlobalEndTime = settings.GlobalEndTime;
            existing.MinBreakDuration = settings.MinBreakDuration;
            existing.MaxBreakDuration = settings.MaxBreakDuration;
            existing.AutoEventNamePreLunch = settings.AutoEventNamePreLunch;
            existing.AutoEventNameLunch = settings.AutoEventNameLunch;
            existing.AutoEventNamePostLunch = settings.AutoEventNamePostLunch;

            await db.SaveChangesAsync();

            var currentWeekdays = await db.WeekdaySettings
                .Where(x => x.SemesterSettingsId == existing.Id)
                .ToListAsync();

            db.WeekdaySettings.RemoveRange(currentWeekdays);
            await db.SaveChangesAsync();

            foreach (var wd in settings.WeekdaySettings.OrderBy(x => x.DayOfWeek))
            {
                db.WeekdaySettings.Add(new WeekdaySettings
                {
                    SemesterSettingsId = existing.Id,
                    DayOfWeek = wd.DayOfWeek,
                    ArrivalTime = wd.ArrivalTime,
                    DepartureTime = wd.DepartureTime,
                    LunchStart = wd.LunchStart,
                    LunchEnd = wd.LunchEnd
                });
            }

            await db.SaveChangesAsync();
        }

        public static SemesterSettings? LoadSemesterSettings(int year, SemesterType semester, int employeeId = 1)
        {
            using var db = new AppDbContext();

            return db.SemesterSettings
                .AsNoTracking()
                .Include(x => x.WeekdaySettings)
                .FirstOrDefault(x =>
                    x.EmployeeId == employeeId &&
                    x.Year == year &&
                    x.Semester == semester);
        }

        public static SemesterType GetSemesterForDate(DateTime date)
        {
            if ((date.Month >= 9) || (date.Month == 2 && date.Day <= 9))
                return SemesterType.Winter;

            if (date.Month >= 2 && date.Month <= 8)
                return SemesterType.Summer;

            return SemesterType.Winter;
        }

        public static List<int> GetYearsWithData(int employeeId = 1)
        {
            using var db = new AppDbContext();

            return db.SemesterSettings
                .AsNoTracking()
                .Where(x => x.EmployeeId == employeeId)
                .Select(x => x.Year)
                .Concat(
                    db.Events.AsNoTracking()
                        .Where(e => !e.IsDeleted && e.EmployeeId == employeeId)
                        .Select(e => e.StartTime.Year))
                .Concat(
                    db.Events.AsNoTracking()
                        .Where(e => !e.IsDeleted && e.EmployeeId == employeeId)
                        .Select(e => e.EndTime.Year))
                .Concat(
                    db.DaySettings.AsNoTracking()
                        .Where(x => x.EmployeeId == employeeId)
                        .Select(x => x.Date.Year))
                .Distinct()
                .OrderBy(x => x)
                .ToList();
        }

        public static Employee EnsureDefaultEmployee(int employeeId = 1)
        {
            using var db = new AppDbContext();

            var employee = db.Employees.FirstOrDefault(x => x.Id == employeeId);
            if (employee != null)
                return employee;

            employee = new Employee
            {
                Id = employeeId,
                FullName = "Radek Matoušek",
                Department = "Katedra informačních technologií"
            };

            db.Employees.Add(employee);
            db.SaveChanges();

            return employee;
        }
        public static async Task SaveEmployeeInfoAsync(int employeeId, string fullName, string department)
        {
            using var db = new AppDbContext();

            var employee = await db.Employees.FirstOrDefaultAsync(x => x.Id == employeeId);
            if (employee == null)
            {
                employee = new Employee
                {
                    Id = employeeId,
                    FullName = fullName,
                    Department = department
                };
                db.Employees.Add(employee);
            }
            else
            {
                employee.FullName = fullName;
                employee.Department = department;
            }

            await db.SaveChangesAsync();
        }

        public static SemesterSettings GetDefaultSettings(int year, SemesterType semester, int employeeId = 1)
        {
            EnsureDefaultEmployee(employeeId);

            if (semester == SemesterType.Winter)
            {
                return new SemesterSettings
                {
                    EmployeeId = employeeId,
                    Year = year,
                    Semester = SemesterType.Winter,
                    GlobalStartTime = "08:00",
                    GlobalEndTime = "16:30",
                    MinBreakDuration = "00:15",
                    MaxBreakDuration = "01:00",
                    AutoEventNamePreLunch = "Dopolední pracovní doba",
                    AutoEventNameLunch = "Oběd",
                    AutoEventNamePostLunch = "Odpolední pracovní doba",
                    WeekdaySettings = new List<WeekdaySettings>
                    {
                        new() { DayOfWeek = 1, ArrivalTime = TimeSpan.Parse("08:00"), DepartureTime = TimeSpan.Parse("16:30"), LunchStart = TimeSpan.Parse("12:00"), LunchEnd = TimeSpan.Parse("12:30") },
                        new() { DayOfWeek = 2, ArrivalTime = TimeSpan.Parse("08:00"), DepartureTime = TimeSpan.Parse("16:30"), LunchStart = TimeSpan.Parse("12:00"), LunchEnd = TimeSpan.Parse("12:30") },
                        new() { DayOfWeek = 3, ArrivalTime = TimeSpan.Parse("08:00"), DepartureTime = TimeSpan.Parse("16:30"), LunchStart = TimeSpan.Parse("12:00"), LunchEnd = TimeSpan.Parse("12:30") },
                        new() { DayOfWeek = 4, ArrivalTime = TimeSpan.Parse("08:00"), DepartureTime = TimeSpan.Parse("16:30"), LunchStart = TimeSpan.Parse("12:00"), LunchEnd = TimeSpan.Parse("12:30") },
                        new() { DayOfWeek = 5, ArrivalTime = TimeSpan.Parse("08:00"), DepartureTime = TimeSpan.Parse("16:30"), LunchStart = TimeSpan.Parse("12:00"), LunchEnd = TimeSpan.Parse("12:30") }
                    }
                };
            }

            return new SemesterSettings
            {
                EmployeeId = employeeId,
                Year = year,
                Semester = SemesterType.Summer,
                GlobalStartTime = "08:30",
                GlobalEndTime = "17:00",
                MinBreakDuration = "00:15",
                MaxBreakDuration = "01:00",
                AutoEventNamePreLunch = "Dopolední pracovní doba",
                AutoEventNameLunch = "Oběd",
                AutoEventNamePostLunch = "Odpolední pracovní doba",
                WeekdaySettings = new List<WeekdaySettings>
                {
                    new() { DayOfWeek = 1, ArrivalTime = TimeSpan.Parse("08:30"), DepartureTime = TimeSpan.Parse("17:00"), LunchStart = TimeSpan.Parse("12:30"), LunchEnd = TimeSpan.Parse("13:00") },
                    new() { DayOfWeek = 2, ArrivalTime = TimeSpan.Parse("08:30"), DepartureTime = TimeSpan.Parse("17:00"), LunchStart = TimeSpan.Parse("12:30"), LunchEnd = TimeSpan.Parse("13:00") },
                    new() { DayOfWeek = 3, ArrivalTime = TimeSpan.Parse("08:30"), DepartureTime = TimeSpan.Parse("17:00"), LunchStart = TimeSpan.Parse("12:30"), LunchEnd = TimeSpan.Parse("13:00") },
                    new() { DayOfWeek = 4, ArrivalTime = TimeSpan.Parse("08:30"), DepartureTime = TimeSpan.Parse("17:00"), LunchStart = TimeSpan.Parse("12:30"), LunchEnd = TimeSpan.Parse("13:00") },
                    new() { DayOfWeek = 5, ArrivalTime = TimeSpan.Parse("08:30"), DepartureTime = TimeSpan.Parse("17:00"), LunchStart = TimeSpan.Parse("12:30"), LunchEnd = TimeSpan.Parse("13:00") }
                }
            };
        }

        public static async Task EnsureDefaultSemesterSettingsAsync(int year, SemesterType semester, int employeeId = 1)
        {
            using var db = new AppDbContext();

            var exists = await db.SemesterSettings
                .AnyAsync(x => x.EmployeeId == employeeId && x.Year == year && x.Semester == semester);

            if (exists)
                return;

            var defaults = GetDefaultSettings(year, semester, employeeId);
            await SaveSemesterSettingsAsync(year, semester, defaults, employeeId);
        }
    }
}