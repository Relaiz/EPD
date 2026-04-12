using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using TeacherScheduleApp.Helpers;
using TeacherScheduleApp.Messages;
using TeacherScheduleApp.Models;

namespace TeacherScheduleApp.Services
{
    public class AutomaticEventsGeneratorService
    {
        private readonly EventService _eventService;
        private readonly Func<string, Task<bool>> _askCollision;
        private bool? _moveAllLunchesForAllDays = null;
        private readonly int _employeeId;
        private sealed record ManualSlice(Event Event, DateTime S, DateTime E);

        public AutomaticEventsGeneratorService(
            EventService eventService,
            Func<string, Task<bool>> askCollision,
            int employeeId = 1)
        {
            _eventService = eventService;
            _askCollision = askCollision;
            _employeeId = employeeId;
        }

        public SemesterType GetSemesterForDate(DateTime date)
            => GlobalSettingsService.GetSemesterForDate(date);

        private static bool IsWorkLike(EventType t)
            => t == EventType.Work || t == EventType.BusinessTrip;

        private static bool IsLunch(EventType t)
            => t == EventType.Lunch;

        private static bool IsSpecial(EventType t)
            => !IsWorkLike(t) && !IsLunch(t);

        public sealed record LunchPreparationResult(bool Ok, Event? Event, string? Error);

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

        private static bool Overlaps(DateTime aS, DateTime aE, DateTime bS, DateTime bE)
            => aS < bE && bS < aE;

        public async Task<LunchPreparationResult> PrepareManualLunchAsync(Event proposed)
        {
            if (proposed.EventType != EventType.Lunch)
                return new LunchPreparationResult(true, proposed, null);

            if (proposed.AllDay)
                return new LunchPreparationResult(false, null, "Oběd nemůže být celodenní.");

            if (proposed.StartTime.Date != proposed.EndTime.Date)
                return new LunchPreparationResult(false, null, "Oběd musí být v rámci jednoho dne.");

            if (proposed.EndTime <= proposed.StartTime)
                return new LunchPreparationResult(false, null, "Konec oběda musí být po začátku.");

            var day = proposed.StartTime.Date;
            var resolved = SettingsService.GetResolvedDaySettings(day, _employeeId);

            var arrival = day + resolved.ArrivalTime;
            var departure = day + resolved.DepartureTime;
            var gross = departure - arrival;

            int targetLunchCount = GetTargetLunchCount(gross);
            if (targetLunchCount == 0)
                return new LunchPreparationResult(false, null, "Pro den kratší než 8 hodin se oběd nevytváří.");

            var duration = proposed.EndTime - proposed.StartTime;
            var maxLunchLen = ParseDurationOrDefault(resolved.MaxBreakDuration, TimeSpan.FromMinutes(30));

            if (duration > maxLunchLen)
            {
                return new LunchPreparationResult(
                    false,
                    null,
                    $"Oběd nesmí být delší než {maxLunchLen:hh\\:mm}.");
            }

            var dayEvents = _eventService.GetEventsForDay(_employeeId, day)
                .Where(e => !e.IsDeleted)
                .Where(e => e.Id != proposed.Id && e.ParentEventId != proposed.Id)
                .OrderBy(e => e.StartTime)
                .ToList();

            int existingLunchCount = dayEvents.Count(e => e.EventType == EventType.Lunch);
            if (existingLunchCount >= targetLunchCount)
            {
                return new LunchPreparationResult(
                    false,
                    null,
                    targetLunchCount == 1
                        ? "V tomto dni už jeden oběd existuje."
                        : "V tomto dni už existují dva obědy.");
            }

            DateTime minStartByRule =
                existingLunchCount == 0
                    ? Max(arrival.AddHours(4), day + resolved.LunchStart)
                    : dayEvents
                        .Where(e => e.EventType == EventType.Lunch)
                        .Max(e => e.EndTime)
                        .AddHours(4);

            var desiredStart = proposed.StartTime < minStartByRule
                ? minStartByRule
                : proposed.StartTime;

            var desiredEnd = desiredStart + duration;

            if (desiredEnd > departure)
            {
                return new LunchPreparationResult(
                    false,
                    null,
                    "Pro oběd už v rámci pracovní doby nezbývá místo.");
            }

            static (DateTime S, DateTime E) ClipToDay(DateTime ds, DateTime de, DateTime s, DateTime e)
            {
                var cs = s < ds ? ds : s;
                var ce = e > de ? de : e;
                return ce > cs ? (cs, ce) : (cs, cs);
            }

            var manual = dayEvents
                .Where(e => !e.IsAutoGenerated)
                .Select(e =>
                {
                    var c = ClipToDay(day, day.AddDays(1), e.StartTime, e.EndTime);
                    return new ManualSlice(e, c.S, c.E);
                })
                .Where(x => x.E > x.S)
                .ToList();

            var placement = await ResolveLunchPlacementAsync(
                day,
                arrival,
                departure,
                desiredStart,
                desiredEnd,
                manual);

            if (placement.end <= placement.start || placement.end > departure)
            {
                return new LunchPreparationResult(
                    false,
                    null,
                    "Oběd se nepodařilo umístit bez kolize do pracovní doby.");
            }

            var prepared = new Event
            {
                Id = proposed.Id,
                EmployeeId = proposed.EmployeeId,
                Title = string.IsNullOrWhiteSpace(proposed.Title) ? "Oběd" : proposed.Title,
                Description = proposed.Description,
                EventType = proposed.EventType,
                AllDay = false,
                StartTime = placement.start,
                EndTime = placement.end,
                IsDeleted = false,
                HasCollision = placement.wasCollision,
                ParentEventId = proposed.ParentEventId,
                ImportBatchId = proposed.ImportBatchId,
                IsAutoGenerated = proposed.IsAutoGenerated,
                AutoGeneratedForDate = proposed.AutoGeneratedForDate
            };

            return new LunchPreparationResult(true, prepared, null);
        }

