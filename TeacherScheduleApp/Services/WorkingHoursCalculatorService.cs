using System;
using System.Collections.Generic;
using System.Linq;
using TeacherScheduleApp.Helpers;
using TeacherScheduleApp.Models;

namespace TeacherScheduleApp.Services
{
    public class WorkingHoursCalculatorService
    {
        private const double DayNorm = 8.0;
        private const int QUANTUM_MIN = 5;

        public readonly record struct DisplayMetrics(
            int ActualMinutes,
            int ExpectedMinutes,
            int OverMinutes,
            int UnderMinutes);

        private static readonly HashSet<EventType> SpecialNonPc = new()
        {
            EventType.Vacation,
            EventType.Illness,
            EventType.Ocr,
            EventType.Doctor,
            EventType.Holiday,
            EventType.DayOff
        };

        private static bool IsWorkday(DateTime d)
            => d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday
               && !HolidayHelper.IsCzechHoliday(d);

        private static int RoundDownToQuantum(int minutes)
            => minutes - minutes % QUANTUM_MIN;

        private static int ToWholeMinutes(double hours)
            => (int)Math.Round(hours * 60.0);

        private static int ToDisplayMinutes(double hours)
            => Math.Max(0, RoundDownToQuantum(ToWholeMinutes(hours)));

        private static (TimeSpan arr, TimeSpan dep, TimeSpan ls, TimeSpan le) GetWindow(DateTime day, int employeeId)
        {
            var resolved = SettingsService.GetResolvedDaySettings(day, employeeId);
            return (
                resolved.ArrivalTime,
                resolved.DepartureTime,
                resolved.LunchStart,
                resolved.LunchEnd
            );
        }

        private static (DateTime s, DateTime e) ClampTo(DateTime s, DateTime e, DateTime winS, DateTime winE)
            => (s < winS ? winS : s, e > winE ? winE : e);

        private static List<(DateTime s, DateTime e)> MergeIv(IEnumerable<(DateTime s, DateTime e)> iv)
        {
            var list = iv.Where(x => x.e > x.s).OrderBy(x => x.s).ToList();
            var res = new List<(DateTime s, DateTime e)>();

            foreach (var seg in list)
            {
                if (res.Count == 0 || res[^1].e < seg.s)
                    res.Add(seg);
                else
                    res[^1] = (res[^1].s, res[^1].e > seg.e ? res[^1].e : seg.e);
            }

            return res;
        }

        public (double worked, double expected, double over, double under, double specialNonPc, double workInclBT, double credited)
            DailyMetrics(DateTime day, IEnumerable<Event> all, int employeeId = EventService.DefaultEmployeeId)
        {
            if (!IsWorkday(day))
                return (0, 0, 0, 0, 0, 0, 0);

            var (arr, dep, _, _) = GetWindow(day, employeeId);
            var winS = day.Date + arr;
            var winE = day.Date + dep;

            var evs = all
                .Where(e => !e.IsDeleted && e.StartTime.Date == day.Date)
                .ToList();

            var specialIv = MergeIv(
                evs.Where(e => SpecialNonPc.Contains(e.EventType))
                   .Select(e => ClampTo(e.StartTime, e.EndTime, winS, winE))
                   .Where(x => x.e > x.s)
            );

            var workIv = MergeIv(
                evs.Where(e => e.EventType == EventType.Work || e.EventType == EventType.BusinessTrip)
                   .Select(e => ClampTo(e.StartTime, e.EndTime, winS, winE))
                   .Where(x => x.e > x.s)
            );

            var creditedIv = MergeIv(specialIv.Concat(workIv));

            var specialNonPc = specialIv.Sum(x => (x.e - x.s).TotalHours);
            var workInclBT = workIv.Sum(x => (x.e - x.s).TotalHours);
            var credited = creditedIv.Sum(x => (x.e - x.s).TotalHours);

            var expected = DayNorm;
            var worked = Math.Min(DayNorm, credited);
            var over = Math.Max(0, credited - DayNorm);
            var under = Math.Max(0, DayNorm - credited);

            return (worked, expected, over, under, specialNonPc, workInclBT, credited);
        }

        public DisplayMetrics DailyDisplayMetrics(
            DateTime day,
            IEnumerable<Event> all,
            int employeeId = EventService.DefaultEmployeeId)
        {
            var m = DailyMetrics(day, all, employeeId);

            return new DisplayMetrics(
                ActualMinutes: ToDisplayMinutes(m.credited),
                ExpectedMinutes: ToDisplayMinutes(m.expected),
                OverMinutes: ToDisplayMinutes(m.over),
                UnderMinutes: ToDisplayMinutes(m.under));
        }

        public DisplayMetrics WeeklyDisplayMetrics(
            DateTime anyDate,
            IEnumerable<Event> all,
            int employeeId = EventService.DefaultEmployeeId)
        {
            int delta = ((int)anyDate.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            var weekStart = anyDate.Date.AddDays(-delta);

            int actual = 0, expected = 0, over = 0, under = 0;

            foreach (var d in Enumerable.Range(0, 7).Select(i => weekStart.AddDays(i)).Where(IsWorkday))
            {
                var m = DailyDisplayMetrics(d, all, employeeId);
                actual += m.ActualMinutes;
                expected += m.ExpectedMinutes;
                over += m.OverMinutes;
                under += m.UnderMinutes;
            }

            return new DisplayMetrics(actual, expected, over, under);
        }

        public DisplayMetrics MonthlyDisplayMetrics(
            int year,
            int month,
            IEnumerable<Event> all,
            int employeeId = EventService.DefaultEmployeeId)
        {
            int actual = 0, expected = 0, over = 0, under = 0;

            foreach (var d in Enumerable.Range(1, DateTime.DaysInMonth(year, month))
                                        .Select(i => new DateTime(year, month, i))
                                        .Where(IsWorkday))
            {
                var m = DailyDisplayMetrics(d, all, employeeId);
                actual += m.ActualMinutes;
                expected += m.ExpectedMinutes;
                over += m.OverMinutes;
                under += m.UnderMinutes;
            }

            return new DisplayMetrics(actual, expected, over, under);
        }

        public Dictionary<(int Year, int Month), DisplayMetrics> WeeklyDisplayMetricsByMonth(
            DateTime anyDate,
            IEnumerable<Event> all,
            int employeeId = EventService.DefaultEmployeeId)
        {
            int delta = ((int)anyDate.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            var weekStart = anyDate.Date.AddDays(-delta);

            var result = new Dictionary<(int Year, int Month), DisplayMetrics>();

            var groups = Enumerable.Range(0, 7)
                .Select(i => weekStart.AddDays(i))
                .Where(IsWorkday)
                .GroupBy(d => (d.Year, d.Month));

            foreach (var g in groups)
            {
                int actual = 0, expected = 0, over = 0, under = 0;

                foreach (var d in g)
                {
                    var m = DailyDisplayMetrics(d, all, employeeId);
                    actual += m.ActualMinutes;
                    expected += m.ExpectedMinutes;
                    over += m.OverMinutes;
                    under += m.UnderMinutes;
                }

                result[g.Key] = new DisplayMetrics(actual, expected, over, under);
            }

            return result;
        }
    }
}