using Microsoft.EntityFrameworkCore;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TeacherScheduleApp.Data;
using TeacherScheduleApp.Helpers;
using TeacherScheduleApp.Messages;
using TeacherScheduleApp.Models;

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

        private static bool IsWorkday(DateTime d)
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

            BalanceWeekForDateAsync(parentDate, employeeId).GetAwaiter().GetResult();
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

            parent.Title = ev.Title;
            parent.StartTime = ev.StartTime;
            parent.EndTime = ev.EndTime;
            parent.AllDay = ev.AllDay;
            parent.Description = ev.Description;
            parent.IsDeleted = ev.IsDeleted;
            parent.EventType = ev.EventType;
            parent.IsAutoGenerated = ev.IsAutoGenerated;
            parent.HasCollision = ev.HasCollision;
            parent.AutoGeneratedForDate = ev.AutoGeneratedForDate;
            parent.ImportBatchId = ev.ImportBatchId;

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
                .Concat(Enumerable.Range(0, (parent.EndTime.Date - parent.StartTime.Date).Days + 1).Select(i => parent.StartTime.Date.AddDays(i)))
            );

            InvalidateMonthsForDates(affectedDates);
            InvalidateWeeksForDates(affectedDates, employeeId);

            if (!suppressRegen)
            {
                foreach (var day in affectedDates.Where(IsWorkday).OrderBy(x => x))
                {   RemoveAutoGeneratedEvents(employeeId, day);
                    AdjustDaySettingsAfterChange(day, employeeId);

                    var gen = new AutomaticEventsGeneratorService(this, _ => Task.FromResult(false), employeeId);
                    gen.RegenerateDailyEventsAsync(day, preserveUserSettings: true).GetAwaiter().GetResult();
                    EnsureLunchInsideWorkWindowAsync(employeeId, day).GetAwaiter().GetResult();
                }

                BalanceForChangedRangeAsync(affectedDates.Min(), affectedDates.Max(), employeeId).GetAwaiter().GetResult();

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

            ev.IsDeleted = true;
            db.SaveChanges();

            RemoveAutoGeneratedEvents(employeeId, day);

            InvalidateMonthsForDates(new[] { day });
            InvalidateWeeksForDates(new[] { day }, employeeId);
            AdjustDaySettingsAfterChange(day, employeeId);

            var gen = new AutomaticEventsGeneratorService(this, _ => Task.FromResult(false), employeeId);
            gen.RegenerateDailyEventsAsync(day, preserveUserSettings: false).GetAwaiter().GetResult();
            EnsureLunchInsideWorkWindowAsync(employeeId, day).GetAwaiter().GetResult();
            BalanceWeekForDateAsync(day, employeeId).GetAwaiter().GetResult();

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

            foreach (var day in affectedDates)
            {
                RemoveAutoGeneratedEvents(employeeId, day);

                bool anyLeft = db.Events.Any(e => e.EmployeeId == employeeId && !e.IsDeleted && e.StartTime.Date == day);
                if (!anyLeft)
                {
                    SettingsService.DeleteComputedDaySettingsForDate(day, employeeId);
                    continue;
                }

                AdjustDaySettingsAfterChange(day, employeeId);

                var gen = new AutomaticEventsGeneratorService(this, _ => Task.FromResult(false), employeeId);
                gen.RegenerateDailyEventsAsync(day, preserveUserSettings: false).GetAwaiter().GetResult();
                EnsureLunchInsideWorkWindowAsync(employeeId, day).GetAwaiter().GetResult();
                BalanceWeekForDateAsync(day, employeeId).GetAwaiter().GetResult();

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
                AdjustDaySettingsAfterChange(day, employeeId);
                await gen.RegenerateDailyEventsAsync(day, preserveUserSettings: false);
            }

            await BalanceForChangedRangeAsync(from, to, employeeId);
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
                AdjustDaySettingsAfterChange(day, employeeId);
                await gen.RegenerateDailyEventsAsync(day, preserveUserSettings: false);
            }

            if (affectedDays.Count > 0)
                await BalanceForChangedRangeAsync(affectedDays.Min(), affectedDays.Max(), employeeId);

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
            return db.Events.Where(e => e.IsDeleted).ExecuteDelete();
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
                            e.EventType == EventType.Lunch)
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

        private void SplitAutoWorkAroundLunch(DateTime day, TimeSpan ls, TimeSpan le, int employeeId = DefaultEmployeeId)
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

            return take.TotalHours;
        }

        private double TrimEndAuto(DateTime day, double hours, int employeeId)
            => TrimEndAuto(day, QMinutes(ToWholeMinutes(hours)), employeeId);

        private double TrimStartAuto(DateTime day, double hours, int employeeId)
            => TrimStartAuto(day, QMinutes(ToWholeMinutes(hours)), employeeId);

        private double ExtendEndAuto(DateTime day, double hours, int employeeId)
        {
            int addMin = ToWholeMinutes(hours);
            if (addMin <= 0) return 0;

            var (_, last) = GetEdgeWorkEvents(day, employeeId);
            var resolved = GetResolvedWindow(day, employeeId);
            var arr = resolved.ArrivalTime;

            var start = TruncToMinute(last?.EndTime ?? (day + arr));
            var end = TruncToMinute(start + QMinutes(addMin));

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

            var ls = resolved.LunchStart;
            var le = resolved.LunchEnd;
            if (le > ls && (le > end.TimeOfDay || ls < arr))
                (ls, le) = (TimeSpan.Zero, TimeSpan.Zero);

            SettingsService.SaveDaySettingsForDate(day, arr, end.TimeOfDay, ls, le, employeeId);
            ReplaceLunchEvent(day, ls, le, employeeId);
            SplitAutoWorkAroundLunch(day, ls, le, employeeId);

            return addMin / 60.0;
        }

        private double ExtendStartAuto(DateTime day, double hours, int employeeId)
        {
            int addMin = ToWholeMinutes(hours);
            if (addMin <= 0) return 0;

            var resolved = GetResolvedWindow(day, employeeId);
            var arr = resolved.ArrivalTime;
            var dep = resolved.DepartureTime;

            var firstBlockingStart = GetEventsForDay(employeeId, day)
                .Where(e => !e.IsDeleted)
                .Where(e => e.EventType != EventType.Lunch)
                .Where(e => !e.IsAutoGenerated || !IsWorkLike(e))
                .Select(e => e.StartTime)
                .DefaultIfEmpty(day + dep)
                .Min();

            var end = TruncToMinute(firstBlockingStart);
            var start = TruncToMinute(end - QMinutes(addMin));
            if (start < day + arr) start = day + arr;

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

            var ls = resolved.LunchStart;
            var le = resolved.LunchEnd;
            if (le > ls && (le > dep || ls < start.TimeOfDay))
                (ls, le) = (TimeSpan.Zero, TimeSpan.Zero);

            SettingsService.SaveDaySettingsForDate(day, start.TimeOfDay, dep, ls, le, employeeId);
            ReplaceLunchEvent(day, ls, le, employeeId);
            SplitAutoWorkAroundLunch(day, ls, le, employeeId);

            return (end - start).TotalHours;
        }

        private void AdjustDaySettingsAfterChange(DateTime day, int employeeId = DefaultEmployeeId)
        {
            const double EPS = 1e-6;
            var resolved = GetResolvedWindow(day, employeeId);
            var movedOut = WorkTransferReportingService.GetMovedOut(day);
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
            var desiredLs = resolved.LunchStart;
            var desiredLe = resolved.LunchEnd;

            if (dep <= arr)
                return;

            var desiredLen = desiredLe > desiredLs
                ? desiredLe - desiredLs
                : TimeSpan.FromMinutes(30);

            var evs = GetEventsForDay(employeeId, day).Where(e => !e.IsDeleted).ToList();
            var lunchEv = evs.FirstOrDefault(e => e.EventType == EventType.Lunch);

            var ls0 = lunchEv?.StartTime.TimeOfDay ?? desiredLs;
            var le0 = lunchEv?.EndTime.TimeOfDay ?? desiredLe;

            bool Inside(TimeSpan ls, TimeSpan le) => le > ls && ls >= arr && le <= dep;
            const double EPS = 1e-6;

            var (ls, le) = RefitLunch(day, arr, dep, ls0, le0, desiredLen, employeeId);
            var movedOut = WorkTransferReportingService.GetMovedOut(day);

            if (le <= ls)
            {
                if (!TryCarveLunchFromInnerAuto(day, arr, dep, desiredLen, out ls, out le, employeeId))
                {
                    bool canStart = CanTrimStart(day, employeeId);
                    bool canEnd = CanTrimEnd(day, employeeId);

                    if (movedOut > EPS)
                    {
                        ls = le = TimeSpan.Zero;
                    }
                    else
                    {
                        if (canEnd)
                        {
                            ExtendEndAuto(day, desiredLen.TotalHours, employeeId);
                            var us2 = SettingsService.GetResolvedDaySettings(day, employeeId);
                            dep = us2.DepartureTime;
                            (ls, le) = RefitLunch(day, arr, dep, desiredLs, desiredLe, desiredLen, employeeId);
                        }
                        else if (canStart)
                        {
                            ExtendStartAuto(day, desiredLen.TotalHours, employeeId);
                            var us2 = SettingsService.GetResolvedDaySettings(day, employeeId);
                            arr = us2.ArrivalTime;
                            (ls, le) = RefitLunch(day, arr, dep, desiredLs, desiredLe, desiredLen, employeeId);
                        }
                    }
                }
            }

            if (Inside(ls, le))
            {
                SettingsService.SaveDaySettingsForDate(day, arr, dep, ls, le, employeeId);
                ReplaceLunchEvent(day, ls, le, employeeId);
                SplitAutoWorkAroundLunch(day, ls, le, employeeId);

                if (callRegenerate)
                {
                    var gen = new AutomaticEventsGeneratorService(this, _ => Task.FromResult(false), employeeId);
                    await gen.RegenerateDailyEventsAsync(day);
                }
            }
            else
            {
                SettingsService.SaveDaySettingsForDate(day, arr, dep, TimeSpan.Zero, TimeSpan.Zero, employeeId);
                ReplaceLunchEvent(day, TimeSpan.Zero, TimeSpan.Zero, employeeId);
            }
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
                .Where(d => d.Date != lockedDay.Date && (CanTrimStart(d, employeeId) || CanTrimEnd(d, employeeId)))
                .OrderBy(d => d)
                .ToList();

            if (donors.Count == 0)
                return 0;

            var preferEnd = donors.ToDictionary(d => d, _ => true);
            var touched = new HashSet<DateTime>();
            int i = 0, guard = 0;

            while (leftMin >= QUANTUM_MIN && donors.Count > 0 && guard++ < 2000)
            {
                var d = donors[i % donors.Count];
                i++;

                double cut = 0;
                if (preferEnd[d] && CanTrimEnd(d, employeeId))
                    cut = TrimEndAuto(d, QMinutes(QUANTUM_MIN), employeeId);

                if (cut <= EPS && CanTrimStart(d, employeeId))
                    cut = TrimStartAuto(d, QMinutes(QUANTUM_MIN), employeeId);

                if (cut <= EPS && CanTrimEnd(d, employeeId))
                    cut = TrimEndAuto(d, QMinutes(QUANTUM_MIN), employeeId);

                int gotMin = RoundDownToQuantum((int)Math.Round(cut * 60.0));
                if (gotMin <= 0)
                {
                    if (!(CanTrimStart(d, employeeId) || CanTrimEnd(d, employeeId)))
                        donors.Remove(d);

                    continue;
                }

                leftMin -= gotMin;
                preferEnd[d] = !preferEnd[d];
                touched.Add(d);

                WorkTransferReportingService.AddTransfer(d, lockedDay, gotMin / 60.0);
            }

            var gen = new AutomaticEventsGeneratorService(this, _ => Task.FromResult(false), employeeId);
            foreach (var d in touched)
            {
                await gen.RegenerateDailyEventsAsync(d, preserveUserSettings: false);
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
                await gen.RegenerateDailyEventsAsync(day, preserveUserSettings: false);
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

        private async Task BalanceOneWeekAsync(List<DateTime> weekDays, int employeeId = DefaultEmployeeId)
        {
            const double EPS = 1e-6;

            WorkTransferReportingService.ResetWeek(weekDays);
            var calc = new WorkingHoursCalculatorService();

            var meta = weekDays.ToDictionary(d => d, d =>
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

            var touched = new HashSet<DateTime>();

            double CutFairOneStep(DateTime day, double planHours)
            {
                double cut = 0.0;
                bool canS = CanTrimStart(day, employeeId);
                bool canE = CanTrimEnd(day, employeeId);

                if (!canS && !canE)
                    return 0.0;

                if (planHours >= 1.0 && canS && canE)
                {
                    cut += TrimStartAuto(day, 0.5, employeeId);
                    cut += TrimEndAuto(day, 0.5, employeeId);

                    var left = 1.0 - cut;
                    if (left > EPS)
                    {
                        if (CanTrimEnd(day, employeeId)) cut += TrimEndAuto(day, left, employeeId);
                        else if (CanTrimStart(day, employeeId)) cut += TrimStartAuto(day, left, employeeId);
                    }
                }
                else
                {
                    double req = (planHours >= 1.0) ? 1.0 : 0.5;
                    if (canE) cut += TrimEndAuto(day, req, employeeId);
                    else if (canS) cut += TrimStartAuto(day, req, employeeId);
                }

                if (cut > EPS)
                {
                    meta[day].CanStart = CanTrimStart(day, employeeId);
                    meta[day].CanEnd = CanTrimEnd(day, employeeId);
                    touched.Add(day);
                }

                return cut;
            }

            bool progress = true;
            while (progress)
            {
                progress = false;

                foreach (var d in weekDays.OrderBy(x => x))
                {
                    var md = meta[d];
                    if (md.Extra <= EPS) continue;
                    if (!(md.CanStart || md.CanEnd)) continue;

                    var plan = Math.Min(1.0, md.Extra);
                    var cut = CutFairOneStep(d, plan);

                    if (cut > EPS)
                    {
                        md.Extra = Math.Max(0, md.Extra - cut);
                        meta[d] = md;
                        progress = true;
                    }
                }
            }

            foreach (var kv in meta.Where(x => x.Value.Locked && x.Value.Extra > EPS).OrderBy(x => x.Key))
            {
                var lockedDay = kv.Key;
                var extra = kv.Value.Extra;

                var transferred = await TransferLockedOvertimeAsync(lockedDay, extra, weekDays, employeeId);
                meta[lockedDay].Extra = Math.Max(0, extra - transferred);
            }

            var gen = new AutomaticEventsGeneratorService(this, _ => Task.FromResult(false), employeeId);
            foreach (var d in touched)
            {
                await gen.RegenerateDailyEventsAsync(d, preserveUserSettings: false);
                await EnsureLunchInsideWorkWindowAsync(employeeId, d, callRegenerate: false);
            }
        }

        private async Task BalanceTwoWeeksAsync(List<DateTime> w1, List<DateTime> w2, int employeeId = DefaultEmployeeId)
        {
            var all = w1.Concat(w2).OrderBy(d => d).ToList();
            await BalanceOneWeekAsync(all, employeeId);
        }

        public async Task BalanceEventsForMonthAsync(int year, int month, Func<string, Task<bool>> askCollision, int employeeId = DefaultEmployeeId)
        {
            var first = new DateTime(year, month, 1);
            var last = new DateTime(year, month, DateTime.DaysInMonth(year, month));

            var monthDays = Enumerable.Range(0, (last - first).Days + 1)
                .Select(i => first.AddDays(i))
                .Where(d => d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday && !HolidayHelper.IsCzechHoliday(d))
                .ToList();

            WorkTransferReportingService.ResetWeek(monthDays);
            if (monthDays.Count == 0)
                return;

            int IsoW(DateTime d) => System.Globalization.ISOWeek.GetWeekOfYear(d);
            int IsoY(DateTime d) => System.Globalization.ISOWeek.GetYear(d);

            var weekGroups = monthDays.GroupBy(d => (IsoY(d), IsoW(d))).OrderBy(g => g.Key).ToList();

            bool IsPartialMonthWeek((int Y, int W) key)
            {
                var mon = (from i in Enumerable.Range(0, 7)
                           let any = FirstDayOfIsoWeek(key.Y, key.W).AddDays(i)
                           select any).ToList();

                return mon.Any(d => d.Month != month);
            }

            foreach (var g in weekGroups)
            {
                var days = g.Where(d => d.Month == month).OrderBy(d => d).ToList();
                await BalanceOneWeekAsync(days, employeeId);
            }

            if (weekGroups.Count >= 2 && IsPartialMonthWeek(weekGroups.First().Key) && IsPartialMonthWeek(weekGroups.Last().Key))
            {
                var firstWeekDays = weekGroups.First().Where(d => d.Month == month).ToList();
                var lastWeekDays = weekGroups.Last().Where(d => d.Month == month).ToList();
                await BalanceTwoWeeksAsync(firstWeekDays, lastWeekDays, employeeId);
            }

            await PostNormalizeMonthAsync(year, month, employeeId);
        }

        public async Task BalanceWeekForDateAsync(DateTime anyDate, int employeeId = DefaultEmployeeId)
        {
            if (_isBalancingNow)
                return;

            try
            {
                _isBalancingNow = true;

                var days = GetWeekWorkdays(anyDate);
                if (days.Count == 0)
                    return;

                WorkTransferReportingService.ResetWeek(days);
                await BalanceOneWeekAsync(days, employeeId);

                foreach (var g in days.GroupBy(d => new { d.Year, d.Month }))
                    await PostNormalizeMonthAsync(g.Key.Year, g.Key.Month, employeeId);
            }
            finally
            {
                _isBalancingNow = false;
            }
        }

        public async Task BalanceForChangedRangeAsync(DateTime startIncl, DateTime endIncl, int employeeId = DefaultEmployeeId)
        {
            if (_isBalancingNow)
                return;

            try
            {
                _isBalancingNow = true;

                var days = Enumerable.Range(0, (endIncl.Date - startIncl.Date).Days + 1)
                    .Select(i => startIncl.Date.AddDays(i))
                    .Where(IsWorkday)
                    .ToList();

                if (days.Count == 0)
                    return;

                var groups = days.GroupBy(d => (System.Globalization.ISOWeek.GetYear(d),
                                                System.Globalization.ISOWeek.GetWeekOfYear(d)))
                                 .OrderBy(g => g.Key);

                foreach (var g in groups)
                {
                    var weekDays = g.OrderBy(d => d).ToList();
                    WorkTransferReportingService.ResetWeek(weekDays);
                    await BalanceOneWeekAsync(weekDays, employeeId);
                }

                foreach (var m in days.Select(d => (d.Year, d.Month)).Distinct())
                    await PostNormalizeMonthAsync(m.Year, m.Month, employeeId);
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

                bool canStart = CanTrimStart(day, employeeId);
                bool canEnd = CanTrimEnd(day, employeeId);
                var movedOut = WorkTransferReportingService.GetMovedOut(day);

                if (total < 8.0 - EPS && (canStart || canEnd) && movedOut <= EPS)
                {
                    double need = 8.0 - total;
                    double placed = 0;

                    if (canEnd) placed += ExtendEndAuto(day, need - placed, employeeId);
                    if (need - placed > EPS && canStart) placed += ExtendStartAuto(day, need - placed, employeeId);

                    var gen = new AutomaticEventsGeneratorService(this, _ => Task.FromResult(false), employeeId);
                    await gen.RegenerateDailyEventsAsync(day);
                    await EnsureLunchInsideWorkWindowAsync(employeeId, day);

                    all = GetEventsForDay(employeeId, day);
                    m = calc.DailyMetrics(day, all);
                    total = m.worked;
                }

                if (!hasLunch && hasAnyCredited && (canStart || canEnd) && movedOut <= EPS)
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
                            double placed = 0;

                            if (canEnd) placed += ExtendEndAuto(day, need - placed, employeeId);
                            if (need - placed > EPS && canStart) placed += ExtendStartAuto(day, need - placed, employeeId);

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
    }
}