using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TeacherScheduleApp.Messages;

namespace TeacherScheduleApp.Services
{
    public readonly record struct ChangedRange(DateTime? Start, DateTime? End)
    {
        public bool HasValue => Start.HasValue && End.HasValue;

        public static ChangedRange Empty => new(null, null);

        public ChangedRange Normalize()
        {
            if (!HasValue)
                return Empty;

            var s = Start!.Value.Date;
            var e = End!.Value.Date;

            return s <= e
                ? new ChangedRange(s, e)
                : new ChangedRange(e, s);
        }

        public static ChangedRange FromDates(IEnumerable<DateTime> dates)
        {
            var list = dates
                .Select(d => d.Date)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            if (list.Count == 0)
                return Empty;

            return new ChangedRange(list.First(), list.Last());
        }

        public ChangedRange Merge(ChangedRange other)
        {
            if (!HasValue) return other;
            if (!other.HasValue) return this;

            var a = Normalize();
            var b = other.Normalize();

            return new ChangedRange(
                a.Start!.Value < b.Start!.Value ? a.Start : b.Start,
                a.End!.Value > b.End!.Value ? a.End : b.End);
        }
    }

    public sealed class ScheduleChangeProcessor
    {
        private readonly EventService _eventService;
        private readonly Func<string, Task<bool>> _askCollision;
        private readonly int _employeeId;

        public ScheduleChangeProcessor(
            EventService eventService,
            Func<string, Task<bool>> askCollision,
            int employeeId)
        {
            _eventService = eventService;
            _askCollision = askCollision;
            _employeeId = employeeId;
        }

        public async Task ApplyAsync(
            ChangedRange changedRange,
            bool preserveUserSettings,
            bool expandToFullYearIfYearHasNoAuto = false)
        {
            if (!changedRange.HasValue)
                return;

            var scopes = expandToFullYearIfYearHasNoAuto
                ? BuildScopesForImport(changedRange.Normalize())
                : MergeScopes(new[] { changedRange.Normalize() });

            if (scopes.Count == 0)
                return;

            var generator = new AutomaticEventsGeneratorService(
                _eventService,
                _askCollision,
                _employeeId);

            using var _ = _eventService.BeginNotificationSuppression();

            foreach (var scope in scopes)
            {
                await generator.RegenerateRangeEventsAsync(
                    scope.Start!.Value,
                    scope.End!.Value,
                    preserveUserSettings,
                    ensureLunchAfterGeneration: false);
            }

            foreach (var scope in scopes)
            {
                await _eventService.BalanceForChangedRangeAsync(
                    scope.Start!.Value,
                    scope.End!.Value,
                    _employeeId);
            }

            MessageBus.Current.SendMessage(new AutoEventsGeneratedMessage());

            foreach (var scope in scopes)
            {
                for (var day = scope.Start!.Value.Date; day <= scope.End!.Value.Date; day = day.AddDays(1))
                    MessageBus.Current.SendMessage(new UserSettingsChangedMessage(day));
            }
        }

        private List<ChangedRange> BuildScopesForImport(ChangedRange changedRange)
        {
            var src = changedRange.Normalize();
            var result = new List<ChangedRange>
            {
                ExpandToWholeMonths(src)
            };

            for (int year = src.Start!.Value.Year; year <= src.End!.Value.Year; year++)
                result.AddRange(_eventService.GetUninitializedScopesForYear(year, _employeeId));

            return MergeScopes(result);
        }

        private static ChangedRange ExpandToWholeMonths(ChangedRange changedRange)
        {
            var src = changedRange.Normalize();
            if (!src.HasValue)
                return ChangedRange.Empty;

            var start = new DateTime(src.Start!.Value.Year, src.Start.Value.Month, 1);
            var endMonth = new DateTime(src.End!.Value.Year, src.End.Value.Month, 1);
            var end = endMonth.AddMonths(1).AddDays(-1);

            return new ChangedRange(start, end);
        }

        private static List<ChangedRange> MergeScopes(IEnumerable<ChangedRange> scopes)
        {
            var list = scopes
                .Where(x => x.HasValue)
                .Select(x => x.Normalize())
                .OrderBy(x => x.Start)
                .ToList();

            if (list.Count == 0)
                return new List<ChangedRange>();

            var merged = new List<ChangedRange> { list[0] };

            for (int i = 1; i < list.Count; i++)
            {
                var last = merged[^1];
                var cur = list[i];

                if (cur.Start!.Value <= last.End!.Value.AddDays(1))
                {
                    merged[^1] = new ChangedRange(
                        last.Start,
                        cur.End!.Value > last.End!.Value ? cur.End : last.End);
                }
                else
                {
                    merged.Add(cur);
                }
            }

            return merged;
        }
    }
}
