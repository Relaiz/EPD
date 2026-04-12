using Microsoft.EntityFrameworkCore;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using TeacherScheduleApp.Data;
using TeacherScheduleApp.Helpers;
using TeacherScheduleApp.Messages;
using TeacherScheduleApp.Models;
using static TeacherScheduleApp.Services.EventService;

namespace TeacherScheduleApp.Services
{
    public class EventService
    {
        public const int DefaultEmployeeId = 1;
        private const int QUANTUM_MIN = 5;

        private bool _isBalancingNow;

        public static bool IsWorkLike(Event e)
            => e.EventType == EventType.Work || e.EventType == EventType.BusinessTrip;

        private static int ResolveEmployeeId(Event ev)
            => ev.EmployeeId > 0 ? ev.EmployeeId : DefaultEmployeeId;

        private static int RoundDownToQuantum(int minutes) => minutes - minutes % QUANTUM_MIN;
        private static int RoundUpToQuantum(int minutes) => minutes % QUANTUM_MIN == 0 ? minutes : minutes + (QUANTUM_MIN - minutes % QUANTUM_MIN);
        private static TimeSpan QMinutes(int minutes) => TimeSpan.FromMinutes(RoundDownToQuantum(minutes));
        private static int ToWholeMinutes(double hours) => (int)Math.Round(hours * 60.0);

        private static DateTime TruncToMinute(DateTime dt)
            => new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0, dt.Kind);

        public static bool IsWorkday(DateTime d)
            => d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday
               && !HolidayHelper.IsCzechHoliday(d);

        private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
        private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

        private static List<DateTime> GetWeekWorkdays(DateTime anyDate)
        {
            var date = anyDate.Date;
            int delta = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            var weekStart = date.AddDays(-delta);

            return Enumerable.Range(0, 7)
                .Select(i => weekStart.AddDays(i))
                .Where(IsWorkday)
                .ToList();
        }

        private static List<(DateTime s, DateTime e)> MergeIntervals(IEnumerable<(DateTime s, DateTime e)> intervals)
        {
            var sorted = intervals
                .Where(x => x.e > x.s)
                .OrderBy(x => x.s)
                .ToList();

            var merged = new List<(DateTime s, DateTime e)>();

            foreach (var seg in sorted)
            {
                if (merged.Count == 0 || merged[^1].e < seg.s)
                    merged.Add(seg);
                else
                    merged[^1] = (merged[^1].s, merged[^1].e > seg.e ? merged[^1].e : seg.e);
            }

            return merged;
        }

        private static List<(DateTime s, DateTime e, List<Event> owners)> BuildMergedWithOwners(IEnumerable<Event> evs, DateTime winS, DateTime winE)
        {
            var segs = evs
                .Select(e => (
                    s: e.StartTime < winS ? winS : e.StartTime,
                    e: e.EndTime > winE ? winE : e.EndTime,
                    ev: e))
                .Where(x => x.e > x.s)
                .OrderBy(x => x.s)
                .ToList();

            var merged = new List<(DateTime s, DateTime e, List<Event> owners)>();

            foreach (var seg in segs)
            {
                if (merged.Count == 0 || merged[^1].e < seg.s)
                {
                    merged.Add((seg.s, seg.e, new List<Event> { seg.ev }));
                }
                else
                {
                    if (seg.e > merged[^1].e)
                        merged[^1] = (merged[^1].s, seg.e, merged[^1].owners);

                    merged[^1].owners.Add(seg.ev);
                }
            }

            return merged;
        }

        private static DateTime FirstDayOfIsoWeek(int isoYear, int isoWeek)
        {
            var thursday = new DateTime(isoYear, 1, 4);
            int delta = System.Globalization.ISOWeek.GetWeekOfYear(thursday) == 1 ? 0 : 7;
            var monday = thursday.AddDays(-((int)thursday.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7);
            return monday.AddDays(7 * (isoWeek - 1) - delta);
        }

        private static ResolvedDaySettings GetResolvedWindow(DateTime day, int employeeId)
            => SettingsService.GetResolvedDaySettings(day.Date, employeeId);

        private void InvalidateMonthsForDates(IEnumerable<DateTime> dates)
        {
            var months = dates.Select(d => new { d.Year, d.Month }).Distinct().ToList();
            foreach (var m in months)
                MonthBalanceStore.Invalidate(m.Year, m.Month);
        }

        public List<Event> GetAllEvents(int employeeId = DefaultEmployeeId)
        {
            using var db = new AppDbContext();
            return db.Events
                .Where(x => x.EmployeeId == employeeId)
                .ToList();
        }

        public List<Event> LoadEvents(int employeeId = DefaultEmployeeId)
        {
            using var db = new AppDbContext();
            return db.Events
                .Where(e => e.EmployeeId == employeeId && !e.IsDeleted)
                .ToList();
        }

        public Event? GetEventById(int id, int employeeId = DefaultEmployeeId)
        {
            using var db = new AppDbContext();
            return db.Events.FirstOrDefault(e => e.Id == id && e.EmployeeId == employeeId);
        }

        public Event? FindEventByStartTime(DateTime startTime, int employeeId = DefaultEmployeeId)
        {
            using var db = new AppDbContext();
            return db.Events.FirstOrDefault(e =>
                e.EmployeeId == employeeId &&
                e.StartTime == startTime &&
                !e.IsDeleted);
        }

        public List<Event> GetEventsForDay(int employeeId, DateTime date)
        {
            using var db = new AppDbContext();

            return db.Events
                .Where(e => e.EmployeeId == employeeId &&
                            e.StartTime.Date == date.Date &&
                            !e.IsDeleted)
                .OrderBy(e => e.StartTime)
                .ToList();
        }

        public List<Event> GetEventsForWeek(int employeeId, DateTime anyDate)
        {
            var date = anyDate.Date;
            int delta = (int)date.DayOfWeek - (int)DayOfWeek.Monday;
            if (delta < 0) delta += 7;

            var weekStart = date.AddDays(-delta);
            var weekEnd = weekStart.AddDays(7);

            using var db = new AppDbContext();

            return db.Events
                .Where(e => e.EmployeeId == employeeId &&
                            !e.IsDeleted &&
                            e.StartTime < weekEnd &&
                            e.EndTime >= weekStart)
                .OrderBy(e => e.StartTime)
                .ToList();
        }

        public List<Event> GetEventsForMonth(int employeeId, DateTime date)
        {
            using var db = new AppDbContext();

            return db.Events
                .Where(e => e.EmployeeId == employeeId &&
                            e.StartTime.Year == date.Year &&
                            e.StartTime.Month == date.Month &&
                            !e.IsDeleted)
                .OrderBy(e => e.StartTime)
                .ToList();
        }

        public List<Event> GetEventsForRange(int employeeId, DateTime start, DateTime end)
        {
            using var db = new AppDbContext();

            return db.Events
                .Where(e => e.EmployeeId == employeeId &&
                            e.StartTime < end &&
                            e.EndTime >= start &&
                            !e.IsDeleted)
                .OrderBy(e => e.StartTime)
                .ToList();
        }

        public void CreateEventsBulk(IEnumerable<Event> events)
        {
            using var db = new AppDbContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;

            foreach (var ev in events)
            {
                if (ev.EmployeeId <= 0)
                    ev.EmployeeId = DefaultEmployeeId;
            }

            db.Events.AddRange(events);
            db.SaveChanges();
        }

        public void CreateAutoEvent(Event ev)
        {
            using var db = new AppDbContext();

            if (ev.EmployeeId <= 0)
                ev.EmployeeId = DefaultEmployeeId;

            db.Events.Add(ev);
            db.SaveChanges();
        }

        public void CreateEvent(Event ev)
        {
            var employeeId = ResolveEmployeeId(ev);

            if (ev.IsAutoGenerated)
            {
                using var dbContext = new AppDbContext();
                ev.EmployeeId = employeeId;
                dbContext.Events.Add(ev);
                dbContext.SaveChanges();
                return;
            }

            DateTime parentDate = ev.StartTime.Date;

            while (parentDate <= ev.EndTime.Date &&
                   (parentDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ||
                    HolidayHelper.IsCzechHoliday(parentDate)))
            {
                parentDate = parentDate.AddDays(1);
            }

            if (parentDate > ev.EndTime.Date)
                return;

            using var db = new AppDbContext();

            var parent = new Event
            {
                EmployeeId = employeeId,
                Title = ev.Title,
                Description = ev.Description,
                EventType = ev.EventType,
                AllDay = ev.AllDay,
                StartTime = parentDate + ev.StartTime.TimeOfDay,
                EndTime = parentDate + ev.EndTime.TimeOfDay,
                ParentEventId = null,
                IsAutoGenerated = false,
                ImportBatchId = ev.ImportBatchId,
                IsDeleted = false,
                HasCollision = ev.HasCollision,
                AutoGeneratedForDate = null
            };

            db.Events.Add(parent);
            db.SaveChanges();

            for (var day = parentDate.AddDays(1); day <= ev.EndTime.Date; day = day.AddDays(1))
            {
                if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || HolidayHelper.IsCzechHoliday(day))
                    continue;

                var child = new Event
                {
                    EmployeeId = employeeId,
                    Title = ev.Title,
                    Description = ev.Description,
                    EventType = ev.EventType,
                    AllDay = ev.AllDay,
                    StartTime = day + ev.StartTime.TimeOfDay,
                    EndTime = day + ev.EndTime.TimeOfDay,
                    ParentEventId = parent.Id,
                    IsAutoGenerated = false,
                    ImportBatchId = ev.ImportBatchId,
                    IsDeleted = false,
                    HasCollision = ev.HasCollision,
                    AutoGeneratedForDate = null
                };

                db.Events.Add(child);
            }

            db.SaveChanges();

            var affectedDates = Enumerable
                .Range(0, (ev.EndTime.Date - parentDate).Days + 1)
                .Select(offset => parentDate.AddDays(offset))
                .Where(d => d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday && !HolidayHelper.IsCzechHoliday(d))
                .ToList();

            var autos = db.Events
                .Where(x => x.EmployeeId == employeeId
                         && x.IsAutoGenerated
                         && x.AutoGeneratedForDate.HasValue
                         && affectedDates.Contains(x.AutoGeneratedForDate.Value.Date))
                .ToList();

            db.Events.RemoveRange(autos);
            db.SaveChanges();

            InvalidateMonthsForDates(affectedDates);
            InvalidateWeeksForDates(affectedDates, employeeId);

            var autoGen = new AutomaticEventsGeneratorService(this, _ => Task.FromResult(false), employeeId);
            foreach (var d in affectedDates)
            {
                autoGen.RegenerateDailyEventsAsync(d).GetAwaiter().GetResult();
                EnsureLunchInsideWorkWindowAsync(employeeId, d).GetAwaiter().GetResult();
            }

            MessageBus.Current.SendMessage(new AutoEventsGeneratedMessage());
        }

        public void UpdateEvent(Event ev, bool suppressRegen = false)
        {
            using var db = new AppDbContext();

            var employeeId = ResolveEmployeeId(ev);

            var parent = db.Events.SingleOrDefault(x => x.Id == ev.Id && x.EmployeeId == employeeId);
            if (parent == null)
                return;

            var oldStart = parent.StartTime.Date;
            var oldEnd = parent.EndTime.Date;

            var wasAutoGenerated = parent.IsAutoGenerated;

            parent.Title = ev.Title;
            parent.StartTime = ev.StartTime;
            parent.EndTime = ev.EndTime;
            parent.AllDay = ev.AllDay;
            parent.Description = ev.Description;
            parent.IsDeleted = ev.IsDeleted;
            parent.EventType = ev.EventType;
            parent.HasCollision = ev.HasCollision;
            parent.ImportBatchId = ev.ImportBatchId;

            if (wasAutoGenerated)
            {
                parent.IsAutoGenerated = false;
                parent.AutoGeneratedForDate = null;
                parent.ParentEventId = null;
            }
            else
            {
                parent.IsAutoGenerated = ev.IsAutoGenerated;
                parent.AutoGeneratedForDate = ev.AutoGeneratedForDate;
            }

            db.SaveChanges();

            var oldChildren = db.Events
                .Where(c => c.EmployeeId == employeeId && c.ParentEventId == parent.Id)
                .ToList();

            if (oldChildren.Any())
            {
                db.Events.RemoveRange(oldChildren);
                db.SaveChanges();
            }

            DateTime start = parent.StartTime.Date.AddDays(1);

            for (var day = start; day <= ev.EndTime.Date; day = day.AddDays(1))
            {
                if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || HolidayHelper.IsCzechHoliday(day))
                    continue;

                var child = new Event
                {
                    EmployeeId = employeeId,
                    Title = parent.Title,
                    Description = parent.Description,
                    EventType = parent.EventType,
                    AllDay = parent.AllDay,
                    StartTime = day + parent.StartTime.TimeOfDay,
                    EndTime = day + parent.EndTime.TimeOfDay,
                    ParentEventId = parent.Id,
                    IsAutoGenerated = false,
                    ImportBatchId = parent.ImportBatchId,
                    IsDeleted = false,
                    HasCollision = parent.HasCollision,
                    AutoGeneratedForDate = null
                };

                db.Events.Add(child);
            }

            db.SaveChanges();

            var affectedDates = new HashSet<DateTime>(
                Enumerable.Range(0, (oldEnd - oldStart).Days + 1).Select(i => oldStart.AddDays(i))
                .Concat(
                    Enumerable.Range(0, (parent.EndTime.Date - parent.StartTime.Date).Days + 1)
                        .Select(i => parent.StartTime.Date.AddDays(i)))
            );

            InvalidateMonthsForDates(affectedDates);
            InvalidateWeeksForDates(affectedDates, employeeId);

