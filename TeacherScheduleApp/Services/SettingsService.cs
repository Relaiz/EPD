using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using TeacherScheduleApp.Data;
using TeacherScheduleApp.Helpers;
using TeacherScheduleApp.Models;
using System.Collections.Generic;

namespace TeacherScheduleApp.Services
{
    public static class SettingsService
    {
        private static readonly System.Threading.AsyncLocal<SettingsReadCacheScope?> _settingsReadCacheScope = new();

        private sealed class SettingsReadCacheScope : IDisposable
        {
            public SettingsReadCacheScope? Previous { get; }
            public int EmployeeId { get; }

            private readonly HashSet<DateTime> _days;
            private readonly Dictionary<DateTime, DaySettings> _daySettingsByDate = new();
            private readonly Dictionary<DateTime, DaySettings> _manualDaySettingsByDate = new();
            private readonly HashSet<DateTime> _loadedDaySettings = new();
            private readonly HashSet<DateTime> _loadedManualDaySettings = new();
            private readonly Dictionary<(int Year, SemesterType Semester), SemesterSettings?> _semesterSettingsByKey = new();

            public SettingsReadCacheScope(SettingsReadCacheScope? previous, int employeeId, IEnumerable<DateTime> days)
            {
                Previous = previous;
                EmployeeId = employeeId;
                _days = days
                    .Select(d => d.Date)
                    .Distinct()
                    .ToHashSet();

                if (_days.Count > 0)
                    Preload();

                _settingsReadCacheScope.Value = this;
            }

            private void Preload()
            {
                var orderedDays = _days.OrderBy(d => d).ToList();
                var start = orderedDays[0];
                var endExclusive = orderedDays[^1].AddDays(1);

                var semesterKeys = _days
                    .Select(d => (Year: d.Year, Semester: GlobalSettingsService.GetSemesterForDate(d)))
                    .Distinct()
                    .ToList();

                using var db = new AppDbContext();

                var daySettings = db.DaySettings
                    .AsNoTracking()
                    .Where(x => x.EmployeeId == EmployeeId &&
                                x.Date >= start &&
                                x.Date < endExclusive)
                    .ToList();

                foreach (var day in _days)
                {
                    _loadedDaySettings.Add(day);
                    _loadedManualDaySettings.Add(day);
                }

                foreach (var row in daySettings.Where(x => _days.Contains(x.Date)))
                {
                    _daySettingsByDate[row.Date] = row;

                    if (row.IsManualOverride)
                        _manualDaySettingsByDate[row.Date] = row;
                }

                var years = semesterKeys
                    .Select(x => x.Year)
                    .Distinct()
                    .ToList();

                var semesterSettings = db.SemesterSettings
                    .AsNoTracking()
                    .Include(x => x.WeekdaySettings)
                    .Where(x => x.EmployeeId == EmployeeId && years.Contains(x.Year))
                    .ToList();

                foreach (var key in semesterKeys)
                {
                    _semesterSettingsByKey[key] = semesterSettings
                        .FirstOrDefault(x => x.Year == key.Year && x.Semester == key.Semester);
                }
            }

            public bool Contains(DateTime day) => _days.Contains(day.Date);

            public bool TryGetDaySettings(DateTime day, out DaySettings? settings)
            {
                var d = day.Date;
                if (!_loadedDaySettings.Contains(d))
                {
                    settings = null;
                    return false;
                }

                _daySettingsByDate.TryGetValue(d, out var row);
                settings = row;
                return true;
            }

            public bool TryGetManualDaySettings(DateTime day, out DaySettings? settings)
            {
                var d = day.Date;
                if (!_loadedManualDaySettings.Contains(d))
                {
                    settings = null;
                    return false;
                }

                _manualDaySettingsByDate.TryGetValue(d, out var row);
                settings = row;
                return true;
            }

            public void SetDaySettings(DateTime day, DaySettings? settings)
            {
                var d = day.Date;
                _loadedDaySettings.Add(d);

                if (settings == null)
                {
                    _daySettingsByDate.Remove(d);
                    return;
                }

                _daySettingsByDate[d] = settings;
            }

            public void SetManualDaySettings(DateTime day, DaySettings? settings)
            {
                var d = day.Date;
                _loadedManualDaySettings.Add(d);

                if (settings == null)
                {
                    _manualDaySettingsByDate.Remove(d);
                    return;
                }

                _manualDaySettingsByDate[d] = settings;
            }

            public bool TryGetSemesterSettings(int year, SemesterType semester, out SemesterSettings? settings)
                => _semesterSettingsByKey.TryGetValue((year, semester), out settings);

