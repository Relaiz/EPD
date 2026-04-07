using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using TeacherScheduleApp.Data;
using TeacherScheduleApp.Helpers;
using TeacherScheduleApp.Models;

namespace TeacherScheduleApp.Services
{
    public static class SettingsService
    {
        public static DaySettings? GetDaySettingsForDate(DateTime date, int employeeId = 1)
        {
            using var db = new AppDbContext();

            return db.DaySettings
                .AsNoTracking()
                .FirstOrDefault(x => x.EmployeeId == employeeId && x.Date.Date == date.Date);
        }

        public static DaySettings? GetManualDaySettingsForDate(DateTime day, int employeeId = 1)
        {
            using var db = new AppDbContext();

            return db.DaySettings
                .AsNoTracking()
                .FirstOrDefault(x =>
                    x.EmployeeId == employeeId &&
                    x.Date.Date == day.Date &&
                    x.IsManualOverride);
        }

        public static void DeleteDaySettingsForDate(DateTime date, int employeeId = 1)
        {
            using var db = new AppDbContext();

            var existing = db.DaySettings
                .FirstOrDefault(x => x.EmployeeId == employeeId && x.Date.Date == date.Date);

            if (existing == null)
                return;

            db.DaySettings.Remove(existing);
            db.SaveChanges();
        }

        public static void DeleteComputedDaySettingsForDate(DateTime day, int employeeId = 1)
        {
            using var db = new AppDbContext();

            var row = db.DaySettings.FirstOrDefault(x =>
                x.EmployeeId == employeeId &&
                x.Date.Date == day.Date &&
                !x.IsManualOverride);

            if (row == null)
                return;

            db.DaySettings.Remove(row);
            db.SaveChanges();
        }

        public static async Task DeleteComputedDaySettingsInRangeAsync(
           DateTime from,
           DateTime to,
           int employeeId = 1)
        {
            await using var db = new AppDbContext();

            var rows = await db.DaySettings
                .Where(x =>
                    x.EmployeeId == employeeId &&
                    x.Date.Date >= from.Date &&
                    x.Date.Date <= to.Date &&
                    !x.IsManualOverride)
                .ToListAsync();

            if (rows.Count == 0)
                return;

            db.DaySettings.RemoveRange(rows);
            await db.SaveChangesAsync();
        }

        public static void SaveDaySettingsForDate(
            DateTime day,
            TimeSpan arrival,
            TimeSpan departure,
            TimeSpan lunchStart,
            TimeSpan lunchEnd,
            int employeeId = 1,
            bool isManualOverride = false)
        {
            using var db = new AppDbContext();

            var existing = db.DaySettings
                .FirstOrDefault(x =>
                    x.EmployeeId == employeeId &&
                    x.Date.Date == day.Date);

            if (existing == null)
            {
                db.DaySettings.Add(new DaySettings
                {
                    EmployeeId = employeeId,
                    Date = day.Date,
                    ArrivalTime = arrival,
                    DepartureTime = departure,
                    LunchStart = lunchStart,
                    LunchEnd = lunchEnd,
                    IsManualOverride = isManualOverride
                });

                db.SaveChanges();
                return;
            }

            if (existing.IsManualOverride && !isManualOverride)
                return;

            existing.ArrivalTime = arrival;
            existing.DepartureTime = departure;
            existing.LunchStart = lunchStart;
            existing.LunchEnd = lunchEnd;
            existing.IsManualOverride = isManualOverride || existing.IsManualOverride;

            db.SaveChanges();
        }

        public static ResolvedDaySettings GetResolvedDaySettings(DateTime date, int employeeId = 1)
        {
            using var db = new AppDbContext();

            var semester = GlobalSettingsService.GetSemesterForDate(date);

            var semesterSettings = db.SemesterSettings
                .AsNoTracking()
                .FirstOrDefault(x =>
                    x.EmployeeId == employeeId &&
                    x.Year == date.Year &&
                    x.Semester == semester);

            var dayOverride = db.DaySettings
                .AsNoTracking()
                .FirstOrDefault(x => x.EmployeeId == employeeId && x.Date.Date == date.Date);

            var baseSettings = semesterSettings
                ?? GlobalSettingsService.GetDefaultSettings(date.Year, semester, employeeId);

            bool isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            bool isHoliday = HolidayHelper.IsCzechHoliday(date);

            if (isWeekend || isHoliday)
            {
                return new ResolvedDaySettings
                {
                    EmployeeId = employeeId,
                    Date = date.Date,
                    Year = date.Year,
                    Semester = semester,

                    ArrivalTime = dayOverride?.ArrivalTime ?? TimeSpan.Zero,
                    DepartureTime = dayOverride?.DepartureTime ?? TimeSpan.Zero,
                    LunchStart = dayOverride?.LunchStart ?? TimeSpan.Zero,
                    LunchEnd = dayOverride?.LunchEnd ?? TimeSpan.Zero,

                    AutoEventNamePreLunch = baseSettings.AutoEventNamePreLunch,
                    AutoEventNameLunch = baseSettings.AutoEventNameLunch,
                    AutoEventNamePostLunch = baseSettings.AutoEventNamePostLunch,
                    MinBreakDuration = baseSettings.MinBreakDuration,
                    MaxBreakDuration = baseSettings.MaxBreakDuration
                };
            }

            var weekdayNumber = MapDayOfWeek(date.DayOfWeek);

            WeekdaySettings? weekdaySettings;

            if (semesterSettings == null)
            {
                weekdaySettings = baseSettings.WeekdaySettings
                    .FirstOrDefault(x => x.DayOfWeek == weekdayNumber);
            }
            else
            {
                weekdaySettings = db.WeekdaySettings
                    .AsNoTracking()
                    .FirstOrDefault(x =>
                        x.SemesterSettingsId == semesterSettings.Id &&
                        x.DayOfWeek == weekdayNumber);
            }

            if (weekdaySettings == null)
                throw new InvalidOperationException(
                    $"WeekdaySettings not found for {date:yyyy-MM-dd}, employeeId={employeeId}");

            return new ResolvedDaySettings
            {
                EmployeeId = employeeId,
                Date = date.Date,
                Year = date.Year,
                Semester = semester,

                ArrivalTime = dayOverride?.ArrivalTime ?? weekdaySettings.ArrivalTime,
                DepartureTime = dayOverride?.DepartureTime ?? weekdaySettings.DepartureTime,
                LunchStart = dayOverride?.LunchStart ?? weekdaySettings.LunchStart,
                LunchEnd = dayOverride?.LunchEnd ?? weekdaySettings.LunchEnd,

                AutoEventNamePreLunch = baseSettings.AutoEventNamePreLunch,
                AutoEventNameLunch = baseSettings.AutoEventNameLunch,
                AutoEventNamePostLunch = baseSettings.AutoEventNamePostLunch,
                MinBreakDuration = baseSettings.MinBreakDuration,
                MaxBreakDuration = baseSettings.MaxBreakDuration
            };
        }

        public static int MapDayOfWeek(DayOfWeek dayOfWeek)
        {
            return dayOfWeek switch
            {
                DayOfWeek.Monday => 1,
                DayOfWeek.Tuesday => 2,
                DayOfWeek.Wednesday => 3,
                DayOfWeek.Thursday => 4,
                DayOfWeek.Friday => 5,
                _ => throw new InvalidOperationException("Weekend does not have weekday settings.")
            };
        }
    }
}