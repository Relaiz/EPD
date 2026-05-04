using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TeacherScheduleApp.Helpers;
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
        private sealed record AutoWorkGap(DateTime S, DateTime E, bool FillFromEnd);
        private const int QUANTUM_MIN = 5;
        private const int DAY_NORM_MIN = 8 * 60;

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
            => t.IsAutoAdjustableWork();

        private static bool IsTeaching(EventType t)
            => t.IsTeaching();

        private static bool IsLunch(EventType t)
            => t == EventType.Lunch;

        private static bool IsSpecial(EventType t)
            => !IsCreditedWorkTime(t) && !IsLunch(t);

        private static bool IsCreditedWorkTime(EventType t)
            => t.IsCreditedWorkTime() || t == EventType.BusinessTrip;

        private static int RoundDownToQuantum(int minutes)
            => minutes - minutes % QUANTUM_MIN;

        private int SumMergedMinutes(IEnumerable<(DateTime start, DateTime end)> intervals)
        {
            return MergeIntervals(intervals.ToList())
                .Sum(x => Math.Max(0, (int)Math.Round((x.end - x.start).TotalMinutes)));
        }

        private int GetPaidSpecialCreditMinutes(List<ManualSlice> manual)
        {
            var paidAbsenceIntervals = manual
                .Where(x => IsPaidAbsence(x.Event.EventType))
                .Select(x => (start: x.S, end: x.E));

            return Math.Min(DAY_NORM_MIN, SumMergedMinutes(paidAbsenceIntervals));
        }

        private int GetEffectiveWorkMinutes(List<ManualSlice> slices)
        {
            int paidAbsenceMin = GetPaidSpecialCreditMinutes(slices);

            if (paidAbsenceMin >= DAY_NORM_MIN)
                return 0;

            var specialIntervals = slices
                .Where(x => IsSpecial(x.Event.EventType))
                .Select(x => (start: x.S, end: x.E))
                .ToList();

            var workIntervals = slices
                .Where(x =>
                    IsCreditedWorkTime(x.Event.EventType))
                .Select(x => (start: x.S, end: x.E))
                .ToList();

            var activeWork = SubtractIntervals(workIntervals, specialIntervals);

            return SumMergedMinutes(activeWork);
        }

        public sealed record LunchPreparationResult(bool Ok, Event? Event, string? Error);

        private static TimeSpan ParseDurationOrDefault(string? value, TimeSpan fallback)
        {
            return TimeSpan.TryParse(value, out var ts) && ts > TimeSpan.Zero
                ? ts
                : fallback;
        }

        private static int GetTargetLunchCount(TimeSpan gross)
        {
            if (gross > TimeSpan.FromHours(12))
                return 2;

            if (gross > TimeSpan.FromHours(4))
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
            await NormalizeLongManualWorkEventsAsync(day);
            var resolved = SettingsService.GetResolvedDaySettings(day, _employeeId);

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

            var lunchWindow = GetLunchPolicyWindow(day, resolved, manual);
            DateTime arrival = lunchWindow.Arrival;
            DateTime departure = lunchWindow.Departure;

            var gross = departure - arrival;
            var mergedManualWork = MergeIntervals(
                manual
                    .Where(x => IsWorkLike(x.Event.EventType))
                    .Select(x => (x.S, x.E))
                    .ToList());

            var manualWorkGross = TimeSpan.FromTicks(
                mergedManualWork.Sum(x => (x.end - x.start).Ticks));

            int targetLunchCount = Math.Max(
                GetTargetLunchCount(gross),
                GetManualWorkDrivenLunchCount(manualWorkGross));

            if (targetLunchCount == 0)
                return new LunchPreparationResult(false, null, "V tomto dni pro oběd nevzniká povolené místo.");

            var lunchLen = GetLunchLength(resolved);

            if (IsFullSpecialDayCore(day, arrival, departure, targetLunchCount, lunchLen, manual))
            {
                return new LunchPreparationResult(
                    false,
                    null,
                    "Při celodenní zvláštní události se oběd nevytváří.");
            }

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
                existingLunchCount == 0 ? GetPreferredFirstLunchStart(day, arrival, resolved, manual)
                    : dayEvents
                        .Where(e => e.EventType == EventType.Lunch)
                        .Max(e => e.EndTime)
                        .AddHours(4);

            if (existingLunchCount == 0)
            {
                minStartByRule = FindFirstPossibleLunchStart(
                    minStartByRule,
                    departure,
                    duration,
                    manual);
            }

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

            using var _settingsCache = SettingsService.BeginReadCache(_employeeId, allDates);
            using var _cache = _eventService.BeginEventReadCache(_employeeId, allDates);

            foreach (var date in allDates)
                await RegenerateDailyEventsAsync(date);
        }

        public async Task RegenerateRangeEventsAsync(
            DateTime start,
            DateTime end,
            bool preserveUserSettings = false,
            bool ensureLunchAfterGeneration = true)
        {
            _moveAllLunchesForAllDays = null;

            var days = Enumerable.Range(0, (end.Date - start.Date).Days + 1)
                .Select(i => start.Date.AddDays(i))
                .ToList();

            using var _settingsCache = SettingsService.BeginReadCache(_employeeId, days);
            using var _cache = _eventService.BeginEventReadCache(_employeeId, days);

            for (var d = start.Date; d <= end.Date; d = d.AddDays(1))
                await RegenerateDailyEventsAsync(d, preserveUserSettings, ensureLunchAfterGeneration);
        }

        public async Task RegenerateDailyEventsAsync(
            DateTime date,
            bool preserveUserSettings = false,
            bool ensureLunchAfterGeneration = true)
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
                    _eventService.DeleteEventRaw(ev.Id, _employeeId);

                return;
            }

            if (preserveUserSettings)
                DropInvalidComputedSettingsForAutoOnlyDay(day);

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

            var collidingManualIds = new HashSet<int>();

            for (int i = 0; i < manual.Count; i++)
            {
                for (int j = i + 1; j < manual.Count; j++)
                {
                    if (Overlaps(
                        manual[i].S.TimeOfDay, manual[i].E.TimeOfDay,
                        manual[j].S.TimeOfDay, manual[j].E.TimeOfDay))
                    {
                        if (manual[i].Event.EventType.ShouldShowCollisionAgainst(manual[j].Event.EventType))
                            collidingManualIds.Add(manual[i].Event.Id);

                        if (manual[j].Event.EventType.ShouldShowCollisionAgainst(manual[i].Event.EventType))
                            collidingManualIds.Add(manual[j].Event.Id);
                    }
                }
            }

            foreach (var slice in manual)
            {
                var shouldHaveCollision = collidingManualIds.Contains(slice.Event.Id);
                if (slice.Event.HasCollision != shouldHaveCollision)
                    _eventService.UpdateEventCollisionRaw(slice.Event.Id, shouldHaveCollision, _employeeId);
            }

            var lunchWindow = GetLunchPolicyWindow(day, resolved, manual);
            DateTime arrival = lunchWindow.Arrival;
            DateTime departure = lunchWindow.Departure;

            var lunchLen = GetLunchLength(resolved);

            int paidSpecialMin = GetPaidSpecialCreditMinutes(manual);
            int manualWorkMin = GetEffectiveWorkMinutes(manual);
            int remainingAutoWorkMin = Math.Max(0, DAY_NORM_MIN - paidSpecialMin - manualWorkMin);
            int targetLunchCount = GetManualWorkDrivenLunchCount(
                TimeSpan.FromMinutes(manualWorkMin + remainingAutoWorkMin));

            bool fullSpecialDay = IsFullSpecialDayCore(
                day,
                arrival,
                departure,
                targetLunchCount,
                lunchLen,
                manual);

            if (fullSpecialDay)
            {
                foreach (var e in _eventService.GetEventsForDay(_employeeId, day)
                    .Where(e => e.EventType == EventType.Lunch && !e.IsAutoGenerated)
                    .ToList())
                {
                    _eventService.DeleteEventRaw(e.Id, _employeeId);
                }

                SaveDaySettingsFromEvents(day, forceOverwriteManual: !preserveUserSettings);
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
                        ? FindFirstPossibleLunchStart(
                            GetPreferredFirstLunchStart(day, arrival, resolved, manual),
                            departure,
                            lunchLen,
                            manual)
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
            var gaps = BuildAutoWorkGaps(arrival, departure, busy);

            var firstLunchEnd = lunches.Any()
                ? lunches.OrderBy(x => x.E).First().E
                : DateTime.MinValue;

            foreach (var gap in gaps)
            {
                if (remainingAutoWorkMin <= 0)
                    break;

                if ((gap.E - gap.S) <= TimeSpan.FromMinutes(1))
                    continue;

                int takeMinutes = Math.Min(
                    remainingAutoWorkMin,
                    Math.Max(0, (int)Math.Round((gap.E - gap.S).TotalMinutes)));

                if (takeMinutes <= 1)
                    continue;

                var workStart = gap.FillFromEnd
                    ? gap.E.AddMinutes(-takeMinutes)
                    : gap.S;
                var workEnd = workStart.AddMinutes(takeMinutes);

                newEvents.Add(new Event
                {
                    EmployeeId = _employeeId,
                    Title = lunches.Any() && workStart >= firstLunchEnd
                            ? resolved.AutoEventNamePostLunch
                            : resolved.AutoEventNamePreLunch,
                    StartTime = workStart,
                    EndTime = workEnd,
                    EventType = EventType.Work,
                    IsAutoGenerated = true,
                    AutoGeneratedForDate = day
                });

                remainingAutoWorkMin -= takeMinutes;
            }

            var autoEventsToCreate = newEvents
                .Where(x => (x.EndTime - x.StartTime) > TimeSpan.FromMinutes(1))
                .ToList();

            if (autoEventsToCreate.Count > 0)
                _eventService.CreateAutoEventsBulk(autoEventsToCreate);

            _eventService.SplitWorkAroundExistingLunches(day, _employeeId);

            var dm0 = calc.DailyMetrics(day, _eventService.GetEventsForDay(_employeeId, day));

            var missingAfterLunchSplitMin = GetCreditAwareMissingAutoWorkMinutes(day);

            if (missingAfterLunchSplitMin >= QUANTUM_MIN)
            {
                FillAutoWorkGaps(
                    day,
                    arrival,
                    departure,
                    missingAfterLunchSplitMin,
                    resolved);

                dm0 = calc.DailyMetrics(day, _eventService.GetEventsForDay(_employeeId, day));
            }

            var net0 = dm0.workInclBT;

            if (!preserveUserSettings && net0 > 12.0 + 1e-6)
            {
                await _eventService.TrimOvertimeByAutoBlocksAsync(_employeeId, day, preserveUserSettings);
            }

            if (ensureLunchAfterGeneration)
                await _eventService.EnsureLunchInsideWorkWindowAsync(_employeeId, day, callRegenerate: false);

            if (!preserveUserSettings)
                SaveDaySettingsFromEvents(day, forceOverwriteManual: true);
        }

        private int FillAutoWorkGaps(
            DateTime day,
            DateTime arrival,
            DateTime departure,
            int minutesToFill,
            ResolvedDaySettings resolved)
        {
            var remaining = RoundDownToQuantum(minutesToFill);
            if (remaining < QUANTUM_MIN || departure <= arrival)
                return 0;

            var dayEvents = _eventService.GetEventsForDay(_employeeId, day)
                .Where(e => !e.IsDeleted)
                .OrderBy(e => e.StartTime)
                .ToList();

            var firstLunchEnd = dayEvents
                .Where(e => e.EventType == EventType.Lunch)
                .OrderBy(e => e.EndTime)
                .Select(e => e.EndTime)
                .FirstOrDefault();

            var busy = MergeIntervals(
                dayEvents
                    .Select(e => (
                        start: e.StartTime < arrival ? arrival : e.StartTime,
                        end: e.EndTime > departure ? departure : e.EndTime))
                    .Where(x => x.end > x.start)
                    .ToList());

            var gaps = BuildAutoWorkGaps(arrival, departure, busy);
            var toCreate = new List<Event>();

            foreach (var gap in gaps)
                FillGap(gap);

            if (toCreate.Count > 0)
                _eventService.CreateAutoEventsBulk(toCreate);

            return minutesToFill - remaining;

            void FillGap(AutoWorkGap gap)
            {
                if (gap.E <= gap.S || remaining < QUANTUM_MIN)
                    return;

                var available = RoundDownToQuantum((int)Math.Round((gap.E - gap.S).TotalMinutes));
                var take = Math.Min(remaining, available);

                if (take < QUANTUM_MIN)
                    return;

                var start = gap.FillFromEnd
                    ? gap.E.AddMinutes(-take)
                    : gap.S;

                toCreate.Add(new Event
                {
                    EmployeeId = _employeeId,
                    Title = firstLunchEnd != default && start >= firstLunchEnd
                        ? resolved.AutoEventNamePostLunch
                        : resolved.AutoEventNamePreLunch,
                    StartTime = start,
                    EndTime = start.AddMinutes(take),
                    EventType = EventType.Work,
                    IsAutoGenerated = true,
                    AutoGeneratedForDate = day
                });

                remaining -= take;
            }
        }

        private List<AutoWorkGap> BuildAutoWorkGaps(
            DateTime arrival,
            DateTime departure,
            List<(DateTime start, DateTime end)> busy)
        {
            var chronological = new List<AutoWorkGap>();
            var cursor = arrival;

            foreach (var b in busy)
            {
                if (cursor >= departure)
                    break;

                var gapEnd = Min(b.start, departure);
                if (gapEnd > cursor)
                {
                    var hasBusyAfter = busy.Any(x => x.start == gapEnd);
                    chronological.Add(new AutoWorkGap(cursor, gapEnd, hasBusyAfter));
                }

                cursor = Max(cursor, b.end);
            }

            if (cursor < departure)
            {
                var hasBusyAfter = busy.Any(x => x.start == departure);
                chronological.Add(new AutoWorkGap(cursor, departure, hasBusyAfter));
            }

            if (busy.Count <= 1 || chronological.Count <= 1)
                return chronological;

            return chronological
                .Select((gap, index) => new
                {
                    gap,
                    index,
                    IsInner = busy.Any(b => b.end == gap.S) &&
                              busy.Any(b => b.start == gap.E)
                })
                .OrderByDescending(x => x.IsInner)
                .ThenBy(x => x.index)
                .Select(x => x.gap)
                .ToList();
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
                .Where(l => !l.Event.EventType.IsAutoAdjustableWork())
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
                        .Where(l => !l.Event.EventType.IsAutoAdjustableWork())
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
                        .Where(l => !l.Event.EventType.IsAutoAdjustableWork())
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

        private void DropInvalidComputedSettingsForAutoOnlyDay(DateTime day)
        {
            var settings = SettingsService.GetDaySettingsForDate(day, _employeeId);
            if (settings == null || settings.IsManualOverride)
                return;

            var hasManualNonLunch = _eventService
                .GetEventsForDay(_employeeId, day)
                .Any(e =>
                    !e.IsDeleted &&
                    !e.IsAutoGenerated &&
                    e.EventType != EventType.Lunch);

            if (hasManualNonLunch)
                return;

            var baseResolved = SettingsService.GetResolvedDaySettingsIgnoringComputed(day, _employeeId);

            bool outsideBaseWindow =
                settings.ArrivalTime < baseResolved.ArrivalTime ||
                settings.DepartureTime > baseResolved.DepartureTime ||
                (settings.LunchEnd > settings.LunchStart &&
                 (settings.LunchStart < baseResolved.ArrivalTime ||
                  settings.LunchEnd > baseResolved.DepartureTime));

            if (outsideBaseWindow)
                SettingsService.DeleteComputedDaySettingsForDate(day, _employeeId);
        }

        private void SaveDaySettingsFromEvents(DateTime day, bool forceOverwriteManual = false)
        {
            var evs = _eventService.GetEventsForDay(_employeeId, day)
                .Where(e => !e.IsDeleted)
                .OrderBy(e => e.StartTime)
                .ToList();

            var baseResolved = SettingsService.GetResolvedDaySettingsIgnoringComputed(day, _employeeId);
            var manualOverride = SettingsService.GetManualDaySettingsForDate(day, _employeeId);

            bool IsCreditedManual(Event e) =>
                !e.IsDeleted &&
                !e.IsAutoGenerated &&
                e.EventType != EventType.Lunch &&
                IsCreditedWorkTime(e.EventType);

            var manualCredited = evs.Where(IsCreditedManual).ToList();

            static (DateTime S, DateTime E) ClipToDay(DateTime ds, DateTime de, DateTime s, DateTime e)
            {
                var cs = s < ds ? ds : s;
                var ce = e > de ? de : e;
                return ce > cs ? (cs, ce) : (cs, cs);
            }

            var manualSlices = evs
                .Where(e => !e.IsDeleted && !e.IsAutoGenerated)
                .Select(e =>
                {
                    var c = ClipToDay(day, day.AddDays(1), e.StartTime, e.EndTime);
                    return new ManualSlice(e, c.S, c.E);
                })
                .Where(x => x.E > x.S)
                .ToList();

            var allSlices = evs
                .Where(e => !e.IsDeleted)
                .Select(e =>
                {
                    var c = ClipToDay(day, day.AddDays(1), e.StartTime, e.EndTime);
                    return new ManualSlice(e, c.S, c.E);
                })
                .Where(x => x.E > x.S)
                .ToList();

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

            var baseArrival = day + baseResolved.ArrivalTime;
            var baseDeparture = day + baseResolved.DepartureTime;
            int workMinutesForLunch = GetEffectiveWorkMinutes(allSlices);
            bool hasCreditedWorkTime = workMinutesForLunch > 0;

            int targetLunchCount =
                manualOverride != null &&
                manualOverride.LunchStart == manualOverride.LunchEnd &&
                !hasCreditedWorkTime
                    ? 0
                    : GetManualWorkDrivenLunchCount(TimeSpan.FromMinutes(workMinutesForLunch));

            var lunchLen = GetLunchLength(baseResolved);

            bool isFullManualSpecialDay = IsFullSpecialDayCore(
                day,
                baseArrival,
                baseDeparture,
                targetLunchCount,
                lunchLen,
                manualSlices);

            TimeSpan ls = TimeSpan.Zero;
            TimeSpan le = TimeSpan.Zero;

            if (!isFullManualSpecialDay && targetLunchCount > 0)
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

            SettingsService.SaveDaySettingsForDate(day, arr, dep, ls, le, _employeeId, isManualOverride: false, forceOverwriteExistingManual: forceOverwriteManual);
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

        private List<(DateTime start, DateTime end)> SubtractIntervals(
            IEnumerable<(DateTime start, DateTime end)> source,
            IEnumerable<(DateTime start, DateTime end)> blockers)
        {
            var mergedBlockers = MergeIntervals(blockers.ToList());
            var result = new List<(DateTime start, DateTime end)>();

            foreach (var segment in source.Where(x => x.end > x.start).OrderBy(x => x.start))
            {
                var cursor = segment.start;

                foreach (var blocker in mergedBlockers)
                {
                    if (blocker.end <= cursor)
                        continue;

                    if (blocker.start >= segment.end)
                        break;

                    if (blocker.start > cursor)
                        result.Add((cursor, blocker.start < segment.end ? blocker.start : segment.end));

                    if (blocker.end > cursor)
                        cursor = blocker.end > segment.end ? segment.end : blocker.end;

                    if (cursor >= segment.end)
                        break;
                }

                if (cursor < segment.end)
                    result.Add((cursor, segment.end));
            }

            return MergeIntervals(result);
        }

        private bool HasCreditedWorkOutsideSpecial(
            DateTime arrival,
            DateTime departure,
            List<ManualSlice> manual)
        {
            var specials = manual
                .Where(x => IsSpecial(x.Event.EventType))
                .Select(x => (
                    start: x.S < arrival ? arrival : x.S,
                    end: x.E > departure ? departure : x.E))
                .Where(x => x.end > x.start)
                .ToList();

            if (specials.Count == 0)
                return manual.Any(x => IsCreditedWorkTime(x.Event.EventType));

            var credited = manual
                .Where(x => IsCreditedWorkTime(x.Event.EventType))
                .Select(x => (
                    start: x.S < arrival ? arrival : x.S,
                    end: x.E > departure ? departure : x.E))
                .Where(x => x.end > x.start)
                .ToList();

            if (credited.Count == 0)
                return false;

            return SubtractIntervals(credited, specials)
                .Any(x => x.end - x.start >= TimeSpan.FromMinutes(QUANTUM_MIN));
        }

        private static readonly TimeSpan FourHours = TimeSpan.FromHours(4);
        private sealed record ScheduleBlock(DateTime S, DateTime E, bool IsLunch, bool IsFixedLunch);
        private sealed record ReflowPlan(List<(DateTime S, DateTime E)> WorkSegments);

        private static int GetManualWorkDrivenLunchCount(TimeSpan gross)
        {
            if (gross > TimeSpan.FromHours(12))
                return 2;

            if (gross > FourHours)
                return 1;

            return 0;
        }

        private static bool Overlaps((DateTime S, DateTime E) a, (DateTime S, DateTime E) b)
            => a.S < b.E && b.S < a.E;

        private static List<ScheduleBlock> MergeScheduleBlocks(IEnumerable<ScheduleBlock> blocks)
        {
            var sorted = blocks
                .Where(x => x.E > x.S)
                .OrderBy(x => x.S)
                .ThenBy(x => x.E)
                .ToList();

            var merged = new List<ScheduleBlock>();

            foreach (var block in sorted)
            {
                if (merged.Count == 0 || merged[^1].E <= block.S)
                {
                    merged.Add(block);
                    continue;
                }

                var last = merged[^1];
                merged[^1] = new ScheduleBlock(
                    last.S,
                    last.E > block.E ? last.E : block.E,
                    last.IsLunch || block.IsLunch,
                    last.IsFixedLunch || block.IsFixedLunch);
            }

            return merged;
        }

        private static DateTime? FindEarliestFreeSlotStart(
            DateTime rangeStart,
            DateTime rangeEnd,
            TimeSpan requiredLength,
            IEnumerable<ScheduleBlock> blocked)
        {
            if (requiredLength <= TimeSpan.Zero || rangeEnd - rangeStart < requiredLength)
                return null;

            var merged = MergeScheduleBlocks(
                blocked.Select(x => new ScheduleBlock(
                    x.S < rangeStart ? rangeStart : x.S,
                    x.E > rangeEnd ? rangeEnd : x.E,
                    x.IsLunch,
                    x.IsFixedLunch)));

            var cursor = rangeStart;

            foreach (var block in merged)
            {
                if (block.S - cursor >= requiredLength)
                    return cursor;

                if (block.E > cursor)
                    cursor = block.E;
            }

            return rangeEnd - cursor >= requiredLength ? cursor : null;
        }

        private static Event BuildManualContinuation(Event source, DateTime start, DateTime end)
        {
            return new Event
            {
                EmployeeId = source.EmployeeId,
                Title = source.Title,
                Description = source.Description,
                EventType = source.EventType,
                AllDay = false,
                StartTime = start,
                EndTime = end,
                ParentEventId = null,
                IsAutoGenerated = false,
                ImportBatchId = null,
                IsDeleted = false,
                HasCollision = false,
                AutoGeneratedForDate = null
            };
        }

        private ReflowPlan? BuildManualAdjustableWorkReflowPlan(
            Event candidate,
            IReadOnlyList<Event> dayEvents,
            TimeSpan lunchLen)
        {
            var workDuration = candidate.EndTime - candidate.StartTime;
            if (workDuration <= TimeSpan.Zero)
                return null;

            int requiredLunchCount = GetManualWorkDrivenLunchCount(workDuration);

            var fixedLunches = dayEvents
                .Where(e => e.Id != candidate.Id && e.EventType == EventType.Lunch)
                .Select(e => new ScheduleBlock(e.StartTime, e.EndTime, true, true))
                .OrderBy(e => e.S)
                .ToList();

            bool overlapsLunch = fixedLunches.Any(x =>
                Overlaps((candidate.StartTime, candidate.EndTime), (x.S, x.E)));

            if (requiredLunchCount == 0 && !overlapsLunch)
                return null;

            var hardBlocks = dayEvents
                .Where(e => e.Id != candidate.Id && e.EventType != EventType.Lunch)
                .Select(e => new ScheduleBlock(e.StartTime, e.EndTime, false, false))
                .ToList();

            var plannedLunches = new List<(DateTime S, DateTime E)>();
            var workSegments = new List<(DateTime S, DateTime E)>();
            var remainingWork = workDuration;
            var workSinceLunch = TimeSpan.Zero;
            var cursor = candidate.StartTime;
            var consumedLunches = 0;
            var daySoftEnd = candidate.StartTime.Date.AddHours(23).AddMinutes(55);

            List<ScheduleBlock> BuildAllBlocks()
            {
                return MergeScheduleBlocks(
                    hardBlocks
                        .Concat(fixedLunches)
                        .Concat(plannedLunches.Select(x => new ScheduleBlock(x.S, x.E, true, false))));
            }

            ScheduleBlock? FindContainingBlock(DateTime point)
                => BuildAllBlocks().FirstOrDefault(x => point >= x.S && point < x.E);

            ScheduleBlock? FindNextBlock(DateTime point)
                => BuildAllBlocks().FirstOrDefault(x => x.S >= point);

            ScheduleBlock? FindUpcomingFixedLunch(DateTime point)
                => fixedLunches.FirstOrDefault(x => x.E > point);

            void AppendWorkSegment(DateTime start, DateTime end)
            {
                if (end <= start)
                    return;

                if (workSegments.Count > 0 && workSegments[^1].E == start)
                    workSegments[^1] = (workSegments[^1].S, end);
                else
                    workSegments.Add((start, end));
            }

            while (remainingWork > TimeSpan.Zero)
            {
                if (cursor >= daySoftEnd)
                    return null;

                var activeBlock = FindContainingBlock(cursor);
                if (activeBlock != null)
                {
                    cursor = activeBlock.E;

                    if (activeBlock.IsLunch)
                    {
                        consumedLunches++;
                        workSinceLunch = TimeSpan.Zero;
                    }

                    continue;
                }

                if (consumedLunches < requiredLunchCount && workSinceLunch >= FourHours)
                {
                    var upcomingFixedLunch = FindUpcomingFixedLunch(cursor);
                    if (upcomingFixedLunch != null)
                    {
                        cursor = cursor < upcomingFixedLunch.S
                            ? upcomingFixedLunch.S
                            : cursor;
                        continue;
                    }

                    var lunchStart = FindEarliestFreeSlotStart(cursor, daySoftEnd, lunchLen, BuildAllBlocks());
                    if (!lunchStart.HasValue)
                        return null;

                    plannedLunches.Add((lunchStart.Value, lunchStart.Value + lunchLen));
                    cursor = lunchStart.Value;
                    continue;
                }

                var nextBlock = FindNextBlock(cursor);
                var freeEnd = nextBlock?.S ?? daySoftEnd;
                if (freeEnd > daySoftEnd)
                    freeEnd = daySoftEnd;

                if (freeEnd <= cursor)
                {
                    cursor = nextBlock?.E ?? daySoftEnd;
                    continue;
                }

                var availableWork = freeEnd - cursor;

                if (consumedLunches < requiredLunchCount)
                {
                    var untilLunchIsDue = FourHours - workSinceLunch;
                    if (untilLunchIsDue < availableWork)
                        availableWork = untilLunchIsDue;
                }

                if (availableWork <= TimeSpan.Zero)
                    continue;

                var workChunk = remainingWork < availableWork
                    ? remainingWork
                    : availableWork;

                AppendWorkSegment(cursor, cursor + workChunk);
                cursor += workChunk;
                remainingWork -= workChunk;
                workSinceLunch += workChunk;
            }

            return workSegments.Count == 0
                ? null
                : new ReflowPlan(workSegments);
        }

        private async Task NormalizeLongManualWorkEventsAsync(DateTime day)
        {
            await Task.CompletedTask;

            var resolved = SettingsService.GetResolvedDaySettingsIgnoringComputed(day, _employeeId);
            var lunchLen = GetLunchLength(resolved);

            var dayEvents = _eventService.GetEventsForDay(_employeeId, day)
                .Where(e => !e.IsDeleted && !e.IsAutoGenerated)
                .OrderBy(e => e.StartTime)
                .ToList();

            var candidates = dayEvents
                .Where(e =>
                    e.StartTime.Date == e.EndTime.Date &&
                    IsWorkLike(e.EventType) &&
                    e.ImportBatchId == null)
                .OrderBy(e => e.StartTime)
                .Select(e => e.Id)
                .ToList();

            foreach (var candidateId in candidates)
            {
                dayEvents = _eventService.GetEventsForDay(_employeeId, day)
                    .Where(e => !e.IsDeleted && !e.IsAutoGenerated)
                    .OrderBy(e => e.StartTime)
                    .ToList();

                var candidate = dayEvents.FirstOrDefault(e => e.Id == candidateId);
                if (candidate == null)
                    continue;

                var plan = BuildManualAdjustableWorkReflowPlan(candidate, dayEvents, lunchLen);
                if (plan == null || plan.WorkSegments.Count == 0)
                    continue;

                var firstSegment = plan.WorkSegments[0];
                bool changed =
                    candidate.AllDay ||
                    candidate.StartTime != firstSegment.S ||
                    candidate.EndTime != firstSegment.E ||
                    plan.WorkSegments.Count > 1;

                if (!changed)
                    continue;

                candidate.AllDay = false;
                candidate.StartTime = firstSegment.S;
                candidate.EndTime = firstSegment.E;
                _eventService.UpdateEventRaw(candidate);

                foreach (var segment in plan.WorkSegments.Skip(1))
                    _eventService.CreateEventRaw(BuildManualContinuation(candidate, segment.S, segment.E));
            }
        }

        private bool IsFullSpecialDayCore(
            DateTime day,
            DateTime arrival,
            DateTime departure,
            int targetLunchCount,
            TimeSpan lunchLen,
            List<ManualSlice> manual)
        {
            var specials = manual
                .Where(x => IsSpecial(x.Event.EventType))
                .Select(x => (S: x.S, E: x.E, T: x.Event.EventType))
                .ToList();

            if (specials.Count == 0)
                return false;

            if (HasCreditedWorkOutsideSpecial(arrival, departure, manual))
                return false;

            TimeSpan IntersectLen((DateTime S, DateTime E, EventType T) e)
            {
                var s = e.S < arrival ? arrival : e.S;
                var ee = e.E > departure ? departure : e.E;
                return ee > s ? (ee - s) : TimeSpan.Zero;
            }

            var gross = departure - arrival;
            var eight = TimeSpan.FromHours(8);

            var specialDayThreshold =
                gross - TimeSpan.FromTicks(lunchLen.Ticks * Math.Max(1, targetLunchCount));

            if (specialDayThreshold < TimeSpan.Zero)
                specialDayThreshold = TimeSpan.Zero;

            return
                specials.Any(sp => sp.T == EventType.Vacation && IntersectLen(sp) >= eight) ||
                specials.Any(sp => sp.T != EventType.Vacation && IntersectLen(sp) >= specialDayThreshold);
        }

        private static TimeSpan GetLunchLength(ResolvedDaySettings resolved)
        {
            var configuredLunchLen =
                resolved.LunchEnd > resolved.LunchStart
                    ? resolved.LunchEnd - resolved.LunchStart
                    : TimeSpan.FromMinutes(30);

            var maxLunchLen = ParseDurationOrDefault(
                resolved.MaxBreakDuration,
                TimeSpan.FromMinutes(30));

            var lunchLen = configuredLunchLen <= TimeSpan.Zero
                ? TimeSpan.FromMinutes(30)
                : configuredLunchLen;

            if (lunchLen > maxLunchLen)
                lunchLen = maxLunchLen;

            return lunchLen;
        }

        private DateTime GetPreferredFirstLunchStart(
            DateTime day,
            DateTime arrival,
            ResolvedDaySettings resolved,
            List<ManualSlice> manual)
        {
            return arrival.AddHours(4);
        }

        private DateTime FindFirstPossibleLunchStart(
            DateTime anchorStart,
            DateTime departure,
            TimeSpan lunchLen,
            List<ManualSlice> manual)
        {
            var blockers = manual
                .Where(x => x.Event.EventType != EventType.Lunch)
                .Where(x => !x.Event.EventType.IsAutoAdjustableWork())
                .OrderBy(x => x.S)
                .ToList();

            var probe = anchorStart;

            while (probe + lunchLen <= departure)
            {
                var probeEnd = probe + lunchLen;
                ManualSlice? blocker = null;

                foreach (var item in blockers)
                {
                    if (Overlaps(probe, probeEnd, item.S, item.E))
                    {
                        blocker = item;
                        break;
                    }
                }

                if (blocker == null)
                    return probe;

                probe = blocker.E > probe
                    ? blocker.E
                    : probe.AddMinutes(QUANTUM_MIN);
            }

            return anchorStart;
        }

        private static bool IsCreditedForLunchWindow(EventType t)
            => IsCreditedWorkTime(t);

        private static (DateTime Arrival, DateTime Departure) GetLunchPolicyWindow(
            DateTime day,
            ResolvedDaySettings resolved,
            List<ManualSlice> manual)
        {
            DateTime arrival = day + resolved.ArrivalTime;
            DateTime departure = day + resolved.DepartureTime;

            var credited = manual
                .Where(x =>
                    IsCreditedForLunchWindow(x.Event.EventType))
                .ToList();

            if (credited.Any())
            {
                var first = credited.Min(x => x.S);
                var last = credited.Max(x => x.E);

                if (first < arrival) arrival = first;
                if (last > departure) departure = last;
            }

            return (arrival, departure);
        }

        private int GetStableTargetLunchCount(
            DateTime day,
            DateTime arrival,
            DateTime departure,
            ResolvedDaySettings resolved)
        {
            var manualOverride = SettingsService.GetManualDaySettingsForDate(day, _employeeId);
            var dayEvents = _eventService.GetEventsForDay(_employeeId, day)
                .Where(e => !e.IsDeleted)
                .ToList();

            bool hasCreditedWorkTime = dayEvents.Any(e => IsCreditedWorkTime(e.EventType));

            if (manualOverride != null &&
                manualOverride.LunchStart == manualOverride.LunchEnd &&
                !hasCreditedWorkTime)
                    return 0;

            int currentTarget = GetTargetLunchCount(departure - arrival);
            int eventWindowTarget = GetEventWindowDrivenLunchCount(day, dayEvents);
            var manualWorkGross = TimeSpan.FromTicks(
                MergeIntervals(
                    dayEvents
                        .Where(e =>
                            !e.IsAutoGenerated &&
                            e.EventType.IsAutoAdjustableWork())
                        .Select(e => (e.StartTime, e.EndTime))
                        .ToList())
                    .Sum(x => (x.end - x.start).Ticks));
            int manualWorkTarget = GetManualWorkDrivenLunchCount(manualWorkGross);

            var baseResolved = SettingsService.GetResolvedDaySettingsIgnoringComputed(day, _employeeId);
            int baseTarget = GetTargetLunchCount(
                (day + baseResolved.DepartureTime) - (day + baseResolved.ArrivalTime));

            bool hasLunchPolicy =
                (manualOverride != null && manualOverride.LunchEnd > manualOverride.LunchStart) ||
                (resolved.LunchEnd > resolved.LunchStart) ||
                (baseResolved.LunchEnd > baseResolved.LunchStart);

            if (!hasLunchPolicy)
                return Math.Max(Math.Max(currentTarget, manualWorkTarget), eventWindowTarget);

            return Math.Max(Math.Max(Math.Max(currentTarget, baseTarget), manualWorkTarget), eventWindowTarget);
        }

        private int GetEventWindowDrivenLunchCount(DateTime day, IEnumerable<Event> dayEvents)
        {
            var dayStart = day.Date;
            var dayEnd = dayStart.AddDays(1);

            var credited = dayEvents
                .Where(e =>
                    !e.IsDeleted &&
                    e.EventType != EventType.Lunch &&
                    IsCreditedForLunchWindow(e.EventType))
                .Select(e => (
                    s: e.StartTime < dayStart ? dayStart : e.StartTime,
                    e: e.EndTime > dayEnd ? dayEnd : e.EndTime))
                .Where(x => x.e > x.s)
                .ToList();

            if (credited.Count == 0)
                return 0;

            var worked = TimeSpan.FromTicks(
                MergeIntervals(credited)
                    .Sum(x => (x.end - x.start).Ticks));

            return GetManualWorkDrivenLunchCount(worked);
        }

        private static bool IsPaidAbsence(EventType t) => t is EventType.DayOff
            or EventType.Illness
            or EventType.Vacation
            or EventType.Ocr
            or EventType.Doctor
            or EventType.Holiday;

        private List<ManualSlice> GetCurrentDaySlices(DateTime day)
        {
            var dayStart = day.Date;
            var dayEnd = dayStart.AddDays(1);

            static (DateTime S, DateTime E) ClipToDay(DateTime ds, DateTime de, DateTime s, DateTime e)
            {
                var cs = s < ds ? ds : s;
                var ce = e > de ? de : e;
                return ce > cs ? (cs, ce) : (cs, cs);
            }

            return _eventService
                .GetEventsForDay(_employeeId, day)
                .Where(e => !e.IsDeleted)
                .Select(e =>
                {
                    var c = ClipToDay(dayStart, dayEnd, e.StartTime, e.EndTime);
                    return new ManualSlice(e, c.S, c.E);
                })
                .Where(x => x.E > x.S)
                .ToList();
        }

        private int GetCreditAwareMissingAutoWorkMinutes(DateTime day)
        {
            var slices = GetCurrentDaySlices(day);

            int paidAbsenceMin = GetPaidSpecialCreditMinutes(slices);

            if (paidAbsenceMin >= DAY_NORM_MIN)
                return 0;

            int workMin = GetEffectiveWorkMinutes(slices);

            int creditedMin = Math.Min(DAY_NORM_MIN, paidAbsenceMin + workMin);
            creditedMin = RoundDownToQuantum(creditedMin);

            return RoundDownToQuantum(Math.Max(0, DAY_NORM_MIN - creditedMin));
        }
    }
}