        public async Task RegenerateAllAutoEventsForSemester(SemesterType sem)
        {
            var allDates = _eventService
                .GetAllEvents(_employeeId)
                .Where(e => e.IsAutoGenerated
                            && e.AutoGeneratedForDate.HasValue
                            && GetSemesterForDate(e.AutoGeneratedForDate.Value) == sem)
                .Select(e => e.AutoGeneratedForDate!.Value.Date)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            foreach (var date in allDates)
                await RegenerateDailyEventsAsync(date);
        }

        public async Task RegenerateRangeEventsAsync(DateTime start, DateTime end, bool preserveUserSettings = false)
        {
            _moveAllLunchesForAllDays = null;

            for (var d = start.Date; d <= end.Date; d = d.AddDays(1))
                await RegenerateDailyEventsAsync(d, preserveUserSettings);

           // await _eventService.BalanceForChangedRangeAsync(start, end, _employeeId);
            MessageBus.Current.SendMessage(new AutoEventsGeneratedMessage());
        }

        public async Task RegenerateDailyEventsAsync(DateTime date, bool preserveUserSettings = false)
        {
            var day = date.Date;
            var dayStart = day;
            var dayEnd = day.AddDays(1);

            var calc = new WorkingHoursCalculatorService();

            _eventService.RemoveAutoGeneratedEvents(_employeeId, day);

            static (DateTime S, DateTime E) ClipToDay(DateTime ds, DateTime de, DateTime s, DateTime e)
            {
                var cs = s < ds ? ds : s;
                var ce = e > de ? de : e;
                return ce > cs ? (cs, ce) : (cs, cs);
            }

            if (HolidayHelper.IsCzechHoliday(day) || day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                var manualFullDay = _eventService
                    .GetEventsForDay(_employeeId, day)
                    .Where(e => !e.IsAutoGenerated && e.AllDay && e.EventType is not EventType.Work and not EventType.Lunch)
                    .ToList();

                foreach (var ev in manualFullDay)
                    _eventService.DeleteEvent(ev.Id);

                return;
            }

            var resolved = preserveUserSettings
                ? SettingsService.GetResolvedDaySettings(day, _employeeId)
                : SettingsService.GetResolvedDaySettingsIgnoringComputed(day, _employeeId);

            var dayOverride = SettingsService.GetManualDaySettingsForDate(day, _employeeId);

            await NormalizeLongManualWorkEventsAsync(day);

            var manualRaw = _eventService
                .GetEventsForDay(_employeeId, day)
                .Where(e => !e.IsAutoGenerated)
                .OrderBy(e => e.StartTime)
                .ToList();

            var manual = manualRaw
                .Select(e =>
                {
                    var c = ClipToDay(dayStart, dayEnd, e.StartTime, e.EndTime);
                    return new ManualSlice(e, c.S, c.E);
                })
                .Where(x => x.E > x.S)
                .ToList();

            for (int i = 0; i < manual.Count; i++)
            {
                for (int j = i + 1; j < manual.Count; j++)
                {
                    if (Overlaps(
                        manual[i].S.TimeOfDay, manual[i].E.TimeOfDay,
                        manual[j].S.TimeOfDay, manual[j].E.TimeOfDay))
                    {
                        manual[j].Event.HasCollision = true;
                        _eventService.UpdateEvent(manual[j].Event, suppressRegen: true);
                    }
                }
            }

            DateTime arrival = day + resolved.ArrivalTime;
            DateTime departure = day + resolved.DepartureTime;

            var manualWork = manual.Where(x => IsWorkLike(x.Event.EventType)).ToList();

            if (manualWork.Any())
            {
                var first = manualWork.Min(x => x.S);
                var last = manualWork.Max(x => x.E);

                if (first < arrival) arrival = first;
                if (last > departure) departure = last;
            }

            var specials = manual
                .Where(x => IsSpecial(x.Event.EventType))
                .Select(x => (S: x.S, E: x.E, T: x.Event.EventType))
                .ToList();

            TimeSpan IntersectLen((DateTime S, DateTime E, EventType T) e)
            {
                var s = e.S < arrival ? arrival : e.S;
                var ee = e.E > departure ? departure : e.E;
                return ee > s ? (ee - s) : TimeSpan.Zero;
            }

            var gross = departure - arrival;
            var eight = TimeSpan.FromHours(8);

            var configuredLunchLen =
                resolved.LunchEnd > resolved.LunchStart
                    ? resolved.LunchEnd - resolved.LunchStart
                    : TimeSpan.FromMinutes(30);

            var maxLunchLen = ParseDurationOrDefault(resolved.MaxBreakDuration, TimeSpan.FromMinutes(30));
            var lunchLen = configuredLunchLen <= TimeSpan.Zero ? TimeSpan.FromMinutes(30) : configuredLunchLen;
            if (lunchLen > maxLunchLen)
                lunchLen = maxLunchLen;

            int targetLunchCount =
                dayOverride != null && dayOverride.LunchStart == dayOverride.LunchEnd
                    ? 0
                    : GetTargetLunchCount(gross);

            bool hasManualWorkLike = manual.Any(x => IsWorkLike(x.Event.EventType));

            bool fullSpecialDay =
                !hasManualWorkLike &&
                (
                    specials.Any(sp => sp.T == EventType.Vacation && IntersectLen(sp) >= eight) ||
                    specials.Any(sp => sp.T != EventType.Vacation &&
                                       IntersectLen(sp) >= (gross - TimeSpan.FromTicks(lunchLen.Ticks * Math.Max(1, targetLunchCount))))
                );

            if (fullSpecialDay)
            {
                foreach (var e in _eventService.GetEventsForDay(_employeeId, day)
                             .Where(e => e.EventType == EventType.Lunch && !e.IsAutoGenerated)
                             .ToList())
                {
                    _eventService.DeleteEvent(e.Id);
                }

                SaveDaySettingsFromEvents(day);
                return;
            }

            var manualLunches = manual
                .Where(x => x.Event.EventType == EventType.Lunch)
                .OrderBy(x => x.S)
                .ToList();

            var lunches = new List<(DateTime S, DateTime E, bool IsManual, bool HasCollision, string Title)>();

            foreach (var ml in manualLunches)
            {
                var s = ml.S < arrival ? arrival : ml.S;
                var e = ml.E > departure ? departure : ml.E;

                if (e <= s)
                    continue;

                lunches.Add((s, e, true, ml.Event.HasCollision, ml.Event.Title));
            }

            int missingAutoLunches = Math.Max(0, targetLunchCount - lunches.Count);

            for (int i = 0; i < missingAutoLunches; i++)
            {
                DateTime desiredStart =
                    lunches.Count == 0
                        ? Max(arrival.AddHours(4), day + resolved.LunchStart)
                        : lunches.OrderBy(x => x.E).Last().E.AddHours(4);

                DateTime desiredEnd = desiredStart + lunchLen;

                if (desiredEnd > departure)
                    break;

                var placement = await ResolveLunchPlacementAsync(
                    day,
                    arrival,
                    departure,
                    desiredStart,
                    desiredEnd,
                    manual);

                if (placement.end <= placement.start || placement.end > departure)
                    break;

                lunches.Add((
                    placement.start,
                    placement.end,
                    false,
                    placement.wasCollision,
                    resolved.AutoEventNameLunch));
            }

            lunches = lunches
                .OrderBy(x => x.S)
                .ToList();

            var newEvents = new List<Event>();

            foreach (var lunch in lunches.Where(x => !x.IsManual))
            {
                newEvents.Add(new Event
                {
                    EmployeeId = _employeeId,
                    Title = lunch.Title,
                    StartTime = lunch.S,
                    EndTime = lunch.E,
                    EventType = EventType.Lunch,
                    IsAutoGenerated = true,
                    AutoGeneratedForDate = day,
                    HasCollision = lunch.HasCollision
                });
            }

            var busyCandidates = new List<(DateTime Start, DateTime End)>();
            busyCandidates.AddRange(manual.Select(l => (l.S, l.E)));
            busyCandidates.AddRange(lunches.Where(x => !x.IsManual).Select(x => (x.S, x.E)));

            var busy = MergeIntervals(busyCandidates);

            var gaps = new List<(DateTime s, DateTime e)>();
            var cursor = arrival;

            foreach (var b in busy)
            {
                if (cursor >= departure)
                    break;

                var gapEnd = Min(b.start, departure);
                if (gapEnd > cursor)
                    gaps.Add((cursor, gapEnd));

                cursor = Max(cursor, b.end);
            }

            if (cursor < departure)
                gaps.Add((cursor, departure));

            var firstLunchEnd = lunches.Any()
                ? lunches.OrderBy(x => x.E).First().E
                : DateTime.MinValue;

            foreach (var (s, e) in gaps)
            {
                if ((e - s) <= TimeSpan.FromMinutes(1))
                    continue;

                newEvents.Add(new Event
                {
                    EmployeeId = _employeeId,
                    Title = lunches.Any() && s >= firstLunchEnd
                            ? resolved.AutoEventNamePostLunch
                            : resolved.AutoEventNamePreLunch,
                    StartTime = s,
                    EndTime = e,
                    EventType = EventType.Work,
                    IsAutoGenerated = true,
                    AutoGeneratedForDate = day
                });
            }

            foreach (var ev in newEvents.Where(x => (x.EndTime - x.StartTime) > TimeSpan.FromMinutes(1)))
                _eventService.CreateAutoEvent(ev);

            var dm0 = calc.DailyMetrics(day, _eventService.GetEventsForDay(_employeeId, day));
            var net0 = dm0.workInclBT;

            if (!preserveUserSettings && net0 > 12.0 + 1e-6)
            {
                await _eventService.TrimOvertimeByAutoBlocksAsync(_employeeId, day, preserveUserSettings);
            }

            if (!preserveUserSettings)
                SaveDaySettingsFromEvents(day);
        }

