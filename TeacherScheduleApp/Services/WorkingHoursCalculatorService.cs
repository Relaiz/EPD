using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TeacherScheduleApp.Helpers;
using TeacherScheduleApp.Models;
using TeacherScheduleApp.Services;

namespace TeacherScheduleApp.Services
{
    public class WorkingHoursCalculatorService
    {
        /// <summary>
        /// Vrací očekávaný počet hodin pro dané datum
        /// </summary>
        private double GetExpectedHours(DateTime date)
        {
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                return 0;

            var sem = GlobalSettingsService.GetSemesterForDate(date);
            var global = GlobalSettingsService.LoadGlobalSettings(sem)
                         ?? GlobalSettingsService.GetDefaultSettings(sem);

            var (defArr, defDep, defLunchStart, defLunchEnd)
                = PdfService.GetWeekdayDefaults(global, date.DayOfWeek);

            var user = SettingsService.GetUserSettingsForDate(date);
            if (user != null)
                (defArr, defDep, defLunchStart, defLunchEnd)
                    = (user.ArrivalTime, user.DepartureTime, user.LunchStart, user.LunchEnd);

            var workWindow = defDep - defArr;
            var lunchDur = defLunchEnd - defLunchStart;
            var expected = workWindow - lunchDur;

            return Math.Max(0, expected.TotalHours);
        }

        /// <summary>
        /// Vrací započítané hodiny pro danou událost
        /// </summary>
        private double CountedHours(Event ev)
        {
            switch (ev.EventType)
            {
                case EventType.Work:
                    return Math.Max(0, (ev.EndTime - ev.StartTime).TotalHours);

                case EventType.BusinessTrip:
                    return Math.Max(0, (ev.EndTime - ev.StartTime).TotalHours);

                default:
                    return 0;
            }
        }

        /// <summary>
        /// Vypočítá počet hodin za jeden den
        /// </summary>
        public double CalculateDailyHours(DateTime date, IEnumerable<Event> allEvents)
        {
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ||
                HolidayHelper.IsCzechHoliday(date))
                return 0;

            var actual = allEvents
                .Where(e => e.StartTime.Date == date.Date)
                .Sum(CountedHours);

            var expected = GetExpectedHours(date);
            return Math.Min(actual, expected);
        }

        /// <summary>
        /// Vypočítá týdenní statistiky (fakt vs. norma vs. přesčas vs. neodprac.)
        /// </summary>
        public (double actual, double expected, double overtime, double undertime)
          CalculateWeeklyStats(DateTime anyDate, IEnumerable<Event> allEvents)
        {
            var date = anyDate.Date;
            var delta = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            var weekStart = date.AddDays(-delta);

            var workDays = Enumerable
                .Range(0, 5)
                .Select(i => weekStart.AddDays(i))
                .ToList();

            var dailyStats = workDays
                .Select(d =>
                {
                    var evs = allEvents
                        .Where(e => e.StartTime.Date == d)
                        .ToList();

                    double actualDay = evs.Sum(CountedHours);
                    double expectedDay = GetExpectedHours(d);
                    double clamped = Math.Min(actualDay, expectedDay);

                    return new { actualDay, expectedDay, clamped };
                })
                .ToList();

            double totalActual = dailyStats.Sum(x => x.clamped);
            double totalExpected = dailyStats.Sum(x => x.expectedDay);
            double overtime = Math.Max(0, dailyStats.Sum(x => x.actualDay - x.expectedDay));
            double undertime = Math.Max(0, dailyStats.Sum(x => x.expectedDay - x.actualDay));

            return (totalActual, totalExpected, overtime, undertime);
        }

        /// <summary>
        /// Vypočítá měsíční souhrn hodin (fakt a norma)
        /// </summary>
        public (double actual, double norm) CalculateMonthlySummary(
           int year, int month, IEnumerable<Event> allEvents)
        {
            int daysInMonth = DateTime.DaysInMonth(year, month);
            double actualSum = 0, normSum = 0;

            for (int d = 1; d <= daysInMonth; d++)
            {
                var day = new DateTime(year, month, d);
                var expected = GetExpectedHours(day);
                if (expected <= 0)
                    continue;

                var actual = allEvents
                    .Where(e => e.StartTime.Date == day)
                    .Sum(CountedHours);

                actualSum += Math.Min(actual, expected);
                normSum += expected;
            }

            return (actualSum, normSum);
        }

        /// <summary>
        /// Vypočítá měsíčně přeřazené hodiny (redistribuce přebytku a deficitu)
        /// </summary>
        public double CalculateMonthlyRedistributedHours(int year, int month, IEnumerable<Event> allEvents)
        {
            int daysInMonth = DateTime.DaysInMonth(year, month);

            var dailyStats = Enumerable.Range(1, daysInMonth)
                .Select(d =>
                {
                    var day = new DateTime(year, month, d);
                    double expected = GetExpectedHours(day);
                    var evs = allEvents.Where(e => e.StartTime.Date == day);
                    double actual = evs.Sum(CountedHours);

                    double clamped = Math.Min(actual, expected);
                    double deficit = expected - clamped;
                    double excess = Math.Max(0, actual - expected);

                    return new { Clamped = clamped, Deficit = deficit, Excess = excess, Expected = expected };
                })
                .Where(x => x.Expected > 0)
                .ToList();

            double sumClamped = dailyStats.Sum(x => x.Clamped);
            double totalDeficit = dailyStats.Sum(x => x.Deficit);
            double totalExcess = dailyStats.Sum(x => x.Excess);

            double redistributed = Math.Min(totalExcess, totalDeficit);

            return sumClamped + redistributed;
        }
    }
}
