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
            var day = date.Date;

            using var db = new AppDbContext();

            return db.DaySettings
                .AsNoTracking()
                .FirstOrDefault(x => x.EmployeeId == employeeId && x.Date == day);
        }

        public static DaySettings? GetManualDaySettingsForDate(DateTime day, int employeeId = 1)
        {
            var d = day.Date;

            using var db = new AppDbContext();

            return db.DaySettings
                .AsNoTracking()
                .FirstOrDefault(x =>
                    x.EmployeeId == employeeId &&
                    x.Date == d &&
                    x.IsManualOverride);
        }

        public static void DeleteDaySettingsForDate(DateTime date, int employeeId = 1)
        {
            var day = date.Date;

            using var db = new AppDbContext();

            var existing = db.DaySettings
                .FirstOrDefault(x => x.EmployeeId == employeeId && x.Date == day);

            if (existing == null)
                return;

            db.DaySettings.Remove(existing);
            db.SaveChanges();
        }

        public static void DeleteComputedDaySettingsForDate(DateTime day, int employeeId = 1)
        {
            var d = day.Date;

            using var db = new AppDbContext();

            var row = db.DaySettings.FirstOrDefault(x =>
                x.EmployeeId == employeeId &&
                x.Date == d &&
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
            var start = from.Date;
            var endExclusive = to.Date.AddDays(1);

            await using var db = new AppDbContext();

#if NET7_0_OR_GREATER
            await db.DaySettings
                .Where(x =>
                    x.EmployeeId == employeeId &&
                    x.Date >= start &&
                    x.Date < endExclusive &&
                    !x.IsManualOverride)
                .ExecuteDeleteAsync();
#else
            var rows = await db.DaySettings
                .Where(x =>
                    x.EmployeeId == employeeId &&
                    x.Date >= start &&
                    x.Date < endExclusive &&
                    !x.IsManualOverride)
                .ToListAsync();

            if (rows.Count == 0)
                return;

            db.DaySettings.RemoveRange(rows);
            await db.SaveChangesAsync();
#endif
        }

        public static void SaveDaySettingsForDate(
            DateTime day,
            TimeSpan arrival,
            TimeSpan departure,
            TimeSpan lunchStart,
            TimeSpan lunchEnd,
            int employeeId = 1,
            bool isManualOverride = false,
            bool forceOverwriteExistingManual = false)
        {
            var d = day.Date;

            using var db = new AppDbContext();

            var existing = db.DaySettings
                .FirstOrDefault(x =>
                    x.EmployeeId == employeeId &&
                    x.Date == d);

            if (existing == null)
            {
                db.DaySettings.Add(new DaySettings
                {
                    EmployeeId = employeeId,
                    Date = d,
                    ArrivalTime = arrival,
                    DepartureTime = departure,
                    LunchStart = lunchStart,
                    LunchEnd = lunchEnd,
                    IsManualOverride = isManualOverride
                });

                db.SaveChanges();
                return;
            }

            if (existing.IsManualOverride && !isManualOverride && !forceOverwriteExistingManual)
                return;

            bool newManualFlag = forceOverwriteExistingManual
                ? isManualOverride
                : (isManualOverride || existing.IsManualOverride);

            if (existing.ArrivalTime == arrival &&
                existing.DepartureTime == departure &&
                existing.LunchStart == lunchStart &&
                existing.LunchEnd == lunchEnd &&
                existing.IsManualOverride == newManualFlag)
            {
                return;
            }

            existing.ArrivalTime = arrival;
            existing.DepartureTime = departure;
            existing.LunchStart = lunchStart;
            existing.LunchEnd = lunchEnd;
            existing.IsManualOverride = newManualFlag;

            db.SaveChanges();
        }

        public static ResolvedDaySettings GetResolvedDaySettings(DateTime date, int employeeId = 1)
            => ResolveDaySettingsCore(date, employeeId, ignoreComputed: false);

        public static ResolvedDaySettings GetResolvedDaySettingsIgnoringComputed(DateTime date, int employeeId = 1)
            => ResolveDaySettingsCore(date, employeeId, ignoreComputed: true);

        private static ResolvedDaySettings ResolveDaySettingsCore(DateTime date, int employeeId, bool ignoreComputed)
        {
            var day = date.Date;

            using var db = new AppDbContext();

            var semester = GlobalSettingsService.GetSemesterForDate(day);

            var semesterSettings = db.SemesterSettings
                .AsNoTracking()
                .Include(x => x.WeekdaySettings)
                .FirstOrDefault(x =>
                    x.EmployeeId == employeeId &&
                    x.Year == day.Year &&
                    x.Semester == semester);

            var dayOverride = db.DaySettings
                .AsNoTracking()
                .FirstOrDefault(x =>
                    x.EmployeeId == employeeId &&
                    x.Date == day &&
                    (!ignoreComputed || x.IsManualOverride));

            var baseSettings = semesterSettings
                ?? GlobalSettingsService.GetDefaultSettings(day.Year, semester, employeeId);

            bool isWeekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            bool isHoliday = HolidayHelper.IsCzechHoliday(day);

            if (isWeekend || isHoliday)
            {
                return new ResolvedDaySettings
                {
                    EmployeeId = employeeId,
                    Date = day,
                    Year = day.Year,
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

            var weekdayNumber = MapDayOfWeek(day.DayOfWeek);

            var weekdaySettings = baseSettings.WeekdaySettings
                .FirstOrDefault(x => x.DayOfWeek == weekdayNumber);

            if (weekdaySettings == null)
                throw new InvalidOperationException(
                    $"WeekdaySettings not found for {day:yyyy-MM-dd}, employeeId={employeeId}");

            return new ResolvedDaySettings
            {
                EmployeeId = employeeId,
                Date = day,
                Year = day.Year,
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