        private async Task<(DateTime start, DateTime end, bool wasCollision)> ResolveLunchPlacementAsync(
            DateTime day,
            DateTime arrival,
            DateTime departure,
            DateTime desiredLunchStart,
            DateTime desiredLunchEnd,
            List<ManualSlice> manual)
        {
            var lunchStart = desiredLunchStart;
            var lunchEnd = desiredLunchEnd;

            if (lunchStart < arrival) lunchStart = arrival;
            if (lunchEnd > departure) lunchEnd = departure;

            if (lunchEnd <= lunchStart)
                return (day, day, false);

            var overlapping = manual
                .Where(l => l.Event.EventType != EventType.Lunch)
                .Where(l => Overlaps(lunchStart.TimeOfDay, lunchEnd.TimeOfDay, l.S.TimeOfDay, l.E.TimeOfDay))
                .ToList();

            bool lunchCollision = overlapping.Any();

            if (!lunchCollision)
                return (lunchStart, lunchEnd, false);

            if (!_moveAllLunchesForAllDays.HasValue)
            {
                _moveAllLunchesForAllDays = await _askCollision(
                    $"Oběd {lunchStart:HH\\:mm}-{lunchEnd:HH\\:mm} se překrývá s události. Přesunout všechny obědy s kolizemi hned?");
            }

            if (_moveAllLunchesForAllDays.Value)
            {
                var duration = lunchEnd - lunchStart;
                var probeStart = lunchStart;
                var probeEnd = lunchEnd;

                while (true)
                {
                    var overlaps = manual
                        .Where(l => l.Event.EventType != EventType.Lunch)
                        .Where(l => Overlaps(probeStart.TimeOfDay, probeEnd.TimeOfDay, l.S.TimeOfDay, l.E.TimeOfDay))
                        .ToList();

                    if (!overlaps.Any())
                        break;

                    var lastEnd = overlaps.Max(l => (DateTime)l.E);
                    probeStart = lastEnd;
                    probeEnd = probeStart + duration;

                    if (probeEnd > departure)
                        return (day, day, true);
                }

                return (probeStart, probeEnd, true);
            }
            else
            {
                var duration = lunchEnd - lunchStart;
                var probeStart = lunchStart;
                var probeEnd = lunchEnd;

                foreach (var m in overlapping)
                {
                    var ok = await _askCollision(
                        $"Oběd {probeStart:HH\\:mm}-{probeEnd:HH\\:mm} se překrývá s událostí “{m.Event.Title}” dne {m.S:dd.MM.yyyy} ({m.S:HH\\:mm}-{m.E:HH\\:mm}). Přesunout oběd hned po této události?");

                    if (!ok)
                        continue;

                    probeStart = m.E;
                    probeEnd = probeStart + duration;
                }

                while (true)
                {
                    var overlaps = manual
                        .Where(l => l.Event.EventType != EventType.Lunch)
                        .Where(l => Overlaps(probeStart.TimeOfDay, probeEnd.TimeOfDay, l.S.TimeOfDay, l.E.TimeOfDay))
                        .ToList();

                    if (!overlaps.Any())
                        break;

                    var lastEnd = overlaps.Max(l => (DateTime)l.E);
                    probeStart = lastEnd;
                    probeEnd = probeStart + duration;

                    if (probeEnd > departure)
                        return (day, day, true);
                }

                return (probeStart, probeEnd, true);
            }
        }