            if (!suppressRegen)
            {
                var gen = new AutomaticEventsGeneratorService(this, _ => Task.FromResult(false), employeeId);

                foreach (var day in affectedDates.Where(IsWorkday).OrderBy(x => x))
                {
                    bool dayHadAuto = DayHadAutoGeneratedEvents(day, employeeId) || wasAutoGenerated;
                    RebuildDayAfterChange(day, employeeId, dayHadAuto);
                }

                MessageBus.Current.SendMessage(new AutoEventsGeneratedMessage());
                MessageBus.Current.SendMessage(new UserSettingsChangedMessage(parent.StartTime.Date));
            }
        }

        public void DeleteEvent(int id, int employeeId = DefaultEmployeeId)
        {
            using var db = new AppDbContext();

            var ev = db.Events.FirstOrDefault(e => e.Id == id && e.EmployeeId == employeeId);
            if (ev == null)
                return;

            var day = ev.StartTime.Date;

            bool dayHadAuto = db.Events.Any(e =>
                e.EmployeeId == employeeId &&
                !e.IsDeleted &&
                e.StartTime.Date == day &&
                e.IsAutoGenerated);

            ev.IsDeleted = true;
            db.SaveChanges();

            InvalidateMonthsForDates(new[] { day });
            InvalidateWeeksForDates(new[] { day }, employeeId);

            RebuildDayAfterChange(day, employeeId, dayHadAuto);

            MessageBus.Current.SendMessage(new AutoEventsGeneratedMessage());
            MessageBus.Current.SendMessage(new UserSettingsChangedMessage(day));
        }

        public void DeleteEventCascadeAndCleanup(int id, int employeeId = DefaultEmployeeId)
        {
            using var db = new AppDbContext();

            var toDelete = db.Events
                .Where(e => e.EmployeeId == employeeId && (e.Id == id || e.ParentEventId == id))
                .ToList();

            if (toDelete.Count == 0)
                return;

            var affectedDates = toDelete
                .Select(e => e.StartTime.Date)
                .Distinct()
                .ToList();

            foreach (var e in toDelete)
                e.IsDeleted = true;

            db.SaveChanges();
            InvalidateMonthsForDates(affectedDates);
            InvalidateWeeksForDates(affectedDates, employeeId);
            var dayHadAutoByDate = affectedDates.ToDictionary(
                d => d,
                d => db.Events.Any(e =>
                    e.EmployeeId == employeeId &&
                    !e.IsDeleted &&
                    e.StartTime.Date == d &&
                    e.IsAutoGenerated)
            );

            foreach (var day in affectedDates)
            {
                bool anyLeft = db.Events.Any(e => e.EmployeeId == employeeId && !e.IsDeleted && e.StartTime.Date == day);
                if (!anyLeft)
                {
                    SettingsService.DeleteComputedDaySettingsForDate(day, employeeId);
                    continue;
                }

                RebuildDayAfterChange(day, employeeId, dayHadAutoByDate[day]);

                MessageBus.Current.SendMessage(new AutoEventsGeneratedMessage());
                MessageBus.Current.SendMessage(new UserSettingsChangedMessage(day));
            }
        }

        public int SoftDeleteImportedInRange(DateTime startIncl, DateTime endIncl, int employeeId = DefaultEmployeeId)
        {
            using var db = new AppDbContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;

            var toDel = db.Events
                .Where(e => e.EmployeeId == employeeId
                         && !e.IsDeleted
                         && e.ImportBatchId != null
                         && e.StartTime.Date >= startIncl.Date
                         && e.StartTime.Date <= endIncl.Date)
                .ToList();

            foreach (var e in toDel)
                e.IsDeleted = true;

            db.SaveChanges();
            return toDel.Count;
        }

        public async Task BulkSoftDeleteImportedInRangeAsync(DateTime from, DateTime to, int employeeId = DefaultEmployeeId)
        {
            var s = from.Date;
            var e = to.Date;

            using var db = new AppDbContext();

            var affectedDays = await db.Events
                .Where(x => x.EmployeeId == employeeId
                            && !x.IsDeleted
                            && x.ImportBatchId != null
                            && x.StartTime.Date >= s
                            && x.StartTime.Date <= e)
                .Select(x => x.StartTime.Date)
                .Distinct()
                .ToListAsync();
#if NET7_0_OR_GREATER
            await db.Events
                .Where(x => x.EmployeeId == employeeId
                            && !x.IsDeleted
                            && x.ImportBatchId != null
                            && x.StartTime.Date >= s
                            && x.StartTime.Date <= e)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.IsDeleted, true));
#else
            var list = await db.Events
                .Where(x => x.EmployeeId == employeeId
                            && !x.IsDeleted
                            && x.ImportBatchId != null
                            && x.StartTime.Date >= s
                            && x.StartTime.Date <= e)
                .ToListAsync();

            foreach (var ev in list)
                ev.IsDeleted = true;

            await db.SaveChangesAsync();
#endif

            var gen = new AutomaticEventsGeneratorService(this, _ => Task.FromResult(false), employeeId);
            foreach (var day in affectedDays)
            {
                SelfTrimStore.ClearDates(employeeId, new[] { day });
                AdjustDaySettingsAfterChange(day, employeeId);
                await gen.RegenerateDailyEventsAsync(day, preserveUserSettings: false);
            }

            MessageBus.Current.SendMessage(new AutoEventsGeneratedMessage());
        }

        public async Task DeleteEventsByImportIdFastAsync(string batchId, int employeeId = DefaultEmployeeId)
        {
            using var db = new AppDbContext();

            var affectedDays = await db.Events
                .Where(e => e.EmployeeId == employeeId &&
                            !e.IsDeleted &&
                            e.ImportBatchId == batchId)
                .Select(e => e.StartTime.Date)
                .Distinct()
                .ToListAsync();

#if NET7_0_OR_GREATER
            await db.Events
                .Where(e => e.EmployeeId == employeeId &&
                            !e.IsDeleted &&
                            e.ImportBatchId == batchId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true));
#else
            var toUpdate = await db.Events
                .Where(e => e.EmployeeId == employeeId &&
                            !e.IsDeleted &&
                            e.ImportBatchId == batchId)
                .ToListAsync();

            foreach (var ev in toUpdate)
                ev.IsDeleted = true;

            await db.SaveChangesAsync();