            public void Invalidate(IEnumerable<DateTime> days)
            {
                foreach (var d in days.Select(x => x.Date).Distinct().Where(Contains))
                {
                    _loadedDaySettings.Remove(d);
                    _loadedManualDaySettings.Remove(d);
                    _daySettingsByDate.Remove(d);
                    _manualDaySettingsByDate.Remove(d);
                }
            }

            public void Dispose()
            {
                _settingsReadCacheScope.Value = Previous;
            }
        }

        public static IDisposable BeginReadCache(int employeeId, IEnumerable<DateTime> days)
            => new SettingsReadCacheScope(_settingsReadCacheScope.Value, employeeId, days);

        private static SettingsReadCacheScope? GetReadCacheScopeForDay(int employeeId, DateTime day)
        {
            for (var scope = _settingsReadCacheScope.Value; scope != null; scope = scope.Previous)
            {
                if (scope.EmployeeId == employeeId && scope.Contains(day))
                    return scope;
            }

            return null;
        }

        private static void InvalidateReadCacheDays(int employeeId, IEnumerable<DateTime> days)
        {
            var normalized = days
                .Select(d => d.Date)
                .Distinct()
                .ToList();

            for (var scope = _settingsReadCacheScope.Value; scope != null; scope = scope.Previous)
            {
                if (scope.EmployeeId == employeeId)
                    scope.Invalidate(normalized);
            }
        }

        public static DaySettings? GetDaySettingsForDate(DateTime date, int employeeId = 1)
        {
            var day = date.Date;
            var scope = GetReadCacheScopeForDay(employeeId, day);

            if (scope != null)
            {
                if (scope.TryGetDaySettings(day, out var cached))
                    return cached;
            }

            using var db = new AppDbContext();
            var row = db.DaySettings
                .AsNoTracking()
                .FirstOrDefault(x => x.EmployeeId == employeeId && x.Date == day);

            scope?.SetDaySettings(day, row);
            return row;
        }

        public static DaySettings? GetManualDaySettingsForDate(DateTime day, int employeeId = 1)
        {
            var d = day.Date;
            var scope = GetReadCacheScopeForDay(employeeId, d);

            if (scope != null)
            {
                if (scope.TryGetManualDaySettings(d, out var cached))
                    return cached;
            }

            using var db = new AppDbContext();
            var row = db.DaySettings
                .AsNoTracking()
                .FirstOrDefault(x =>
                    x.EmployeeId == employeeId &&
                    x.Date == d &&
                    x.IsManualOverride);

            scope?.SetManualDaySettings(d, row);
            return row;
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
            InvalidateReadCacheDays(employeeId, new[] { day });
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
            InvalidateReadCacheDays(employeeId, new[] { d });
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
            InvalidateReadCacheDays(employeeId, Enumerable.Range(0, (to.Date - from.Date).Days + 1).Select(i => from.Date.AddDays(i)));
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
                InvalidateReadCacheDays(employeeId, new[] { d });
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
            InvalidateReadCacheDays(employeeId, new[] { d });
        }

        public static ResolvedDaySettings GetResolvedDaySettings(DateTime date, int employeeId = 1)
            => ResolveDaySettingsCore(date, employeeId, ignoreComputed: false);

        public static ResolvedDaySettings GetResolvedDaySettingsIgnoringComputed(DateTime date, int employeeId = 1)
            => ResolveDaySettingsCore(date, employeeId, ignoreComputed: true);

        private static ResolvedDaySettings ResolveDaySettingsCore(DateTime date, int employeeId, bool ignoreComputed)
        {
            var day = date.Date;

            var semester = GlobalSettingsService.GetSemesterForDate(day);

            var scope = GetReadCacheScopeForDay(employeeId, day);
            SemesterSettings? semesterSettings = null;
            DaySettings? dayOverride;

            if (scope != null)
            {
                scope.TryGetSemesterSettings(day.Year, semester, out semesterSettings);
                dayOverride = ignoreComputed
                    ? GetManualDaySettingsForDate(day, employeeId)
                    : GetDaySettingsForDate(day, employeeId);
            }
            else
            {
                using var db = new AppDbContext();

                semesterSettings = db.SemesterSettings
                    .AsNoTracking()
                    .Include(x => x.WeekdaySettings)
                    .FirstOrDefault(x =>
                        x.EmployeeId == employeeId &&
                        x.Year == day.Year &&
                        x.Semester == semester);

                dayOverride = db.DaySettings
                    .AsNoTracking()
                    .FirstOrDefault(x =>
                        x.EmployeeId == employeeId &&
                        x.Date == day &&
                        (!ignoreComputed || x.IsManualOverride));
            }

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