        private void SaveDaySettingsFromEvents(DateTime day)
        {
            var evs = _eventService.GetEventsForDay(_employeeId, day)
                .Where(e => !e.IsDeleted)
                .OrderBy(e => e.StartTime)
                .ToList();

            var baseResolved = SettingsService.GetResolvedDaySettingsIgnoringComputed(day, _employeeId);
            var manualOverride = SettingsService.GetManualDaySettingsForDate(day, _employeeId);

            bool IsWorkLike(Event e) => e.EventType == EventType.Work || e.EventType == EventType.BusinessTrip;
            bool IsSpecial(Event e) => e.EventType != EventType.Lunch && !IsWorkLike(e);
            bool IsCreditedManual(Event e) =>
                !e.IsDeleted &&
                !e.IsAutoGenerated &&
                (IsWorkLike(e) || IsSpecial(e));

            var manualCredited = evs.Where(IsCreditedManual).ToList();
            var manualSpecials = manualCredited.Where(IsSpecial).ToList();
            var manualLunch = evs
               .Where(e => !e.IsDeleted && e.EventType == EventType.Lunch)
               .OrderBy(e => e.StartTime)
               .FirstOrDefault();

            var arr = baseResolved.ArrivalTime;
            var dep = baseResolved.DepartureTime;

            if (manualCredited.Any())
            {
                var minManual = manualCredited.Min(e => e.StartTime).TimeOfDay;
                var maxManual = manualCredited.Max(e => e.EndTime).TimeOfDay;

                if (minManual < arr) arr = minManual;
                if (maxManual > dep) dep = maxManual;
            }

            bool isFullManualSpecialDay()
            {
                if (manualSpecials.Count == 0)
                    return false;

                var winS = day + baseResolved.ArrivalTime;
                var winE = day + baseResolved.DepartureTime;
                var eight = TimeSpan.FromHours(8);

                foreach (var s in manualSpecials)
                {
                    var ss = s.StartTime < winS ? winS : s.StartTime;
                    var se = s.EndTime > winE ? winE : s.EndTime;
                    var len = se > ss ? se - ss : TimeSpan.Zero;

                    if (s.EventType == EventType.Vacation && len >= eight)
                        return true;

                    if (len >= (winE - winS))
                        return true;
                }

                return false;
            }

            TimeSpan ls = TimeSpan.Zero;
            TimeSpan le = TimeSpan.Zero;

            if (!isFullManualSpecialDay())
            {
                if (manualLunch != null)
                {
                    var mls = manualLunch.StartTime.TimeOfDay;
                    var mle = manualLunch.EndTime.TimeOfDay;

                    if (mle > mls && mls >= arr && mle <= dep)
                    {
                        ls = mls;
                        le = mle;
                    }
                }
                else if (manualOverride != null &&
                         manualOverride.LunchEnd > manualOverride.LunchStart &&
                         manualOverride.LunchStart >= arr &&
                         manualOverride.LunchEnd <= dep)
                {
                    ls = manualOverride.LunchStart;
                    le = manualOverride.LunchEnd;
                }
                else if (baseResolved.LunchEnd > baseResolved.LunchStart &&
                         baseResolved.LunchStart >= arr &&
                         baseResolved.LunchEnd <= dep)
                {
                    ls = baseResolved.LunchStart;
                    le = baseResolved.LunchEnd;
                }
            }

            SettingsService.SaveDaySettingsForDate(day, arr, dep, ls, le, _employeeId);
        }

        private static bool Overlaps(TimeSpan a0, TimeSpan a1, TimeSpan b0, TimeSpan b1)
            => a0 < b1 && b0 < a1;

        private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
        private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

        private List<(DateTime start, DateTime end)> MergeIntervals(List<(DateTime start, DateTime end)> intervals)
        {
            var sorted = intervals.OrderBy(x => x.start).ToList();
            var merged = new List<(DateTime start, DateTime end)>();

            foreach (var seg in sorted)
            {
                if (merged.Count == 0 || merged[^1].end < seg.start)
                    merged.Add(seg);
                else
                    merged[^1] = (
                        merged[^1].start,
                        merged[^1].end > seg.end ? merged[^1].end : seg.end
                    );
            }

            return merged;
        }

        private static readonly TimeSpan FourHours = TimeSpan.FromHours(4);
        private static readonly TimeSpan LunchDuration = TimeSpan.FromMinutes(30);

        private async Task NormalizeLongManualWorkEventsAsync(DateTime day)
        {
            await Task.CompletedTask;
        }    
    }
}