#endif

            var gen = new AutomaticEventsGeneratorService(this, _ => Task.FromResult(false), employeeId);
            foreach (var day in affectedDays)
            {
                SelfTrimStore.ClearDates(employeeId, new[] { day });
                AdjustDaySettingsAfterChange(day, employeeId);
                await gen.RegenerateDailyEventsAsync(day, preserveUserSettings: false);
            }

                

            MessageBus.Current.SendMessage(new AutoEventsGeneratedMessage());
        }

        public void DeleteEventsByImportId(string batchId, int employeeId = DefaultEmployeeId)
        {
            var toDelete = GetAllEvents(employeeId)
                .Where(e => e.ImportBatchId == batchId)
                .ToList();

            foreach (var ev in toDelete)
                DeleteEvent(ev.Id, employeeId);
        }

        public void RemoveAutoGeneratedEvents(int employeeId, DateTime date)
        {
            using var db = new AppDbContext();

            var evs = db.Events
                .Where(e => e.EmployeeId == employeeId &&
                            e.IsAutoGenerated &&
                            e.AutoGeneratedForDate == date.Date)
                .ToList();

            db.Events.RemoveRange(evs);
            db.SaveChanges();
        }

        public static int PurgeSoftDeleted()
        {
            using var db = new AppDbContext();
            using var tx = db.Database.BeginTransaction();

            const int batchSize = 500;
            int totalDeleted = 0;

            while (true)
            {
                var leafIds = db.Events
                    .Where(e => e.IsDeleted)
                    .Where(e => !db.Events.Any(ch => ch.ParentEventId == e.Id))
                    .Select(e => e.Id)
                    .Take(batchSize)
                    .ToList();

                if (leafIds.Count == 0)
                    break;

                var batch = db.Events
                    .Where(e => leafIds.Contains(e.Id))
                    .ToList();

                db.Events.RemoveRange(batch);
                db.SaveChanges();

                totalDeleted += batch.Count;
            }

            var blocked = db.Events
                .Where(e => e.IsDeleted)
                .Select(e => new
                {
                    e.Id,
                    e.ParentEventId
                })
                .ToList();

            tx.Commit();
            return totalDeleted;
        }

        public void UpdateSingleEventEnd(int eventId, DateTime newEnd, int employeeId = DefaultEmployeeId)
        {
            using var db = new AppDbContext();

            var ev = db.Events.SingleOrDefault(e => e.Id == eventId &&
                                                    e.EmployeeId == employeeId &&
                                                    !e.IsDeleted);
            if (ev == null)
                return;

            if (newEnd < ev.StartTime)
                newEnd = ev.StartTime;

            ev.EndTime = newEnd;
            db.SaveChanges();
        }

        private (Event? first, Event? last) GetEdgeWorkEvents(DateTime day, int employeeId = DefaultEmployeeId)
        {
            var evs = GetEventsForDay(employeeId, day)
                .Where(e => IsWorkLike(e) && !e.IsDeleted)
                .OrderBy(e => e.StartTime)
                .ToList();

            return (evs.FirstOrDefault(), evs.LastOrDefault());
        }

        private bool CanTrimStart(DateTime day, int employeeId = DefaultEmployeeId)
        {
            var (first, _) = GetEdgeWorkEvents(day, employeeId);
            return first != null && first.IsAutoGenerated;
        }

        private bool CanTrimEnd(DateTime day, int employeeId = DefaultEmployeeId)
        {
            var (_, last) = GetEdgeWorkEvents(day, employeeId);
            return last != null && last.IsAutoGenerated;
        }

        private bool IsLockedEdgeDay(DateTime day, int employeeId = DefaultEmployeeId)
        {
            var evs = GetEventsForDay(employeeId, day)
                .Where(e => !e.IsDeleted && e.EventType != EventType.Lunch)
                .OrderBy(e => e.StartTime)
                .ToList();

            if (evs.Count == 0)
                return false;

            bool firstManualWorkLike = !evs.First().IsAutoGenerated && IsWorkLike(evs.First());
            bool lastManualWorkLike = !evs.Last().IsAutoGenerated && IsWorkLike(evs.Last());

            return firstManualWorkLike && lastManualWorkLike;
        }

        private void DeleteLunchEvents(DateTime day, int employeeId = DefaultEmployeeId)
        {
            using var db = new AppDbContext();

            var lunches = db.Events
                .Where(e => e.EmployeeId == employeeId &&
                            !e.IsDeleted &&
                            e.StartTime.Date == day.Date &&
                            e.EventType == EventType.Lunch &&
                            e.IsAutoGenerated)
                .ToList();

            if (lunches.Count == 0)
                return;

            db.Events.RemoveRange(lunches);
            db.SaveChanges();
        }

        private void ReplaceLunchEvent(DateTime day, TimeSpan ls, TimeSpan le, int employeeId = DefaultEmployeeId)
        {
            DeleteLunchEvents(day, employeeId);

            if (le <= ls)
                return;

            using var db = new AppDbContext();

            bool hasAnyLunch = db.Events.Any(e =>
                e.EmployeeId == employeeId &&
                !e.IsDeleted &&
                e.StartTime.Date == day.Date &&
                e.EventType == EventType.Lunch);

            if (hasAnyLunch)
                return;

            db.Events.Add(new Event
            {
                EmployeeId = employeeId,
                Title = "Oběd (auto)",
                EventType = EventType.Lunch,
                StartTime = day + ls,
                EndTime = day + le,
                IsAutoGenerated = true,
                AutoGeneratedForDate = day.Date
            });

            db.SaveChanges();
        }

        private void SplitAutoWorkAroundOneLunch(DateTime day, TimeSpan ls, TimeSpan le, int employeeId = DefaultEmployeeId)
        {
            if (le <= ls)
                return;

            var lunchS = day + ls;
            var lunchE = day + le;

            using var db = new AppDbContext();

            var toFix = db.Events
                .Where(e => e.EmployeeId == employeeId &&
                            !e.IsDeleted &&
                            e.StartTime.Date == day.Date &&
                            e.IsAutoGenerated &&
                            e.EventType == EventType.Work &&
                            e.EndTime > lunchS &&
                            e.StartTime < lunchE)
                .OrderBy(e => e.StartTime)
                .ToList();

            foreach (var ev in toFix)
            {
                bool hasLeft = ev.StartTime < lunchS;
                bool hasRight = ev.EndTime > lunchE;

                if (hasLeft && hasRight)
                {
                    db.Events.Add(new Event
                    {
                        EmployeeId = employeeId,
                        Title = ev.Title,
                        Description = ev.Description,
                        EventType = EventType.Work,
                        StartTime = lunchE,
                        EndTime = ev.EndTime,
                        IsAutoGenerated = true,
                        AutoGeneratedForDate = day,
                        ParentEventId = ev.ParentEventId
                    });

                    ev.EndTime = lunchS;
                }
                else if (hasLeft)
                {
                    ev.EndTime = lunchS;
                }
                else if (hasRight)
                {
                    ev.StartTime = lunchE;
                }
                else
                {
                    ev.IsDeleted = true;
                }
            }

            db.SaveChanges();
        }

        private void SplitAutoWorkAroundLunch(DateTime day, TimeSpan ls, TimeSpan le, int employeeId = DefaultEmployeeId)
        {
            var lunches = GetEventsForDay(employeeId, day)
                .Where(e => !e.IsDeleted && e.EventType == EventType.Lunch)
                .OrderBy(e => e.StartTime)
                .Select(e => (S: e.StartTime.TimeOfDay, E: e.EndTime.TimeOfDay))
                .Where(x => x.E > x.S)
                .ToList();

            foreach (var lunch in lunches)
                SplitAutoWorkAroundOneLunch(day, lunch.S, lunch.E, employeeId);

            InvalidateMonthsForDates(new[] { day });
            InvalidateWeeksForDates(new[] { day }, employeeId);
        }

        private List<(DateTime s, DateTime e)> GetBusyIntervals(DateTime day, TimeSpan arr, TimeSpan dep, int employeeId = DefaultEmployeeId)
        {
            var winS = day + arr;
            var winE = day + dep;

            return GetEventsForDay(employeeId, day)
                .Where(e => !e.IsDeleted && e.EventType != EventType.Lunch)
                .Select(e => (s: e.StartTime < winS ? winS : e.StartTime,
                              e: e.EndTime > winE ? winE : e.EndTime))
                .Where(x => x.e > x.s)
                .OrderBy(x => x.s)
                .ToList();
        }

        private (TimeSpan ls, TimeSpan le) RefitLunch(
            DateTime day,
            TimeSpan arr,
            TimeSpan dep,
            TimeSpan desiredLs,
            TimeSpan desiredLe,
            TimeSpan fallbackLen,
            int employeeId = DefaultEmployeeId)
        {
            var len = desiredLe > desiredLs ? (desiredLe - desiredLs) : fallbackLen;
            if (dep <= arr || len <= TimeSpan.Zero)
                return (TimeSpan.Zero, TimeSpan.Zero);

            var winS = day + arr;
            var winE = day + dep;
            var busy = GetBusyIntervals(day, arr, dep, employeeId);

            var free = new List<(DateTime s, DateTime e)>();
            DateTime cursor = winS;

            foreach (var iv in busy)
            {
                if (iv.s > cursor)
                    free.Add((cursor, iv.s));

                if (iv.e > cursor)
                    cursor = iv.e;
            }

            if (cursor < winE)
                free.Add((cursor, winE));

            var candidates = free.Where(g => (g.e - g.s) >= len).ToList();
            if (candidates.Count == 0)
                return (TimeSpan.Zero, TimeSpan.Zero);

            var desiredStart = day + desiredLs;
            DateTime sCand;
            DateTime eCand;

            var exactFit = candidates.FirstOrDefault(g =>
                desiredLe > desiredLs &&
                desiredStart >= g.s &&
                desiredStart + len <= g.e);

            if (exactFit != default)
            {
                sCand = desiredStart;
                eCand = sCand + len;
            }
            else
            {
                var afterDesired = candidates
                    .Where(g => g.e > desiredStart)
                    .OrderBy(g => g.s)
                    .FirstOrDefault();

                if (afterDesired != default)
                {
                    sCand = desiredStart > afterDesired.s ? desiredStart : afterDesired.s;
                    if (sCand + len > afterDesired.e)
                        sCand = afterDesired.s;

                    eCand = sCand + len;
                }
                else
                {
                    var first = candidates.OrderBy(g => g.s).First();
                    sCand = first.s;
                    eCand = sCand + len;
                }
            }

            if (eCand <= sCand)
                return (TimeSpan.Zero, TimeSpan.Zero);

            return (sCand.TimeOfDay, eCand.TimeOfDay);
        }

        private bool TryCarveLunchFromInnerAuto(DateTime day, TimeSpan arr, TimeSpan dep, TimeSpan len, out TimeSpan ls, out TimeSpan le, int employeeId = DefaultEmployeeId)
        {
            ls = le = TimeSpan.Zero;

            if (dep <= arr || len <= TimeSpan.Zero)
                return false;

            var all = GetEventsForDay(employeeId, day)
                .Where(e => !e.IsDeleted && e.EventType != EventType.Lunch)
                .OrderBy(e => e.StartTime)
                .ToList();

            if (all.Count < 3)
                return false;

            var candidate = all
                .Skip(1).Take(all.Count - 2)
                .FirstOrDefault(e =>
                    e.IsAutoGenerated &&
                    e.EventType == EventType.Work &&
                    (e.EndTime - e.StartTime) >= len + TimeSpan.FromMinutes(1) &&
                    e.StartTime >= day + arr &&
                    e.EndTime <= day + dep);

            if (candidate == null)
                return false;

            var cutStart = candidate.StartTime;
            var cutEnd = candidate.StartTime + len;

            var idx = all.FindIndex(x => x.Id == candidate.Id);
            if (idx <= 0 || idx >= all.Count - 1)
                return false;

            var prevEnd = all[idx - 1].EndTime;
            var nextStart = all[idx + 1].StartTime;

            if (cutStart < prevEnd || cutEnd > nextStart)
                return false;

            using (var db = new AppDbContext())
            {
                var ev = db.Events.Single(x => x.Id == candidate.Id && x.EmployeeId == employeeId && !x.IsDeleted);
                ev.StartTime = cutEnd;
                if (ev.EndTime < ev.StartTime)
                    ev.EndTime = ev.StartTime;

                db.SaveChanges();
            }

            CreateAutoEvent(new Event
            {
                EmployeeId = employeeId,
                Title = "Oběd (auto)",
                EventType = EventType.Lunch,
                StartTime = cutStart,
                EndTime = cutEnd,
                IsAutoGenerated = true,
                AutoGeneratedForDate = day
            });

            ls = cutStart.TimeOfDay;
            le = cutEnd.TimeOfDay;
            return true;
        }

        private double TrimEndAuto(DateTime day, TimeSpan cutSpan, int employeeId)
        {
            if (cutSpan <= TimeSpan.Zero)
                return 0;

            var (_, last) = GetEdgeWorkEvents(day, employeeId);
            if (last == null || !last.IsAutoGenerated)
                return 0;

            var maxSpan = last.EndTime - last.StartTime;
            if (maxSpan <= TimeSpan.Zero)
                return 0;

            var take = cutSpan <= maxSpan ? cutSpan : maxSpan;
            take = QMinutes((int)Math.Round(take.TotalMinutes));
            if (take <= TimeSpan.Zero)
                return 0;

            var newEnd = TruncToMinute(last.EndTime - take);

            UpdateSingleEventEnd(last.Id, newEnd, employeeId);

            var resolved = GetResolvedWindow(day, employeeId);
            var arr = resolved.ArrivalTime;
            var dep = newEnd.TimeOfDay;
            var ls = resolved.LunchStart;
            var le = resolved.LunchEnd;

            if (le > ls && (le > dep || ls < arr))
                (ls, le) = (TimeSpan.Zero, TimeSpan.Zero);

            SettingsService.SaveDaySettingsForDate(day, arr, dep, ls, le, employeeId);
            ReplaceLunchEvent(day, ls, le, employeeId);
            SplitAutoWorkAroundLunch(day, ls, le, employeeId);
            if (ShouldTrackSelfTrim())
                SelfTrimStore.Add(employeeId, day, TransferEdge.End, (int)take.TotalMinutes);
            return take.TotalHours;
        }

        private double TrimStartAuto(DateTime day, TimeSpan cutSpan, int employeeId)
        {
            if (cutSpan <= TimeSpan.Zero)
                return 0;

            var (first, _) = GetEdgeWorkEvents(day, employeeId);
            if (first == null || !first.IsAutoGenerated)
                return 0;

            var maxSpan = first.EndTime - first.StartTime;
            if (maxSpan <= TimeSpan.Zero)
                return 0;

            var take = cutSpan <= maxSpan ? cutSpan : maxSpan;
            take = QMinutes((int)Math.Round(take.TotalMinutes));
            if (take <= TimeSpan.Zero)
                return 0;

            var newStart = TruncToMinute(first.StartTime + take);

            using (var db = new AppDbContext())
            {
                var ev = db.Events.Single(x => x.Id == first.Id && x.EmployeeId == employeeId);
                ev.StartTime = newStart;
                db.SaveChanges();
            }

            var resolved = GetResolvedWindow(day, employeeId);
            var arr = newStart.TimeOfDay;
            var dep = resolved.DepartureTime;
            var ls = resolved.LunchStart;
            var le = resolved.LunchEnd;

            if (le > ls && (le > dep || ls < arr))
                (ls, le) = (TimeSpan.Zero, TimeSpan.Zero);

            SettingsService.SaveDaySettingsForDate(day, arr, dep, ls, le, employeeId);
            ReplaceLunchEvent(day, ls, le, employeeId);
            SplitAutoWorkAroundLunch(day, ls, le, employeeId);
            if (ShouldTrackSelfTrim())
                SelfTrimStore.Add(employeeId, day, TransferEdge.Start, (int)take.TotalMinutes);
            return take.TotalHours;
        }

        private double TrimEndAuto(DateTime day, double hours, int employeeId)
            => TrimEndAuto(day, QMinutes(ToWholeMinutes(hours)), employeeId);

        private double TrimStartAuto(DateTime day, double hours, int employeeId)
            => TrimStartAuto(day, QMinutes(ToWholeMinutes(hours)), employeeId);

        private double ExtendEndAuto(DateTime day, double hours, int employeeId)
        {
            int addMin = RoundDownToQuantum(ToWholeMinutes(hours));
            if (addMin <= 0)
                return 0;

            var resolved = GetResolvedWindow(day, employeeId);
            var daySoftEnd = day.Date.AddDays(1).AddMinutes(-QUANTUM_MIN);

            var nonLunch = GetEventsForDay(employeeId, day)
                .Where(e => !e.IsDeleted && e.EventType != EventType.Lunch)
                .OrderBy(e => e.StartTime)
                .ToList();

            var anchorStart = nonLunch.Any()
                ? nonLunch.Max(e => e.EndTime)
                : day + resolved.ArrivalTime;

            if (anchorStart >= daySoftEnd)
                return 0;

            int freeMin = RoundDownToQuantum((int)(daySoftEnd - anchorStart).TotalMinutes);
            if (freeMin <= 0)
                return 0;

            int takeMin = Math.Min(addMin, freeMin);
            if (takeMin <= 0)
                return 0;

            var start = TruncToMinute(anchorStart);
            var end = TruncToMinute(start.AddMinutes(takeMin));

            CreateAutoEvent(new Event
            {
                EmployeeId = employeeId,
                Title = "Vyvažování práce",
                EventType = EventType.Work,
                StartTime = start,
                EndTime = end,
                IsAutoGenerated = true,
                AutoGeneratedForDate = day
            });

            var predictedStart = nonLunch.Any()
                ? nonLunch.Min(e => e.StartTime)
                : start;

            var predictedEnd = nonLunch.Any()
                ? Max(nonLunch.Max(e => e.EndTime), end)
                : end;

            var arr = predictedStart.TimeOfDay;
            var dep = predictedEnd.TimeOfDay;

            var ls = resolved.LunchStart;
            var le = resolved.LunchEnd;
            if (le > ls && (le > dep || ls < arr))
                (ls, le) = (TimeSpan.Zero, TimeSpan.Zero);

            SettingsService.SaveDaySettingsForDate(day, arr, dep, ls, le, employeeId);
            ReplaceLunchEvent(day, ls, le, employeeId);
            SplitAutoWorkAroundLunch(day, ls, le, employeeId);

            return takeMin / 60.0;
        }

        private double ExtendStartAuto(DateTime day, double hours, int employeeId)
        {
            int addMin = RoundDownToQuantum(ToWholeMinutes(hours));
            if (addMin <= 0)
                return 0;

            var resolved = GetResolvedWindow(day, employeeId);

            var nonLunch = GetEventsForDay(employeeId, day)
                .Where(e => !e.IsDeleted && e.EventType != EventType.Lunch)
                .OrderBy(e => e.StartTime)
                .ToList();

            var anchorEnd = nonLunch.Any()
                ? nonLunch.Min(e => e.StartTime)
                : day + resolved.DepartureTime;

            var daySoftStart = day.Date;

            if (anchorEnd <= daySoftStart)
                return 0;

            int freeMin = RoundDownToQuantum((int)(anchorEnd - daySoftStart).TotalMinutes);
            if (freeMin <= 0)
                return 0;

            int takeMin = Math.Min(addMin, freeMin);
            if (takeMin <= 0)
                return 0;

            var end = TruncToMinute(anchorEnd);
            var start = TruncToMinute(end.AddMinutes(-takeMin));

            CreateAutoEvent(new Event
            {
                EmployeeId = employeeId,
                Title = "Vyvažování práce",
                EventType = EventType.Work,
                StartTime = start,
                EndTime = end,
                IsAutoGenerated = true,
                AutoGeneratedForDate = day
            });

            var predictedStart = nonLunch.Any()
                ? Min(nonLunch.Min(e => e.StartTime), start)
                : start;

            var predictedEnd = nonLunch.Any()
                ? nonLunch.Max(e => e.EndTime)
                : end;

            var arr = predictedStart.TimeOfDay;
            var dep = predictedEnd.TimeOfDay;

            var ls = resolved.LunchStart;
            var le = resolved.LunchEnd;
            if (le > ls && (le > dep || ls < arr))
                (ls, le) = (TimeSpan.Zero, TimeSpan.Zero);

            SettingsService.SaveDaySettingsForDate(day, arr, dep, ls, le, employeeId);
            ReplaceLunchEvent(day, ls, le, employeeId);
            SplitAutoWorkAroundLunch(day, ls, le, employeeId);

            return takeMin / 60.0;
        }

        private void AdjustDaySettingsAfterChange(DateTime day, int employeeId = DefaultEmployeeId)
        {
            const double EPS = 1e-6;
            var resolved = GetResolvedWindow(day, employeeId);
            var movedOut = WorkTransferReportingService.GetMovedOut(day, employeeId);
            var prev = SettingsService.GetDaySettingsForDate(day, employeeId);

            bool IsSpecial(Event e) => e.EventType != EventType.Lunch && !IsWorkLike(e);
            bool IsCredited(Event e) => IsWorkLike(e) || IsSpecial(e);

            var evs = GetEventsForDay(employeeId, day)
                .Where(e => !e.IsDeleted)
                .OrderBy(e => e.StartTime)
                .ToList();

            var work = evs.Where(IsWorkLike).ToList();
            var specials = evs.Where(IsSpecial).ToList();
            var credited = evs.Where(IsCredited).ToList();

            if (movedOut > EPS && prev != null)
            {
                if (work.Count == 0 && specials.Count == 0)
                {
                    SettingsService.DeleteComputedDaySettingsForDate(day, employeeId);
                    ReplaceLunchEvent(day, TimeSpan.Zero, TimeSpan.Zero, employeeId);
                    return;
                }

                if (work.Count == 0 && specials.Count > 0)
                {
                    var arKeep = prev.ArrivalTime;
                    var deKeep = prev.DepartureTime;
                    SettingsService.SaveDaySettingsForDate(day, arKeep, deKeep, TimeSpan.Zero, TimeSpan.Zero, employeeId);
                    ReplaceLunchEvent(day, TimeSpan.Zero, TimeSpan.Zero, employeeId);
                    return;
                }

                var ar = credited.Min(e => e.StartTime).TimeOfDay;
                var de = credited.Max(e => e.EndTime).TimeOfDay;

                TimeSpan lS = TimeSpan.Zero, lE = TimeSpan.Zero;
                var lunch = evs.FirstOrDefault(e => e.EventType == EventType.Lunch);
                if (lunch != null)
                {
                    var tls = lunch.StartTime.TimeOfDay;
                    var tle = lunch.EndTime.TimeOfDay;
                    if (tle > tls && tls >= ar && tle <= de)
                    {
                        lS = tls;
                        lE = tle;
                    }
                }

                SettingsService.SaveDaySettingsForDate(day, ar, de, lS, lE, employeeId);
                ReplaceLunchEvent(day, lS, lE, employeeId);
                SplitAutoWorkAroundLunch(day, lS, lE, employeeId);
                return;
            }

            if (work.Count == 0 && specials.Count == 0)
            {
                SettingsService.DeleteDaySettingsForDate(day, employeeId);
                ReplaceLunchEvent(day, TimeSpan.Zero, TimeSpan.Zero, employeeId);
                return;
            }

            if (work.Count == 0 && specials.Count > 0)
            {
                var arKeep = prev?.ArrivalTime ?? resolved.ArrivalTime;
                var deKeep = prev?.DepartureTime ?? resolved.DepartureTime;

                SettingsService.SaveDaySettingsForDate(day, arKeep, deKeep, TimeSpan.Zero, TimeSpan.Zero, employeeId);
                ReplaceLunchEvent(day, TimeSpan.Zero, TimeSpan.Zero, employeeId);
                return;
            }

            var arr = credited.Min(e => e.StartTime).TimeOfDay;
            var dep = credited.Max(e => e.EndTime).TimeOfDay;

            TimeSpan ls = TimeSpan.Zero;
            TimeSpan le = TimeSpan.Zero;

            if (prev != null && prev.LunchEnd > prev.LunchStart)
            {
                if (prev.LunchStart >= arr && prev.LunchEnd <= dep)
                {
                    ls = prev.LunchStart;
                    le = prev.LunchEnd;
                }
            }
            else if (resolved.LunchEnd > resolved.LunchStart &&
                     resolved.LunchStart >= arr &&
                     resolved.LunchEnd <= dep)
            {
                ls = resolved.LunchStart;
                le = resolved.LunchEnd;
            }

            SettingsService.SaveDaySettingsForDate(day, arr, dep, ls, le, employeeId);
            ReplaceLunchEvent(day, ls, le, employeeId);
            SplitAutoWorkAroundLunch(day, ls, le, employeeId);
        }

        public async Task EnsureLunchInsideWorkWindowAsync(int employeeId, DateTime day, bool callRegenerate = true)
        {
            var resolved = GetResolvedWindow(day, employeeId);

            var arr = resolved.ArrivalTime;
            var dep = resolved.DepartureTime;

            if (dep <= arr)
                return;

            var desiredLen =
                resolved.LunchEnd > resolved.LunchStart
                    ? resolved.LunchEnd - resolved.LunchStart
                    : TimeSpan.FromMinutes(30);

            var maxLunchLen = ParseDurationOrDefault(resolved.MaxBreakDuration, TimeSpan.FromMinutes(30));
            if (desiredLen <= TimeSpan.Zero)
                desiredLen = TimeSpan.FromMinutes(30);

            if (desiredLen > maxLunchLen)
                desiredLen = maxLunchLen;

            int targetLunchCount = GetTargetLunchCount(dep - arr);

            using (var db = new AppDbContext())
            {
                var dayAutoLunches = db.Events
                    .Where(e =>
                        e.EmployeeId == employeeId &&
                        !e.IsDeleted &&
                        e.StartTime.Date == day.Date &&
                        e.EventType == EventType.Lunch &&
                        e.IsAutoGenerated)
                    .ToList();

                var invalidAutoLunches = dayAutoLunches
                    .Where(e =>
                        e.EndTime <= e.StartTime ||
                        e.StartTime.TimeOfDay < arr ||
                        e.EndTime.TimeOfDay > dep)
                    .ToList();

                if (invalidAutoLunches.Count > 0)
                {
                    db.Events.RemoveRange(invalidAutoLunches);
                    await db.SaveChangesAsync();
                }
            }

            var lunches = GetEventsForDay(employeeId, day)
                .Where(e => !e.IsDeleted && e.EventType == EventType.Lunch)
                .OrderBy(e => e.StartTime)
                .ToList();

            if (lunches.Count > targetLunchCount)
            {
                using var db = new AppDbContext();

                var extraAutoLunches = lunches
                    .Skip(targetLunchCount)
                    .Where(e => e.IsAutoGenerated)
                    .ToList();

                if (extraAutoLunches.Count > 0)
                {
                    db.Events.RemoveRange(extraAutoLunches);
                    await db.SaveChangesAsync();
                }
            }

            lunches = GetEventsForDay(employeeId, day)
                .Where(e => !e.IsDeleted && e.EventType == EventType.Lunch)
                .OrderBy(e => e.StartTime)
                .ToList();

            while (lunches.Count < targetLunchCount)
            {
                DateTime desiredStart =
                    lunches.Count == 0
                        ? Max(day + arr + TimeSpan.FromHours(4), day + resolved.LunchStart)
                        : lunches.Max(x => x.EndTime) + TimeSpan.FromHours(4);

                DateTime desiredEnd = desiredStart + desiredLen;

                if (desiredEnd > day + dep)
                    break;

                var blockers = GetEventsForDay(employeeId, day)
                    .Where(e => !e.IsDeleted && e.EventType != EventType.Lunch)
                    .OrderBy(e => e.StartTime)
                    .ToList();

                while (blockers.Any(e => desiredStart < e.EndTime && e.StartTime < desiredEnd))
                {
                    desiredStart = blockers
                        .Where(e => desiredStart < e.EndTime && e.StartTime < desiredEnd)
                        .Max(e => e.EndTime);

                    desiredEnd = desiredStart + desiredLen;

                    if (desiredEnd > day + dep)
                        break;
                }

                if (desiredEnd > day + dep)
                    break;

                CreateAutoEvent(new Event
                {
                    EmployeeId = employeeId,
                    Title = resolved.AutoEventNameLunch,
                    EventType = EventType.Lunch,
                    StartTime = desiredStart,
                    EndTime = desiredEnd,
                    IsAutoGenerated = true,
                    AutoGeneratedForDate = day
                });

                lunches = GetEventsForDay(employeeId, day)
                    .Where(e => !e.IsDeleted && e.EventType == EventType.Lunch)
                    .OrderBy(e => e.StartTime)
                    .ToList();
            }

            var firstLunch = lunches.OrderBy(e => e.StartTime).FirstOrDefault();
            var ls = firstLunch?.StartTime.TimeOfDay ?? TimeSpan.Zero;
            var le = firstLunch?.EndTime.TimeOfDay ?? TimeSpan.Zero;

            SettingsService.SaveDaySettingsForDate(day, arr, dep, ls, le, employeeId);
            SplitAutoWorkAroundLunch(day, ls, le, employeeId);
        }

        public async Task EnsureLunchInsideWorkWindowAsync(DateTime day, bool callRegenerate = true)
            => await EnsureLunchInsideWorkWindowAsync(DefaultEmployeeId, day, callRegenerate);

        public record ImportBatchInfo(string Id, string Label, DateTime RangeStart, DateTime RangeEnd, int EventsCount);

        public IEnumerable<ImportBatchInfo> GetImportBatches(int employeeId = DefaultEmployeeId)
        {
            using var db = new AppDbContext();

            var rows = db.Events
                .Where(e => e.EmployeeId == employeeId &&
                            !e.IsDeleted &&
                            e.ImportBatchId != null)
                .GroupJoin(
                    db.ImportBatches,
                    ev => ev.ImportBatchId,
                    batch => batch.Id,
                    (ev, batches) => new { ev, batch = batches.FirstOrDefault() })
                .GroupBy(x => new { x.ev.ImportBatchId, Label = x.batch != null ? x.batch.Label : null })
                .Select(g => new
                {
                    Id = g.Key.ImportBatchId!,
                    Label = g.Key.Label,
                    RangeStart = g.Min(x => x.ev.StartTime),
                    RangeEnd = g.Max(x => x.ev.EndTime),
                    EventsCount = g.Count()
                })
                .OrderByDescending(x => x.RangeStart)
                .AsNoTracking()
                .ToList();

            return rows.Select(x =>
                new ImportBatchInfo(
                    x.Id,
                    x.Label ?? "Načtení",
                    x.RangeStart.Date,
                    x.RangeEnd.Date,
                    x.EventsCount));
        }

        private async Task<double> TransferLockedOvertimeAsync(DateTime lockedDay, double overtimeHours, IEnumerable<DateTime> weekDays, int employeeId = DefaultEmployeeId)
        {
            const double EPS = 1e-6;
            int leftMin = RoundDownToQuantum(ToWholeMinutes(overtimeHours));
            if (leftMin < QUANTUM_MIN)
                return 0;

            var donors = weekDays
                .Select(d => d.Date)
                .Distinct()
                .Where(d => d != lockedDay.Date)
                .Where(d => CanTrimStart(d, employeeId) || CanTrimEnd(d, employeeId))
                .OrderBy(d => d)
                .ToList();

            if (donors.Count == 0)
                return 0;

            int donorCapacityMin = donors.Sum(d => GetEdgeAutoTrimCapacityMinutes(d, employeeId));

            if (donorCapacityMin < leftMin)
            {

                return 0;
            }

            var preferEnd = donors.ToDictionary(d => d, _ => true);
            var touched = new HashSet<DateTime>();
            int i = 0, guard = 0;

            while (leftMin >= QUANTUM_MIN && donors.Count > 0 && guard++ < 2000)
            {
                var d = donors[i % donors.Count];
                i++;

                double cut = 0;
                TransferEdge? usedEdge = null;

                if (preferEnd[d] && CanTrimEnd(d, employeeId))
                {
                    cut = TrimEndAuto(d, QMinutes(QUANTUM_MIN), employeeId);
                    if (cut > EPS) usedEdge = TransferEdge.End;
                }

                if (cut <= EPS && CanTrimStart(d, employeeId))
                {
                    cut = TrimStartAuto(d, QMinutes(QUANTUM_MIN), employeeId);
                    if (cut > EPS) usedEdge = TransferEdge.Start;
                }

                if (cut <= EPS && CanTrimEnd(d, employeeId))
                {
                    cut = TrimEndAuto(d, QMinutes(QUANTUM_MIN), employeeId);
                    if (cut > EPS) usedEdge = TransferEdge.End;
                }

                int gotMin = RoundDownToQuantum((int)Math.Round(cut * 60.0));
                if (gotMin <= 0 || usedEdge is null)
                {
                    if (!(CanTrimStart(d, employeeId) || CanTrimEnd(d, employeeId)))
                        donors.Remove(d);

                    continue;
                }

                leftMin -= gotMin;
                preferEnd[d] = !preferEnd[d];
                touched.Add(d);

                WorkTransferReportingService.AddTransfer(d, lockedDay, gotMin, usedEdge.Value, employeeId);
            }

            var gen = new AutomaticEventsGeneratorService(this, _ => Task.FromResult(false), employeeId);
            foreach (var d in touched.OrderBy(x => x))
            {
                await gen.RegenerateDailyEventsAsync(d, preserveUserSettings: true);
                await EnsureLunchInsideWorkWindowAsync(employeeId, d, callRegenerate: false);
            }

            MessageBus.Current.SendMessage(new AutoEventsGeneratedMessage());
            return (ToWholeMinutes(overtimeHours) - leftMin) / 60.0;
        }

        private async Task<double> TrimAutoSymmetricAsync(DateTime day, double hours, bool callRegenerate = true, int employeeId = DefaultEmployeeId)
        {
            int toCutMin = RoundDownToQuantum(ToWholeMinutes(hours));
            if (toCutMin <= 0)
                return 0;

            int cut = 0;

            while (toCutMin - cut >= QUANTUM_MIN && (CanTrimStart(day, employeeId) || CanTrimEnd(day, employeeId)))
            {
                int left = toCutMin - cut;

                int endWant = RoundUpToQuantum(left / 2);
                if (endWant > left) endWant = left;
                int startWant = left - endWant;

                double c1 = 0, c2 = 0;

                if (endWant > 0 && CanTrimEnd(day, employeeId))
                    c1 = TrimEndAuto(day, TimeSpan.FromMinutes(endWant), employeeId);

                if (startWant > 0 && CanTrimStart(day, employeeId))
                    c2 = TrimStartAuto(day, TimeSpan.FromMinutes(startWant), employeeId);

                int got = RoundDownToQuantum((int)Math.Round((c1 + c2) * 60.0));

                if (got <= 0)
                {
                    if (CanTrimEnd(day, employeeId))
                        got = RoundDownToQuantum((int)Math.Round(TrimEndAuto(day, QMinutes(QUANTUM_MIN), employeeId) * 60.0));

                    if (got <= 0 && CanTrimStart(day, employeeId))
                        got = RoundDownToQuantum((int)Math.Round(TrimStartAuto(day, QMinutes(QUANTUM_MIN), employeeId) * 60.0));

                    if (got <= 0)
                        break;
                }

                cut += got;
            }

            if (callRegenerate)
            {
                var gen = new AutomaticEventsGeneratorService(this, _ => Task.FromResult(false), employeeId);
                await gen.RegenerateDailyEventsAsync(day, preserveUserSettings: true);
            }

            await EnsureLunchInsideWorkWindowAsync(employeeId, day, callRegenerate: false);
            MessageBus.Current.SendMessage(new AutoEventsGeneratedMessage());

            return cut / 60.0;
        }

        public async Task TrimOvertimeByAutoBlocksAsync(int employeeId, DateTime day, bool preserveUserSettings = false)
        {
            var calc = new WorkingHoursCalculatorService();
            var all = GetEventsForDay(employeeId, day);
            var m = calc.DailyMetrics(day, all);
            var over = m.over;

            if (over <= 1e-6)
                return;

            await TrimAutoSymmetricAsync(day, over, callRegenerate: false, employeeId);
        }

        public async Task TrimOvertimeByAutoBlocksAsync(DateTime day, bool preserveUserSettings = false)
            => await TrimOvertimeByAutoBlocksAsync(DefaultEmployeeId, day, preserveUserSettings);

        private sealed class WeekDayMeta
        {
            public DateTime Day;
            public double Extra;
            public bool Locked;
            public bool CanStart;
            public bool CanEnd;
        }

        private async Task<PoolRestoreResult> RollbackSelfTrimPoolForWeekAsync(
            IReadOnlyList<DateTime> scopeDays,
            int employeeId = DefaultEmployeeId)
        {
            var calc = new WorkingHoursCalculatorService();

            int weekUnderMin = scopeDays
                .Select(d => d.Date)
                .Distinct()
                .Sum(d => RoundDownToQuantum(
                    ToWholeMinutes(calc.DailyMetrics(d, GetEventsForDay(employeeId, d)).under)));

            weekUnderMin = RoundDownToQuantum(weekUnderMin);
            if (weekUnderMin < QUANTUM_MIN)
                return new PoolRestoreResult();

            var entries = SelfTrimStore.GetScopeSnapshot(employeeId, scopeDays)
                .OrderByDescending(x => x.Minutes)
                .ThenBy(x => x.Day)
                .ThenBy(x => x.Edge == TransferEdge.End ? 0 : 1)
                .ToList();

            if (entries.Count == 0)
                return new PoolRestoreResult();

            int left = weekUnderMin;
            int restored = 0;
            var touched = new HashSet<DateTime>();
            var restoredByDay = new Dictionary<DateTime, int>();

            foreach (var entry in entries)
            {
                if (left < QUANTUM_MIN)
                    break;

                int want = Math.Min(left, entry.Minutes);
                want = RoundDownToQuantum(want);

                if (want < QUANTUM_MIN)
                    continue;

                var putBackHours = RestoreTransferredMinutes(entry.Day, want, entry.Edge, employeeId);
                int putBackMin = RoundDownToQuantum((int)Math.Round(putBackHours * 60.0));

                if (putBackMin < QUANTUM_MIN)
                    continue;

                restored += putBackMin;
                left -= putBackMin;
                touched.Add(entry.Day.Date);
                AddRestoredMinutes(restoredByDay, entry.Day, putBackMin);

                SelfTrimStore.Consume(employeeId, entry.Day, entry.Edge, putBackMin);
            }

            if (restored >= QUANTUM_MIN)
            {
                var gen = new AutomaticEventsGeneratorService(this, _ => Task.FromResult(false), employeeId);

                foreach (var d in touched.OrderBy(x => x))
                {
                    await gen.RegenerateDailyEventsAsync(d, preserveUserSettings: true);
                    await EnsureLunchInsideWorkWindowAsync(employeeId, d, callRegenerate: false);
                }

                MessageBus.Current.SendMessage(new AutoEventsGeneratedMessage());
            }

            return new PoolRestoreResult
            {
                RestoredMinutes = restored,
                TouchedDays = touched,
                MinutesByDay = restoredByDay
            };
        }

        private List<DateTime> GetMonthPartialScopeDays(int year, int month)
        {
            var monthDays = GetMonthWorkdays(year, month);
            if (monthDays.Count == 0)
                return new List<DateTime>();

            var orderedMonthGroups = monthDays
                .GroupBy(GetIsoWeekKey)
                .Select(g => g.OrderBy(d => d).ToList())
                .Where(g => g.Count > 0)
                .OrderBy(g => g.First())
                .ToList();

            var partialGroups = orderedMonthGroups
                .Where(g => IsPartialMonthWeek(GetIsoWeekKey(g[0]), month))
                .ToList();

            if (partialGroups.Count == 0)
                return new List<DateTime>();

            if (partialGroups.Count >= 2)
            {
                return partialGroups
                    .SelectMany(g => g)
                    .Select(d => d.Date)
                    .Distinct()
                    .OrderBy(d => d)
                    .ToList();
            }

            var fallbackScope = BuildSinglePartialFallbackScope(
                orderedMonthGroups,
                partialGroups[0],
                month);

            if (fallbackScope != null && fallbackScope.Count >= 2)
            {
                return fallbackScope
                    .Select(d => d.Date)
                    .Distinct()
                    .OrderBy(d => d)
                    .ToList();
            }

            return partialGroups[0]
                .Select(d => d.Date)
                .Distinct()
                .OrderBy(d => d)
                .ToList();
        }

        private bool ShouldRebalanceMonthPartialScope(int year, int month, IEnumerable<DateTime> changedDays)
        {
            var changed = changedDays
                .Select(d => d.Date)
                .Distinct()
                .ToHashSet();

            var partialScopeDays = GetMonthPartialScopeDays(year, month);

            if (partialScopeDays.Count == 0)
                return false;

            return partialScopeDays.Any(changed.Contains);
        }

        private async Task BalanceOneWeekAsync(List<DateTime> weekDays, int employeeId = DefaultEmployeeId)
        {
            var scopeDays = weekDays
                .Select(d => d.Date)
                .Distinct()
                .OrderBy(d => d)
                .ToList();


            const double EPS = 1e-6;
            var calc = new WorkingHoursCalculatorService();

            async Task RebuildTouchedDaysAsync(IEnumerable<DateTime> days)
            {
                var touchedDays = days
                    .Select(x => x.Date)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                if (touchedDays.Count == 0)
                    return;

                var genLocal = new AutomaticEventsGeneratorService(this, _ => Task.FromResult(false), employeeId);

                foreach (var d in touchedDays)
                {
                    await genLocal.RegenerateDailyEventsAsync(d, preserveUserSettings: true);
                    await EnsureLunchInsideWorkWindowAsync(employeeId, d, callRegenerate: false);
                }
            }

            Dictionary<DateTime, int> BuildActualExtraMinByDay()
                => scopeDays.ToDictionary(
                    d => d,
                    d => RoundDownToQuantum(ToWholeMinutes(
                        calc.DailyMetrics(d, GetEventsForDay(employeeId, d)).over)));

            Dictionary<DateTime, int> BuildActualUnderMinByDay()
                => scopeDays.ToDictionary(
                    d => d,
                    d => RoundDownToQuantum(ToWholeMinutes(
                        calc.DailyMetrics(d, GetEventsForDay(employeeId, d)).under)));

            int GetActualWeekNetMin()
                => BuildActualExtraMinByDay().Values.Sum() - BuildActualUnderMinByDay().Values.Sum();

            double CutFairOneStep(DateTime day, int wantedMin)
            {
                double cut = 0.0;
                bool canS = CanTrimStart(day, employeeId);
                bool canE = CanTrimEnd(day, employeeId);

                if (!canS && !canE)
                    return 0.0;

                wantedMin = RoundDownToQuantum(wantedMin);
                if (wantedMin < QUANTUM_MIN)
                    return 0.0;

                if (wantedMin >= 60 && canS && canE)
                {
                    cut += TrimStartAuto(day, 0.5, employeeId);
                    cut += TrimEndAuto(day, 0.5, employeeId);

                    var left = (wantedMin / 60.0) - cut;
                    if (left > EPS)
                    {
                        if (CanTrimEnd(day, employeeId))
                            cut += TrimEndAuto(day, left, employeeId);
                        else if (CanTrimStart(day, employeeId))
                            cut += TrimStartAuto(day, left, employeeId);
                    }
                }
                else
                {
                    double reqHours = wantedMin / 60.0;

                    if (canE)
                        cut += TrimEndAuto(day, reqHours, employeeId);
                    else if (canS)
                        cut += TrimStartAuto(day, reqHours, employeeId);
                }

                return cut;
            }

            var meta = scopeDays.ToDictionary(d => d, d =>
            {
                var m = calc.DailyMetrics(d, GetEventsForDay(employeeId, d));

                return new WeekDayMeta
                {
                    Day = d,
                    Extra = m.over,
                    Locked = IsLockedEdgeDay(d, employeeId),
                    CanStart = CanTrimStart(d, employeeId),
                    CanEnd = CanTrimEnd(d, employeeId)
                };
            });

            var restoredCarryMinByDay = scopeDays.ToDictionary(d => d, _ => 0);

            foreach (var d in scopeDays.OrderBy(x => x))
            {
                var movedIn = WorkTransferReportingService.GetMovedIn(d, employeeId);
                if (movedIn <= EPS)
                    continue;

                var actual = meta[d];
                int needBackMin = RoundDownToQuantum((int)Math.Round((movedIn - actual.Extra) * 60.0));

                if (needBackMin < QUANTUM_MIN)
                    continue;         

                var rb = await RollbackTransfersForLockedDayAsync(d, needBackMin, employeeId);
                if (rb.RestoredMinutes > 0)
                {
                    MergeRestoredMinutes(restoredCarryMinByDay, rb.MinutesByDay);
                    RefreshWeekMeta(meta, scopeDays, calc, employeeId);
                }
            }

            var selfTrimRollback = await RollbackSelfTrimPoolForWeekAsync(scopeDays, employeeId);
            if (selfTrimRollback.RestoredMinutes >= QUANTUM_MIN)
            {
                MergeRestoredMinutes(restoredCarryMinByDay, selfTrimRollback.MinutesByDay);
                RefreshWeekMeta(meta, scopeDays, calc, employeeId);
            }

            var transferRollback = await RollbackTransferPoolForScopeAsync(scopeDays, employeeId);
            if (transferRollback.RestoredMinutes >= QUANTUM_MIN)
            {
                MergeRestoredMinutes(restoredCarryMinByDay, transferRollback.MinutesByDay);
                RefreshWeekMeta(meta, scopeDays, calc, employeeId);
            }

            RefreshWeekMeta(meta, scopeDays, calc, employeeId);

            var rawExtraMinByDay = scopeDays.ToDictionary(
                d => d,
                d => RoundDownToQuantum(ToWholeMinutes(meta[d].Extra)));

            var underMinByDay = scopeDays.ToDictionary(
                d => d,
                d => RoundDownToQuantum(ToWholeMinutes(
                    calc.DailyMetrics(d, GetEventsForDay(employeeId, d)).under)));

            var effectiveExtraMinByDay = scopeDays.ToDictionary(
                d => d,
                d => Math.Max(0, rawExtraMinByDay[d] - restoredCarryMinByDay[d]));

            int totalRawExtraMin = rawExtraMinByDay.Values.Sum();
            int totalUnderMin = underMinByDay.Values.Sum();
            int totalRestoredCarryMin = restoredCarryMinByDay.Values.Sum();
            int totalEffectiveExtraMin = effectiveExtraMinByDay.Values.Sum();

            int weekNetMin = totalEffectiveExtraMin - totalUnderMin;

            var extraBudgetMinByDay = scopeDays.ToDictionary(
                d => d,
                d => effectiveExtraMinByDay[d]);

            if (Math.Abs(weekNetMin) >= QUANTUM_MIN)
            {
                if (weekNetMin > 0)
                {
                    int underAbsorbPoolMin = totalUnderMin;

                    foreach (var d in scopeDays
                        .OrderByDescending(d => meta[d].Locked)
                        .ThenBy(d => d))
                    {
                        if (underAbsorbPoolMin < QUANTUM_MIN)
                            break;

                        int budget = extraBudgetMinByDay[d];
                        if (budget < QUANTUM_MIN)
                            continue;

                        int absorb = Math.Min(budget, underAbsorbPoolMin);
                        absorb = RoundDownToQuantum(absorb);

                        if (absorb < QUANTUM_MIN)
                            continue;

                        extraBudgetMinByDay[d] -= absorb;
                        underAbsorbPoolMin -= absorb;

                    }

                    int availableTrimBudgetMin = extraBudgetMinByDay.Values.Sum();
                    int toTrimWeekMin = Math.Min(availableTrimBudgetMin, weekNetMin);
                    toTrimWeekMin = RoundDownToQuantum(toTrimWeekMin);

                    var touched = new HashSet<DateTime>();

                    using (var _track = new SelfTrimTrackingScope())
                    {
                        int trimmedWeekMin = 0;
                        bool progress = true;

                        while (progress && trimmedWeekMin + QUANTUM_MIN <= toTrimWeekMin)
                        {
                            progress = false;

                            foreach (var d in scopeDays.OrderBy(x => x))
                            {
                                int dayBudgetMin = extraBudgetMinByDay[d];
                                if (dayBudgetMin < QUANTUM_MIN)
                                    continue;

                                if (!(CanTrimStart(d, employeeId) || CanTrimEnd(d, employeeId)))
                                    continue;

                                int leftWeekMin = toTrimWeekMin - trimmedWeekMin;
                                if (leftWeekMin < QUANTUM_MIN)
                                    break;

                                int planMin = Math.Min(60, Math.Min(dayBudgetMin, leftWeekMin));
                                planMin = RoundDownToQuantum(planMin);

                                if (planMin < QUANTUM_MIN)
                                    continue;

                                var cut = CutFairOneStep(d, planMin);
                                int cutMin = RoundDownToQuantum(ToWholeMinutes(cut));

                                if (cutMin >= QUANTUM_MIN)
                                {
                                    extraBudgetMinByDay[d] = Math.Max(0, extraBudgetMinByDay[d] - cutMin);
                                    trimmedWeekMin += cutMin;
                                    progress = true;
                                    touched.Add(d);
                                }
                            }
                        }
                    }

                    await RebuildTouchedDaysAsync(touched);

                    foreach (var d in scopeDays.Where(x => meta[x].Locked).OrderBy(x => x))
                    {
                        int lockedRemainingMin = extraBudgetMinByDay[d];
                        if (lockedRemainingMin < QUANTUM_MIN)
                            continue;

                        var transferred = await TransferLockedOvertimeAsync(d, lockedRemainingMin / 60.0, scopeDays, employeeId);
                        int transferredMin = RoundDownToQuantum(ToWholeMinutes(transferred));

                        if (transferredMin >= QUANTUM_MIN)
                            extraBudgetMinByDay[d] = Math.Max(0, extraBudgetMinByDay[d] - transferredMin);
                    }
                }
                else
                {
                    int needRestoreWeekMin = Math.Abs(weekNetMin);

                    await FillWeeklyUnderworkAsync(
                        scopeDays,
                        employeeId,
                        skipDays: null,
                        maxRestoreMinutes: needRestoreWeekMin);
                }
            }

            for (int settlePass = 1; settlePass <= 3; settlePass++)
            {
                RefreshWeekMeta(meta, scopeDays, calc, employeeId);

                var actualExtraMinByDay = BuildActualExtraMinByDay();
                var actualUnderMinByDay = BuildActualUnderMinByDay();
                int actualWeekNetMin = actualExtraMinByDay.Values.Sum() - actualUnderMinByDay.Values.Sum();

                if (Math.Abs(actualWeekNetMin) < QUANTUM_MIN)
                    break;

                bool anyProgress = false;

                if (actualWeekNetMin > 0)
                {
                    var settleTouched = new HashSet<DateTime>();

                    using (var _track = new SelfTrimTrackingScope())
                    {
                        foreach (var d in scopeDays.OrderBy(x => x))
                        {
                            bool locked = IsLockedEdgeDay(d, employeeId);
                            if (locked)
                                continue;

                            int dayExtraMin = actualExtraMinByDay[d];
                            if (dayExtraMin < QUANTUM_MIN)
                                continue;

                            if (!(CanTrimStart(d, employeeId) || CanTrimEnd(d, employeeId)))
                                continue;

                            int wantedMin = Math.Min(60, Math.Min(dayExtraMin, actualWeekNetMin));
                            wantedMin = RoundDownToQuantum(wantedMin);

                            if (wantedMin < QUANTUM_MIN)
                                continue;

                            var cut = CutFairOneStep(d, wantedMin);
                            int cutMin = RoundDownToQuantum(ToWholeMinutes(cut));

                            if (cutMin >= QUANTUM_MIN)
                            {
                                actualWeekNetMin -= cutMin;
                                settleTouched.Add(d);
                                anyProgress = true;

                                if (actualWeekNetMin < QUANTUM_MIN)
                                    break;
                            }
                        }
                    }

                    await RebuildTouchedDaysAsync(settleTouched);

                    actualExtraMinByDay = BuildActualExtraMinByDay();
                    actualUnderMinByDay = BuildActualUnderMinByDay();
                    actualWeekNetMin = actualExtraMinByDay.Values.Sum() - actualUnderMinByDay.Values.Sum();

                    if (actualWeekNetMin >= QUANTUM_MIN)
                    {
                        foreach (var d in scopeDays.OrderBy(x => x))
                        {
                            bool locked = IsLockedEdgeDay(d, employeeId);
                            if (!locked)
                                continue;

                            int lockedExtraMin = actualExtraMinByDay[d];
                            if (lockedExtraMin < QUANTUM_MIN)
                                continue;

                            var transferred = await TransferLockedOvertimeAsync(d, lockedExtraMin / 60.0, scopeDays, employeeId);
                            int transferredMin = RoundDownToQuantum(ToWholeMinutes(transferred));

                            if (transferredMin >= QUANTUM_MIN)
                                anyProgress = true;

                            actualExtraMinByDay = BuildActualExtraMinByDay();
                            actualUnderMinByDay = BuildActualUnderMinByDay();
                            actualWeekNetMin = actualExtraMinByDay.Values.Sum() - actualUnderMinByDay.Values.Sum();

                            if (actualWeekNetMin < QUANTUM_MIN)
                                break;
                        }
                    }
                }
                else
                {
                    int needRestoreWeekMin = Math.Abs(actualWeekNetMin);

                    await FillWeeklyUnderworkAsync(
                        scopeDays,
                        employeeId,
                        skipDays: null,
                        maxRestoreMinutes: needRestoreWeekMin);

                    int afterRestoreNetMin = GetActualWeekNetMin();
                    anyProgress = Math.Abs(afterRestoreNetMin) < Math.Abs(actualWeekNetMin);
                }

                if (!anyProgress)
                {
                    break;
                }
            }

            foreach (var d in scopeDays)
                ReconcileSelfTrimPoolForDay(d, employeeId);

            var totalExtraAfter = scopeDays
                .Sum(d => calc.DailyMetrics(d, GetEventsForDay(employeeId, d)).over);

            var totalUnderAfter = scopeDays
                .Sum(d => calc.DailyMetrics(d, GetEventsForDay(employeeId, d)).under);

        }

        private void ResetBalancingArtifactsForScope(IEnumerable<DateTime> scopeDays, int employeeId)
        {
            var days = scopeDays
                .Select(d => d.Date)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            if (days.Count == 0)
                return;

            SelfTrimStore.ClearDates(employeeId, days);

            var transfers = days
                .SelectMany(d =>
                    WorkTransferReportingService.GetTransfersFrom(d, employeeId)
                        .Concat(WorkTransferReportingService.GetTransfersTo(d, employeeId)))
                .GroupBy(x => new
                {
                    From = x.FromDay.Date,
                    To = x.ToDay.Date,
                    x.Edge,
                    x.Minutes
                })
                .Select(g => g.First())
                .ToList();

            foreach (var tr in transfers)
                WorkTransferReportingService.RemoveEntry(tr);
        }

        private string BuildBalanceFingerprint(IEnumerable<DateTime> scopeDays, int employeeId)
        {
            var days = scopeDays
                .Select(d => d.Date)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            var sb = new StringBuilder();

            foreach (var day in days)
            {
                sb.AppendLine($"DAY|{day:yyyy-MM-dd}");

                var evs = GetEventsForDay(employeeId, day)
                    .Where(e => !e.IsDeleted)
                    .OrderBy(e => e.StartTime)
                    .ThenBy(e => e.EndTime)
                    .ThenBy(e => e.EventType)
                    .ThenBy(e => e.IsAutoGenerated)
                    .ThenBy(e => e.Title ?? string.Empty)
                    .ToList();

                foreach (var e in evs)
                {
                    sb.AppendLine(
                        $"EV|{e.EventType}|{e.IsAutoGenerated}|{e.StartTime:O}|{e.EndTime:O}|{e.ParentEventId}|{e.AutoGeneratedForDate:yyyy-MM-dd}|{e.Title}|{e.ImportBatchId}");
                }

                var ds = SettingsService.GetDaySettingsForDate(day, employeeId);
                if (ds == null)
                    sb.AppendLine("SET|null");
                else
                    sb.AppendLine($"SET|{ds.ArrivalTime}|{ds.DepartureTime}|{ds.LunchStart}|{ds.LunchEnd}");

                sb.AppendLine($"SELF|{SelfTrimStore.GetTotal(employeeId, day)}");

                foreach (var tr in WorkTransferReportingService.GetTransfersFrom(day, employeeId)
                             .OrderBy(x => x.ToDay)
                             .ThenBy(x => x.Edge)
                             .ThenBy(x => x.Minutes))
                {
                    sb.AppendLine($"TFROM|{tr.FromDay:yyyy-MM-dd}|{tr.ToDay:yyyy-MM-dd}|{tr.Edge}|{tr.Minutes}");
                }

                foreach (var tr in WorkTransferReportingService.GetTransfersTo(day, employeeId)
                             .OrderBy(x => x.FromDay)
                             .ThenBy(x => x.Edge)
                             .ThenBy(x => x.Minutes))
                {
                    sb.AppendLine($"TTO|{tr.FromDay:yyyy-MM-dd}|{tr.ToDay:yyyy-MM-dd}|{tr.Edge}|{tr.Minutes}");
                }
            }

            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash);
        }

        private List<DateTime> GetRelatedRebalanceScopeDays(List<DateTime> weekDays)
        {
            var result = weekDays
                .Select(d => d.Date)
                .Distinct()
                .ToHashSet();

            var months = result
                .Select(d => (d.Year, d.Month))
                .Distinct()
                .ToList();

            foreach (var m in months)
            {
                if (!ShouldRebalanceMonthPartialScope(m.Year, m.Month, result))
                    continue;

                foreach (var d in GetMonthPartialScopeDays(m.Year, m.Month))
                    result.Add(d.Date);
            }

            return result
                .OrderBy(d => d)
                .ToList();
        }

        private async Task BalanceWeekPipelineUntilStableAsync(List<DateTime> weekDays, int employeeId)
        {
            var baseWeekDays = weekDays
                .Select(d => d.Date)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            if (baseWeekDays.Count == 0)
                return;

            var fullScopeDays = GetRelatedRebalanceScopeDays(baseWeekDays);

            var affectedMonths = baseWeekDays
                .Select(d => (d.Year, d.Month))
                .Distinct()
                .OrderBy(x => x.Year)
                .ThenBy(x => x.Month)
                .ToList();

            var seenFingerprints = new HashSet<string>(StringComparer.Ordinal);

            for (int pass = 1; pass <= 4; pass++)
            {            
                WorkTransferReportingService.ResetWeek(baseWeekDays);
                ResetBalancingArtifactsForScope(baseWeekDays, employeeId);

                await BalanceOneWeekAsync(baseWeekDays, employeeId);

                foreach (var m in affectedMonths)
                {
                    if (!ShouldRebalanceMonthPartialScope(m.Year, m.Month, baseWeekDays))
                    {                       
                        continue;
                    }

                    var partialScope = GetMonthPartialScopeDays(m.Year, m.Month);
                    if (partialScope.Count == 0)
                        continue;

                    WorkTransferReportingService.ResetWeek(partialScope);
                    ResetBalancingArtifactsForScope(partialScope, employeeId);

                    await BalanceMonthPartialScopesAsync(m.Year, m.Month, employeeId);
                }

                foreach (var m in affectedMonths)
                    await PostNormalizeMonthAsync(m.Year, m.Month, employeeId);

                foreach (var d in fullScopeDays)
                    ReconcileSelfTrimPoolForDay(d, employeeId);

                var fp = BuildBalanceFingerprint(fullScopeDays, employeeId);

                if (!seenFingerprints.Add(fp))
                {                  
                    break;
                }
            }

            SaveBalancedWeeksForDates(fullScopeDays, employeeId);
        }

        private async Task BalanceMonthPipelineUntilStableAsync(int year, int month, int employeeId)
        {
            var monthDays = GetMonthWorkdays(year, month)
                .Select(d => d.Date)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            if (monthDays.Count == 0)
                return;

            var fullScopeDays = monthDays
                .Concat(GetMonthPartialScopeDays(year, month))
                .Select(d => d.Date)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            var seenFingerprints = new HashSet<string>(StringComparer.Ordinal);

            for (int pass = 1; pass <= 4; pass++)
            {
                WorkTransferReportingService.ResetWeek(monthDays);
                ResetBalancingArtifactsForScope(monthDays, employeeId);

                var weekGroups = monthDays
                    .GroupBy(GetIsoWeekKey)
                    .OrderBy(g => g.Key.IsoYear)
                    .ThenBy(g => g.Key.IsoWeek)
                    .ToList();

                foreach (var g in weekGroups)
                    await BalanceOneWeekAsync(g.OrderBy(d => d).ToList(), employeeId);

                if (ShouldRebalanceMonthPartialScope(year, month, monthDays))
                {
                    var partialScope = GetMonthPartialScopeDays(year, month);
                    if (partialScope.Count > 0)
                    {
                        WorkTransferReportingService.ResetWeek(partialScope);
                        ResetBalancingArtifactsForScope(partialScope, employeeId);

                        await BalanceMonthPartialScopesAsync(year, month, employeeId);
                    }
                }

                await PostNormalizeMonthAsync(year, month, employeeId);

                foreach (var d in fullScopeDays)
                    ReconcileSelfTrimPoolForDay(d, employeeId);

                var fp = BuildBalanceFingerprint(fullScopeDays, employeeId);

                if (!seenFingerprints.Add(fp))
                {
                    break;
                }
            }

            SaveBalancedWeeksForDates(fullScopeDays, employeeId);
        }

        public async Task BalanceEventsForMonthAsync(int year, int month, Func<string, Task<bool>> askCollision, int employeeId = DefaultEmployeeId)
        {
            var monthDays = GetMonthWorkdays(year, month);
            if (monthDays.Count == 0)
                return;

            await BalanceMonthPipelineUntilStableAsync(year, month, employeeId);
        }

        public async Task BalanceWeekForDateAsync(DateTime anyDate, int employeeId = DefaultEmployeeId)
        {
            if (_isBalancingNow)
            {
                return;
            }

            try
            {
                _isBalancingNow = true;

                var days = GetWeekWorkdays(anyDate)
                    .Select(d => d.Date)
                    .ToList();

                if (days.Count == 0)
                {
                    return;
                }

                await BalanceWeekPipelineUntilStableAsync(days, employeeId);

            }
            finally
            {
                _isBalancingNow = false;
            }
        }

        public async Task BalanceForChangedRangeAsync(DateTime startIncl, DateTime endIncl, int employeeId = DefaultEmployeeId)
        {
            if (_isBalancingNow)
            {           
                return;
            }

            try
            {
                _isBalancingNow = true;

                var changedDays = Enumerable.Range(0, (endIncl.Date - startIncl.Date).Days + 1)
                    .Select(i => startIncl.Date.AddDays(i))
                    .Where(IsWorkday)
                    .Select(d => d.Date)
                    .ToList();

                if (changedDays.Count == 0)
                    return;

                var touchedWeeks = changedDays
                    .Select(d => new
                    {
                        AnyDay = d,
                        IsoYear = System.Globalization.ISOWeek.GetYear(d),
                        IsoWeek = System.Globalization.ISOWeek.GetWeekOfYear(d)
                    })
                    .GroupBy(x => (x.IsoYear, x.IsoWeek))
                    .Select(g => g.First().AnyDay)
                    .ToList();

                foreach (var anyDay in touchedWeeks)
                {
                    var weekDays = GetWeekWorkdays(anyDay)
                        .Select(d => d.Date)
                        .ToList();

                    var isoYear = System.Globalization.ISOWeek.GetYear(anyDay);
                    var isoWeek = System.Globalization.ISOWeek.GetWeekOfYear(anyDay);

                    await BalanceWeekPipelineUntilStableAsync(weekDays, employeeId);
                }
            }
            finally
            {
                _isBalancingNow = false;
            }
        }

        private async Task PostNormalizeMonthAsync(int year, int month, int employeeId = DefaultEmployeeId)
        {
            const double EPS = 1e-6;
            var calc = new WorkingHoursCalculatorService();

            var first = new DateTime(year, month, 1);
            var last = new DateTime(year, month, DateTime.DaysInMonth(year, month));

            var monthDays = Enumerable.Range(0, (last - first).Days + 1)
                .Select(i => first.AddDays(i))
                .Where(d => d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday && !HolidayHelper.IsCzechHoliday(d))
                .ToList();

            foreach (var day in monthDays)
            {
                var all = GetEventsForDay(employeeId, day);
                var m = calc.DailyMetrics(day, all);

                double total = m.worked;
                bool hasLunch = all.Any(e => e.EventType == EventType.Lunch &&
                                             !e.IsDeleted &&
                                             (e.EndTime - e.StartTime) >= TimeSpan.FromMinutes(5));

                bool hasAnyCredited = all.Any(e => !e.IsDeleted && (IsWorkLike(e) || (e.EventType != EventType.Lunch && e.EventType != EventType.Work && e.EventType != EventType.BusinessTrip)));

                bool canStart = CanExtendStart(day, employeeId);
                bool canEnd = CanExtendEnd(day, employeeId);
                var movedOut = WorkTransferReportingService.GetMovedOut(day, employeeId);
                var movedIn = WorkTransferReportingService.GetMovedIn(day, employeeId);

                if (movedOut > EPS || movedIn > EPS || m.over > EPS || m.under > EPS)
                {
                    ReconcileSelfTrimPoolForDay(day, employeeId);
                    continue;
                }

                if (total < 8.0 - EPS && (canStart || canEnd) && movedOut <= EPS && movedIn <= EPS)
                {
                    double need = 8.0 - total;
                    double placed = RestoreUnderworkSmart(day, need, employeeId);

                    var gen = new AutomaticEventsGeneratorService(this, _ => Task.FromResult(false), employeeId);
                    await gen.RegenerateDailyEventsAsync(day);
                    await EnsureLunchInsideWorkWindowAsync(employeeId, day);

                    all = GetEventsForDay(employeeId, day);
                    m = calc.DailyMetrics(day, all);
                    total = m.worked;
                }

                if (!hasLunch && hasAnyCredited && (canStart || canEnd) && movedOut <= EPS && movedIn <= EPS)
                {
                    var resolved = GetResolvedWindow(day, employeeId);
                    var arr = resolved.ArrivalTime;
                    var dep = resolved.DepartureTime;
                    var desiredLen = resolved.LunchEnd > resolved.LunchStart
                        ? resolved.LunchEnd - resolved.LunchStart
                        : TimeSpan.FromMinutes(30);

                    var (ls, le) = RefitLunch(day, arr, dep, resolved.LunchStart, resolved.LunchEnd, desiredLen, employeeId);

                    if (le <= ls)
                    {
                        if (!TryCarveLunchFromInnerAuto(day, arr, dep, desiredLen, out ls, out le, employeeId))
                        {
                            if (canEnd)
                            {
                                ExtendEndAuto(day, desiredLen.TotalHours, employeeId);
                                var us2 = GetResolvedWindow(day, employeeId);
                                dep = us2.DepartureTime;
                                (ls, le) = RefitLunch(day, arr, dep, resolved.LunchStart, resolved.LunchEnd, desiredLen, employeeId);
                            }
                            else if (canStart)
                            {
                                ExtendStartAuto(day, desiredLen.TotalHours, employeeId);
                                var us2 = GetResolvedWindow(day, employeeId);
                                arr = us2.ArrivalTime;
                                (ls, le) = RefitLunch(day, arr, dep, resolved.LunchStart, resolved.LunchEnd, desiredLen, employeeId);
                            }
                        }
                    }

                    if (le > ls)
                    {
                        SettingsService.SaveDaySettingsForDate(day, arr, dep, ls, le, employeeId);
                        ReplaceLunchEvent(day, ls, le, employeeId);

                        await EnsureLunchInsideWorkWindowAsync(employeeId, day);

                        all = GetEventsForDay(employeeId, day);
                        m = calc.DailyMetrics(day, all);
                        total = m.worked;

                        if (total < 8.0 - EPS)
                        {
                            double need = 8.0 - total;
                            double placed = RestoreUnderworkSmart(day, need, employeeId);

                            var gen2 = new AutomaticEventsGeneratorService(this, _ => Task.FromResult(false), employeeId);
                            await gen2.RegenerateDailyEventsAsync(day);
                            await EnsureLunchInsideWorkWindowAsync(employeeId, day);
                        }
                    }
                }
            }
        }

        private void InvalidateWeeksForDates(IEnumerable<DateTime> dates, int employeeId)
        {
            if (_isBalancingNow)
                return;

            var weeks = dates
                .Select(d => (Year: System.Globalization.ISOWeek.GetYear(d),
                              Week: System.Globalization.ISOWeek.GetWeekOfYear(d)))
                .Distinct()
                .ToList();

            foreach (var w in weeks)
                Helpers.WeekBalanceStore.Invalidate(employeeId, w.Year, w.Week);
        }

        public async Task CreateImportBatchAsync(ImportBatch batch)
        {
            await using var db = new AppDbContext();
            db.ImportBatches.Add(batch);
            await db.SaveChangesAsync();
        }

        private bool MustSplitManualEightHourWork(Event ev)
        {
            if (ev.IsAutoGenerated)
                return false;

            if (ev.ImportBatchId != null)
                return false;

            if (ev.EventType != EventType.Work)
                return false;

            if (ev.AllDay)
                return false;

            if (ev.StartTime.Date != ev.EndTime.Date)
                return false;

            var duration = ev.EndTime - ev.StartTime;
            return duration >= TimeSpan.FromHours(8);
        }

        private void SaveBalancedWeeksForDates(IEnumerable<DateTime> dates, int employeeId)
        {
            var weekKeys = dates
                .Select(d => new
                {
                    AnyDay = d.Date,
                    ISOYear = System.Globalization.ISOWeek.GetYear(d),
                    ISOWeek = System.Globalization.ISOWeek.GetWeekOfYear(d)
                })
                .GroupBy(x => (x.ISOYear, x.ISOWeek))
                .Select(g => g.First())
                .ToList();

            foreach (var w in weekKeys)
            {
                var fp = Helpers.BalanceFingerprint.ForWeek(this, employeeId, w.AnyDay);

                Helpers.WeekBalanceStore.Save(employeeId, w.ISOYear, w.ISOWeek, fp);
            }
        }

        private async Task FillWeeklyUnderworkAsync(
            List<DateTime> weekDays,
            int employeeId = DefaultEmployeeId,
            ISet<DateTime>? skipDays = null,
            int maxRestoreMinutes = int.MaxValue)
        {
            const double EPS = 1e-6;
            var calc = new WorkingHoursCalculatorService();
            var touched = new HashSet<DateTime>();

            var skip = skipDays?.Select(d => d.Date).ToHashSet()
                       ?? new HashSet<DateTime>();

            int leftToRestoreMin = maxRestoreMinutes == int.MaxValue
                ? int.MaxValue
                : RoundDownToQuantum(maxRestoreMinutes);

            foreach (var rawDay in weekDays.OrderBy(d => d))
            {
                var day = rawDay.Date;

                if (leftToRestoreMin != int.MaxValue && leftToRestoreMin < QUANTUM_MIN)
                    break;

                if (skip.Contains(day))
                {
                    continue;
                }

                var metrics = calc.DailyMetrics(day, GetEventsForDay(employeeId, day));
                int underMin = RoundDownToQuantum(ToWholeMinutes(metrics.under));

                bool canStart = CanExtendStart(day, employeeId);
                bool canEnd = CanExtendEnd(day, employeeId);            

                if (underMin < QUANTUM_MIN)
                    continue;

                if (leftToRestoreMin != int.MaxValue)
                {
                    underMin = Math.Min(underMin, leftToRestoreMin);
                    underMin = RoundDownToQuantum(underMin);

                    if (underMin < QUANTUM_MIN)
                        continue;
                }

                bool hasOutgoingTransfers = WorkTransferReportingService.GetTransfersFrom(day, employeeId).Any();
                bool hasIncomingTransfers = WorkTransferReportingService.GetTransfersTo(day, employeeId).Any();

                if (hasOutgoingTransfers && !hasIncomingTransfers)
                {
                    continue;
                }

                double placed = RestoreUnderworkSmart(day, underMin / 60.0, employeeId);
                int placedMin = RoundDownToQuantum(ToWholeMinutes(placed));

                if (placedMin >= QUANTUM_MIN)
                {

                    touched.Add(day);

                    if (leftToRestoreMin != int.MaxValue)
                        leftToRestoreMin = Math.Max(0, leftToRestoreMin - placedMin);
                }
                else
                {
                }
            }

            var gen = new AutomaticEventsGeneratorService(this, _ => Task.FromResult(false), employeeId);

            foreach (var day in touched.OrderBy(d => d))
            {
                await gen.RegenerateDailyEventsAsync(day, preserveUserSettings: true);
                await EnsureLunchInsideWorkWindowAsync(employeeId, day, callRegenerate: false);
            }

            if (touched.Count > 0)
                MessageBus.Current.SendMessage(new AutoEventsGeneratedMessage());
        }

        private const double BALANCE_EPS = 1e-6;

        public bool IsScopeReallyBalanced(IEnumerable<DateTime> days, int employeeId = DefaultEmployeeId)
        {
            var totals = GetScopeTotals(days, employeeId);
            return totals.extra <= BALANCE_EPS && totals.under <= BALANCE_EPS;
        }

        private double RestoreTransferredMinutes(
            DateTime day,
            int minutes,
            TransferEdge edge,
            int employeeId)
        {
            if (minutes <= 0)
                return 0;

            var span = TimeSpan.FromMinutes(RoundDownToQuantum(minutes));
            if (span <= TimeSpan.Zero)
                return 0;

            return edge switch
            {
                TransferEdge.Start => ExtendStartAuto(day, span.TotalHours, employeeId),
                TransferEdge.End => ExtendEndAuto(day, span.TotalHours, employeeId),
                _ => 0
            };
        }

        private async Task<RollbackResult> RollbackTransfersForLockedDayAsync(
            DateTime lockedDay,
            int neededMinutes,
            int employeeId = DefaultEmployeeId)
        {
            int left = RoundDownToQuantum(neededMinutes);
            if (left < QUANTUM_MIN)
                return new RollbackResult();

            var incoming = WorkTransferReportingService
                .GetTransfersTo(lockedDay, employeeId)
                .OrderByDescending(x => x.Minutes)
                .ThenBy(x => x.FromDay)
                .ToList();

            if (incoming.Count == 0)
                return new RollbackResult();

            var touched = new HashSet<DateTime>();
            var restoredByDay = new Dictionary<DateTime, int>();
            int restored = 0;

            foreach (var entry in incoming)
            {
                if (left < QUANTUM_MIN)
                    break;

                int want = Math.Min(left, entry.Minutes);
                want = RoundDownToQuantum(want);
                if (want < QUANTUM_MIN)
                    continue;

                var putBackHours = RestoreTransferredMinutes(
                    entry.FromDay,
                    want,
                    entry.Edge,
                    employeeId);

                int putBackMin = RoundDownToQuantum((int)Math.Round(putBackHours * 60.0));
                if (putBackMin < QUANTUM_MIN)
                    continue;

                restored += putBackMin;
                left -= putBackMin;
                touched.Add(entry.FromDay.Date);
                AddRestoredMinutes(restoredByDay, entry.FromDay, putBackMin);

                WorkTransferReportingService.RemoveEntry(entry);

                int remainder = entry.Minutes - putBackMin;
                remainder = RoundDownToQuantum(remainder);

                if (remainder >= QUANTUM_MIN)
                {
                    WorkTransferReportingService.AddTransfer(
                        entry.FromDay.Date,
                        entry.ToDay.Date,
                        remainder,
                        entry.Edge,
                        employeeId);
                }
            }

            var gen = new AutomaticEventsGeneratorService(this, _ => Task.FromResult(false), employeeId);

            foreach (var d in touched.OrderBy(x => x))
            {
                await gen.RegenerateDailyEventsAsync(d, preserveUserSettings: true);
                await EnsureLunchInsideWorkWindowAsync(employeeId, d, callRegenerate: false);
            }

            if (restored > 0)
                MessageBus.Current.SendMessage(new AutoEventsGeneratedMessage());

            return new RollbackResult
            {
                RestoredMinutes = restored,
                TouchedDays = touched,
                MinutesByDay = restoredByDay
            };
        }

        private void RefreshWeekMeta(
            Dictionary<DateTime, WeekDayMeta> meta,
            IEnumerable<DateTime> scopeDays,
            WorkingHoursCalculatorService calc,
            int employeeId)
        {
            foreach (var wd in scopeDays.Select(x => x.Date).Distinct().OrderBy(x => x))
            {
                var m2 = calc.DailyMetrics(wd, GetEventsForDay(employeeId, wd));
                meta[wd].Extra = m2.over;
                meta[wd].CanStart = CanTrimStart(wd, employeeId);
                meta[wd].CanEnd = CanTrimEnd(wd, employeeId);
                meta[wd].Locked = IsLockedEdgeDay(wd, employeeId);
            }
        }

        private sealed class RollbackResult
        {
            public int RestoredMinutes { get; init; }
            public HashSet<DateTime> TouchedDays { get; init; } = new();
            public Dictionary<DateTime, int> MinutesByDay { get; init; } = new();
        }

        private static (int IsoYear, int IsoWeek) GetIsoWeekKey(DateTime d)
             => (System.Globalization.ISOWeek.GetYear(d), System.Globalization.ISOWeek.GetWeekOfYear(d));

        private static List<DateTime> GetMonthWorkdays(int year, int month)
        {
            var first = new DateTime(year, month, 1);
            var last = new DateTime(year, month, DateTime.DaysInMonth(year, month));

            return Enumerable.Range(0, (last - first).Days + 1)
                .Select(i => first.AddDays(i).Date)
                .Where(IsWorkday)
                .ToList();
        }

        private static bool IsPartialMonthWeek((int IsoYear, int IsoWeek) key, int month)
        {
            var monday = FirstDayOfIsoWeek(key.IsoYear, key.IsoWeek);

            var weekWorkdays = Enumerable.Range(0, 7)
                .Select(i => monday.AddDays(i).Date)
                .Where(IsWorkday)
                .ToList();

            return weekWorkdays.Any(d => d.Month == month) &&
                   weekWorkdays.Any(d => d.Month != month);
        }

        private async Task BalanceMonthPartialScopesAsync(int year, int month, int employeeId = DefaultEmployeeId)
        {
            var monthDays = GetMonthWorkdays(year, month);
            if (monthDays.Count == 0)
                return;

            var orderedMonthGroups = monthDays
                .GroupBy(GetIsoWeekKey)
                .Select(g => g.OrderBy(d => d).ToList())
                .Where(g => g.Count > 0)
                .OrderBy(g => g.First())
                .ToList();

            var partialGroups = orderedMonthGroups
                .Where(g => IsPartialMonthWeek(GetIsoWeekKey(g[0]), month))
                .ToList();

            if (partialGroups.Count == 0)
            {
                return;
            }

            List<DateTime>? balanceScope;
            string scopeKind;

            if (partialGroups.Count >= 2)
            {
                balanceScope = partialGroups
                    .SelectMany(g => g)
                    .Select(d => d.Date)
                    .Distinct()
                    .OrderBy(d => d)
                    .ToList();

                scopeKind = "partial_to_partial";
            }
            else
            {
                balanceScope = BuildSinglePartialFallbackScope(orderedMonthGroups, partialGroups[0], month);
                scopeKind = "single_partial_to_full";

                if (balanceScope == null || balanceScope.Count < 2)
                {
                    return;
                }
            }

            if (!CanStrictlyBalancePartialPool(balanceScope, employeeId, out var reason))
            {              
                return;
            }

            await BalanceOneWeekAsync(balanceScope, employeeId);

            var totals = GetScopeTotals(balanceScope, employeeId);
        }

        private double RestoreUnderworkSmart(DateTime day, double hours, int employeeId)
        {
            const double EPS = 1e-6;

            if (hours <= EPS)
                return 0;

            bool canStart = CanExtendStart(day, employeeId);
            bool canEnd = CanExtendEnd(day, employeeId);

            if (!canStart && !canEnd)
                return 0;

            var work = GetEventsForDay(employeeId, day)
                .Where(e => !e.IsDeleted && IsWorkLike(e))
                .OrderBy(e => e.StartTime)
                .ToList();

            double placed = 0.0;

            bool preferStart;

            if (canStart && !canEnd)
            {
                preferStart = true;
            }
            else if (!canStart && canEnd)
            {
                preferStart = false;
            }
            else if (work.Count == 0)
            {
                preferStart = false;
            }
            else
            {
                var first = work.First().StartTime;
                var last = work.Last().EndTime;

                var gapBefore = (first - day.Date).TotalMinutes;
                var gapAfter = (day.Date.AddDays(1).AddMinutes(-QUANTUM_MIN) - last).TotalMinutes;

                preferStart = gapBefore >= gapAfter;
            }

            if (preferStart)
            {
                if (canStart)
                    placed += ExtendStartAuto(day, hours - placed, employeeId);

                if (hours - placed > EPS && canEnd)
                    placed += ExtendEndAuto(day, hours - placed, employeeId);
            }
            else
            {
                if (canEnd)
                    placed += ExtendEndAuto(day, hours - placed, employeeId);

                if (hours - placed > EPS && canStart)
                    placed += ExtendStartAuto(day, hours - placed, employeeId);
            }

            return placed;
        }

        private bool DayHadAutoGeneratedEvents(DateTime day, int employeeId = DefaultEmployeeId)
        {
            using var db = new AppDbContext();

            return db.Events.Any(e =>
                e.EmployeeId == employeeId &&
                !e.IsDeleted &&
                e.StartTime.Date == day.Date &&
                e.IsAutoGenerated);
        }

        private void RebuildDayAfterChange(DateTime day, int employeeId, bool dayHadAuto)
        {
            var gen = new AutomaticEventsGeneratorService(this, _ => Task.FromResult(false), employeeId);

            if (dayHadAuto)
            {
                SettingsService.DeleteComputedDaySettingsForDate(day, employeeId);

                gen.RegenerateDailyEventsAsync(day, preserveUserSettings: false)
                   .GetAwaiter()
                   .GetResult();
            }
            else
            {
                RemoveAutoGeneratedEvents(employeeId, day);
                AdjustDaySettingsAfterChange(day, employeeId);

                gen.RegenerateDailyEventsAsync(day, preserveUserSettings: false)
                   .GetAwaiter()
                   .GetResult();
            }

            EnsureLunchInsideWorkWindowAsync(employeeId, day)
                .GetAwaiter()
                .GetResult();

            ReconcileSelfTrimPoolForDay(day, employeeId);
        }
        private int GetEdgeAutoTrimCapacityMinutes(DateTime day, int employeeId = DefaultEmployeeId)
        {
            var workLike = GetEventsForDay(employeeId, day)
                .Where(e => !e.IsDeleted)
                .Where(e => IsWorkLike(e))
                .OrderBy(e => e.StartTime)
                .ToList();

            if (workLike.Count == 0)
                return 0;

            var first = workLike.First();
            var last = workLike.Last();

            int cap = 0;

            if (first.IsAutoGenerated)
                cap += RoundDownToQuantum((int)(first.EndTime - first.StartTime).TotalMinutes);

            if (last.IsAutoGenerated && last.Id != first.Id)
                cap += RoundDownToQuantum((int)(last.EndTime - last.StartTime).TotalMinutes);

            return cap;
        }

        private (double extra, double under) GetScopeTotals(IEnumerable<DateTime> days, int employeeId = DefaultEmployeeId)
        {
            var calc = new WorkingHoursCalculatorService();
            var scope = days
                .Select(d => d.Date)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            return (
                extra: scope.Sum(d => calc.DailyMetrics(d, GetEventsForDay(employeeId, d)).over),
                under: scope.Sum(d => calc.DailyMetrics(d, GetEventsForDay(employeeId, d)).under)
            );
        }

        private bool CanStrictlyBalancePartialPool(
            IReadOnlyList<DateTime> scopeDays,
            int employeeId,
            out string reason)
        {
            var calc = new WorkingHoursCalculatorService();

            int lockedNeedMin = 0;
            int donorSpareMin = 0;

            foreach (var day in scopeDays
                .Select(d => d.Date)
                .Distinct()
                .OrderBy(d => d))
            {
                int overMin = RoundDownToQuantum(
                    ToWholeMinutes(calc.DailyMetrics(day, GetEventsForDay(employeeId, day)).over));

                int edgeCapMin = GetEdgeAutoTrimCapacityMinutes(day, employeeId);

                if (overMin <= 0)
                {
                    donorSpareMin += edgeCapMin;
                    continue;
                }

                bool isLocked = IsLockedEdgeDay(day, employeeId);

                if (isLocked)
                {
                    lockedNeedMin += overMin;
                    continue;
                }

                if (edgeCapMin < overMin)
                {
                    reason = $"day={day:yyyy-MM-dd}, extraMin={overMin}, selfCapacityMin={edgeCapMin}";
                    return false;
                }

                donorSpareMin += (edgeCapMin - overMin);
            }

            if (donorSpareMin < lockedNeedMin)
            {
                reason = $"lockedNeedMin={lockedNeedMin}, donorSpareMin={donorSpareMin}";
                return false;
            }

            reason = "ok";
            return true;
        }

        private List<DateTime>? BuildSinglePartialFallbackScope(
            List<List<DateTime>> orderedMonthGroups,
            List<DateTime> partialGroup,
            int month)
        {
            if (orderedMonthGroups.Count == 0 || partialGroup.Count == 0)
                return null;

            var partialKey = GetIsoWeekKey(partialGroup[0]);
            var firstKey = GetIsoWeekKey(orderedMonthGroups.First()[0]);
            var lastKey = GetIsoWeekKey(orderedMonthGroups.Last()[0]);

            List<DateTime>? companionFullGroup = null;

            if (partialKey == firstKey)
            {
                companionFullGroup = orderedMonthGroups
                    .Where(g => g.Count > 0)
                    .Where(g => GetIsoWeekKey(g[0]) != partialKey)
                    .FirstOrDefault(g => !IsPartialMonthWeek(GetIsoWeekKey(g[0]), month));
            }
            else if (partialKey == lastKey)
            {
                companionFullGroup = orderedMonthGroups
                    .Where(g => g.Count > 0)
                    .Where(g => GetIsoWeekKey(g[0]) != partialKey)
                    .Reverse()
                    .FirstOrDefault(g => !IsPartialMonthWeek(GetIsoWeekKey(g[0]), month));
            }
            else
            {
                companionFullGroup = orderedMonthGroups
                    .Where(g => g.Count > 0)
                    .Where(g => GetIsoWeekKey(g[0]) != partialKey)
                    .FirstOrDefault(g => !IsPartialMonthWeek(GetIsoWeekKey(g[0]), month));
            }

            if (companionFullGroup == null || companionFullGroup.Count == 0)
                return null;

            return partialGroup
                .Concat(companionFullGroup)
                .Select(d => d.Date)
                .Distinct()
                .OrderBy(d => d)
                .ToList();
        }

        private sealed class DayBudget
        {
            public DateTime Day { get; init; }
            public int Minutes { get; set; }
        }

        public sealed class PdfDayCompensation
        {
            public int ExtraOffsetMinutes { get; set; }
            public int UnderOffsetMinutes { get; set; }
        }

        private void ApplyPdfCompensationForScope(
            IReadOnlyList<DateTime> scopeDays,
            Dictionary<DateTime, PdfDayCompensation> result,
            int employeeId = DefaultEmployeeId)
        {
            var calc = new WorkingHoursCalculatorService();

            var extras = scopeDays
                .Select(d => d.Date)
                .Distinct()
                .OrderBy(d => d)
                .Select(d => new DayBudget
                {
                    Day = d,
                    Minutes = RoundDownToQuantum(
                        ToWholeMinutes(calc.DailyMetrics(d, GetEventsForDay(employeeId, d)).over))
                })
                .Where(x => x.Minutes >= QUANTUM_MIN)
                .ToList();

            var unders = scopeDays
                .Select(d => d.Date)
                .Distinct()
                .OrderBy(d => d)
                .Select(d => new DayBudget
                {
                    Day = d,
                    Minutes = RoundDownToQuantum(
                        ToWholeMinutes(calc.DailyMetrics(d, GetEventsForDay(employeeId, d)).under))
                })
                .Where(x => x.Minutes >= QUANTUM_MIN)
                .ToList();

            int i = 0;
            int j = 0;

            while (i < extras.Count && j < unders.Count)
            {
                int move = Math.Min(extras[i].Minutes, unders[j].Minutes);
                move = RoundDownToQuantum(move);

                if (move < QUANTUM_MIN)
                    break;

                if (result.TryGetValue(extras[i].Day, out var extraComp))
                    extraComp.ExtraOffsetMinutes += move;

                if (result.TryGetValue(unders[j].Day, out var underComp))
                    underComp.UnderOffsetMinutes += move;

                extras[i].Minutes -= move;
                unders[j].Minutes -= move;

                if (extras[i].Minutes < QUANTUM_MIN)
                    i++;

                if (unders[j].Minutes < QUANTUM_MIN)
                    j++;
            }
        }

        public Dictionary<DateTime, PdfDayCompensation> BuildWeeklyPdfCompensation(
            DateTime anyDate,
            int employeeId = DefaultEmployeeId)
        {
            var weekDays = GetWeekWorkdays(anyDate)
                .Select(d => d.Date)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            var result = weekDays.ToDictionary(
                d => d,
                _ => new PdfDayCompensation());

            ApplyPdfCompensationForScope(weekDays, result, employeeId);
            return result;
        }

        public Dictionary<DateTime, PdfDayCompensation> BuildMonthPdfCompensation(
            int year,
            int month,
            int employeeId = DefaultEmployeeId)
        {
            var monthDays = GetMonthWorkdays(year, month)
                .Select(d => d.Date)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            var result = monthDays.ToDictionary(
                d => d,
                _ => new PdfDayCompensation());

            if (monthDays.Count == 0)
                return result;

            var orderedMonthGroups = monthDays
                .GroupBy(GetIsoWeekKey)
                .Select(g => g.OrderBy(d => d).ToList())
                .Where(g => g.Count > 0)
                .OrderBy(g => g.First())
                .ToList();

            var partialGroups = orderedMonthGroups
                .Where(g => IsPartialMonthWeek(GetIsoWeekKey(g[0]), month))
                .ToList();

            var processed = new HashSet<DateTime>();

            void ApplyScope(List<DateTime> scope)
            {
                ApplyPdfCompensationForScope(scope, result, employeeId);

                foreach (var d in scope.Select(x => x.Date))
                {
                    if (d.Month == month && d.Year == year)
                        processed.Add(d);
                }
            }

            if (partialGroups.Count >= 2)
            {
                var partialScope = partialGroups
                    .SelectMany(g => g)
                    .Select(d => d.Date)
                    .Distinct()
                    .OrderBy(d => d)
                    .ToList();

                ApplyScope(partialScope);
            }
            else if (partialGroups.Count == 1)
            {
                var fallbackScope = BuildSinglePartialFallbackScope(
                    orderedMonthGroups,
                    partialGroups[0],
                    month);

                if (fallbackScope != null && fallbackScope.Count >= 2)
                    ApplyScope(fallbackScope);
                else
                    ApplyScope(partialGroups[0]);
            }

            foreach (var group in orderedMonthGroups)
            {
                if (group.Any(d => processed.Contains(d.Date)))
                    continue;

                ApplyScope(group);
            }

            return result;
        }
        private bool CanExtendEnd(DateTime day, int employeeId = DefaultEmployeeId)
        {
            var resolved = GetResolvedWindow(day, employeeId);
            var daySoftEnd = day.Date.AddDays(1).AddMinutes(-QUANTUM_MIN);

            var nonLunch = GetEventsForDay(employeeId, day)
                .Where(e => !e.IsDeleted && e.EventType != EventType.Lunch)
                .OrderBy(e => e.StartTime)
                .ToList();

            var anchorStart = nonLunch.Any()
                ? nonLunch.Max(e => e.EndTime)
                : day + resolved.ArrivalTime;

            int freeMin = RoundDownToQuantum((int)(daySoftEnd - anchorStart).TotalMinutes);
            return freeMin >= QUANTUM_MIN;
        }

        private bool CanExtendStart(DateTime day, int employeeId = DefaultEmployeeId)
        {
            var resolved = GetResolvedWindow(day, employeeId);

            var nonLunch = GetEventsForDay(employeeId, day)
                .Where(e => !e.IsDeleted && e.EventType != EventType.Lunch)
                .OrderBy(e => e.StartTime)
                .ToList();

            var anchorEnd = nonLunch.Any()
                ? nonLunch.Min(e => e.StartTime)
                : day + resolved.DepartureTime;

            int freeMin = RoundDownToQuantum((int)(anchorEnd - day.Date).TotalMinutes);
            return freeMin >= QUANTUM_MIN;
        }

        private sealed class SelfTrimEntry
        {
            public DateTime Day { get; set; }
            public TransferEdge Edge { get; set; }
            public int Minutes { get; set; }
        }

        private static class SelfTrimStore
        {
            public static void Clamp(int employeeId, DateTime day, TransferEdge edge, int maxMinutes)
            {
                day = day.Date;
                maxMinutes = Math.Max(0, RoundDownToQuantum(maxMinutes));

                using var db = new AppDbContext();

                var row = db.BalanceSelfTrims.SingleOrDefault(x =>
                    x.EmployeeId == employeeId &&
                    x.Day == day &&
                    x.Edge == (int)edge);

                if (row == null)
                    return;

                if (maxMinutes < QUANTUM_MIN)
                {
                    db.BalanceSelfTrims.Remove(row);
                    db.SaveChanges();
                    return;
                }

                if (row.Minutes > maxMinutes)
                {
                    row.Minutes = maxMinutes;
                    row.UpdatedAtUtc = DateTime.UtcNow;
                    db.SaveChanges();
                }
            }

            public static void Add(int employeeId, DateTime day, TransferEdge edge, int minutes)
            {
                minutes = minutes - minutes % QUANTUM_MIN;
                if (minutes < QUANTUM_MIN)
                    return;

                day = day.Date;
                var now = DateTime.UtcNow;

                using var db = new AppDbContext();

                var row = db.BalanceSelfTrims.SingleOrDefault(x =>
                    x.EmployeeId == employeeId &&
                    x.Day == day &&
                    x.Edge == (int)edge);

                if (row == null)
                {
                    db.BalanceSelfTrims.Add(new BalanceSelfTrim
                    {
                        EmployeeId = employeeId,
                        Day = day,
                        Edge = (int)edge,
                        Minutes = minutes,
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    });
                }
                else
                {
                    row.Minutes += minutes;
                    row.UpdatedAtUtc = now;
                }

                db.SaveChanges();
            }

            public static int GetTotal(int employeeId, DateTime day)
            {
                day = day.Date;

                using var db = new AppDbContext();
                return db.BalanceSelfTrims
                    .Where(x => x.EmployeeId == employeeId && x.Day == day)
                    .Select(x => (int?)x.Minutes)
                    .Sum() ?? 0;
            }

            public static List<SelfTrimEntry> GetSnapshot(int employeeId, DateTime day)
            {
                day = day.Date;

                using var db = new AppDbContext();
                return db.BalanceSelfTrims
                    .AsNoTracking()
                    .Where(x => x.EmployeeId == employeeId && x.Day == day)
                    .OrderByDescending(x => x.Minutes)
                    .ThenBy(x => x.Edge)
                    .Select(x => new SelfTrimEntry
                    {
                        Day = x.Day,
                        Edge = (TransferEdge)x.Edge,
                        Minutes = x.Minutes
                    })
                    .ToList();
            }

            public static List<SelfTrimEntry> GetScopeSnapshot(int employeeId, IEnumerable<DateTime> days)
            {
                var dayList = days
                    .Select(x => x.Date)
                    .Distinct()
                    .ToList();

                if (dayList.Count == 0)
                    return new List<SelfTrimEntry>();

                using var db = new AppDbContext();
                return db.BalanceSelfTrims
                    .AsNoTracking()
                    .Where(x => x.EmployeeId == employeeId && dayList.Contains(x.Day))
                    .OrderBy(x => x.Day)
                    .ThenBy(x => x.Edge)
                    .Select(x => new SelfTrimEntry
                    {
                        Day = x.Day,
                        Edge = (TransferEdge)x.Edge,
                        Minutes = x.Minutes
                    })
                    .ToList();
            }

            public static void Consume(int employeeId, DateTime day, TransferEdge edge, int minutes)
            {
                minutes = minutes - minutes % QUANTUM_MIN;
                if (minutes < QUANTUM_MIN)
                    return;

                day = day.Date;

                using var db = new AppDbContext();
                var row = db.BalanceSelfTrims.SingleOrDefault(x =>
                    x.EmployeeId == employeeId &&
                    x.Day == day &&
                    x.Edge == (int)edge);

                if (row == null)
                    return;

                row.Minutes -= minutes;

                if (row.Minutes < QUANTUM_MIN)
                    db.BalanceSelfTrims.Remove(row);
                else
                    row.UpdatedAtUtc = DateTime.UtcNow;

                db.SaveChanges();
            }

            public static void ClearDates(int employeeId, IEnumerable<DateTime> dates)
            {
                var dayList = dates
                    .Select(x => x.Date)
                    .Distinct()
                    .ToList();

                if (dayList.Count == 0)
                    return;

                using var db = new AppDbContext();
                var rows = db.BalanceSelfTrims
                    .Where(x => x.EmployeeId == employeeId && dayList.Contains(x.Day))
                    .ToList();

                if (rows.Count == 0)
                    return;

                db.BalanceSelfTrims.RemoveRange(rows);
                db.SaveChanges();
            }
        }

        private async Task<PoolRestoreResult> RollbackTransferPoolForScopeAsync(
            IReadOnlyList<DateTime> scopeDays,
            int employeeId = DefaultEmployeeId)
        {
            var calc = new WorkingHoursCalculatorService();

            var days = scopeDays
                .Select(d => d.Date)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            int scopeUnderMin = days.Sum(d =>
                RoundDownToQuantum(ToWholeMinutes(
                    calc.DailyMetrics(d, GetEventsForDay(employeeId, d)).under)));

            scopeUnderMin = RoundDownToQuantum(scopeUnderMin);
            if (scopeUnderMin < QUANTUM_MIN)
                return new PoolRestoreResult();

            var incoming = days
                .SelectMany(d => WorkTransferReportingService.GetTransfersTo(d, employeeId))
                .OrderByDescending(x => x.Minutes)
                .ThenBy(x => x.ToDay)
                .ThenBy(x => x.FromDay)
                .ToList();

            if (incoming.Count == 0)
                return new PoolRestoreResult();

            int left = scopeUnderMin;
            int restored = 0;
            var touched = new HashSet<DateTime>();
            var restoredByDay = new Dictionary<DateTime, int>();

            foreach (var entry in incoming)
            {
                if (left < QUANTUM_MIN)
                    break;

                int want = Math.Min(left, entry.Minutes);
                want = RoundDownToQuantum(want);

                if (want < QUANTUM_MIN)
                    continue;

                var putBackHours = RestoreTransferredMinutes(
                    entry.FromDay,
                    want,
                    entry.Edge,
                    employeeId);

                int putBackMin = RoundDownToQuantum((int)Math.Round(putBackHours * 60.0));
                if (putBackMin < QUANTUM_MIN)
                    continue;

                restored += putBackMin;
                left -= putBackMin;
                touched.Add(entry.FromDay.Date);
                AddRestoredMinutes(restoredByDay, entry.FromDay, putBackMin);

                WorkTransferReportingService.RemoveEntry(entry);

                int remainder = RoundDownToQuantum(entry.Minutes - putBackMin);
                if (remainder >= QUANTUM_MIN)
                {
                    WorkTransferReportingService.AddTransfer(
                        entry.FromDay.Date,
                        entry.ToDay.Date,
                        remainder,
                        entry.Edge,
                        employeeId);
                }
            }

            if (restored >= QUANTUM_MIN)
            {
                var gen = new AutomaticEventsGeneratorService(this, _ => Task.FromResult(false), employeeId);

                foreach (var d in touched.OrderBy(x => x))
                {
                    await gen.RegenerateDailyEventsAsync(d, preserveUserSettings: true);
                    await EnsureLunchInsideWorkWindowAsync(employeeId, d, callRegenerate: false);
                }

                MessageBus.Current.SendMessage(new AutoEventsGeneratedMessage());
            }

            return new PoolRestoreResult
            {
                RestoredMinutes = restored,
                TouchedDays = touched,
                MinutesByDay = restoredByDay
            };
        }

        private static readonly System.Threading.AsyncLocal<bool> _trackSelfTrim = new();

        private sealed class SelfTrimTrackingScope : IDisposable
        {
            private readonly bool _prev;

            public SelfTrimTrackingScope()
            {
                _prev = _trackSelfTrim.Value;
                _trackSelfTrim.Value = true;
            }

            public void Dispose()
            {
                _trackSelfTrim.Value = _prev;
            }
        }

        private static bool ShouldTrackSelfTrim()
            => _trackSelfTrim.Value;

        private sealed class PoolRestoreResult
        {
            public int RestoredMinutes { get; init; }
            public HashSet<DateTime> TouchedDays { get; init; } = new();
            public Dictionary<DateTime, int> MinutesByDay { get; init; } = new();
        }

        private static void AddRestoredMinutes(
            Dictionary<DateTime, int> bag,
            DateTime day,
            int minutes)
        {
            minutes = RoundDownToQuantum(minutes);
            if (minutes < QUANTUM_MIN)
                return;

            day = day.Date;

            if (bag.TryGetValue(day, out var current))
                bag[day] = current + minutes;
            else
                bag[day] = minutes;
        }

        private static void MergeRestoredMinutes(
            Dictionary<DateTime, int> target,
            IDictionary<DateTime, int> source)
        {
            foreach (var kv in source)
                AddRestoredMinutes(target, kv.Key, kv.Value);
        }

        private int GetExtendEndCapacityMinutes(DateTime day, int employeeId = DefaultEmployeeId)
        {
            var resolved = GetResolvedWindow(day, employeeId);
            var daySoftEnd = day.Date.AddDays(1).AddMinutes(-QUANTUM_MIN);

            var nonLunch = GetEventsForDay(employeeId, day)
                .Where(e => !e.IsDeleted && e.EventType != EventType.Lunch)
                .OrderBy(e => e.StartTime)
                .ToList();

            var anchorStart = nonLunch.Any()
                ? nonLunch.Max(e => e.EndTime)
                : day + resolved.ArrivalTime;

            return Math.Max(0, RoundDownToQuantum((int)(daySoftEnd - anchorStart).TotalMinutes));
        }

        private int GetExtendStartCapacityMinutes(DateTime day, int employeeId = DefaultEmployeeId)
        {
            var resolved = GetResolvedWindow(day, employeeId);

            var nonLunch = GetEventsForDay(employeeId, day)
                .Where(e => !e.IsDeleted && e.EventType != EventType.Lunch)
                .OrderBy(e => e.StartTime)
                .ToList();

            var anchorEnd = nonLunch.Any()
                ? nonLunch.Min(e => e.StartTime)
                : day + resolved.DepartureTime;

            return Math.Max(0, RoundDownToQuantum((int)(anchorEnd - day.Date).TotalMinutes));
        }

        private void ReconcileSelfTrimPoolForDay(DateTime day, int employeeId = DefaultEmployeeId)
        {
            var startCap = GetExtendStartCapacityMinutes(day, employeeId);
            var endCap = GetExtendEndCapacityMinutes(day, employeeId);

            SelfTrimStore.Clamp(employeeId, day, TransferEdge.Start, startCap);
            SelfTrimStore.Clamp(employeeId, day, TransferEdge.End, endCap);
        }

        private static TimeSpan ParseDurationOrDefault(string? value, TimeSpan fallback)
        {
            return TimeSpan.TryParse(value, out var ts) && ts > TimeSpan.Zero
                ? ts
                : fallback;
        }

        private static int GetTargetLunchCount(TimeSpan gross)
        {
            if (gross >= TimeSpan.FromHours(12))
                return 2;

            if (gross >= TimeSpan.FromHours(8))
                return 1;

            return 0;
        }
    }
}