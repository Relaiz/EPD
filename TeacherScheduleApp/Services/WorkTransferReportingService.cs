using System;
using System.Collections.Generic;
using System.Linq;

namespace TeacherScheduleApp.Services
{
    public static class WorkTransferReportingService
    {
        private static readonly Dictionary<DateTime, double> _movedOut = new();
        private static readonly Dictionary<DateTime, List<(DateTime from, double hours)>> _movedIn = new();

        public static void ResetWeek(IEnumerable<DateTime> weekDays)
        {
            var set = new HashSet<DateTime>(weekDays.Select(d => d.Date));
            foreach (var k in set) { _movedOut.Remove(k); _movedIn.Remove(k); }
        }

        public static void AddTransfer(DateTime fromDay, DateTime toDay, double hours)
        {
            if (hours <= 1e-6) return;
            fromDay = fromDay.Date; toDay = toDay.Date;

            _movedOut[fromDay] = (_movedOut.TryGetValue(fromDay, out var v) ? v : 0) + hours;

            if (!_movedIn.TryGetValue(toDay, out var list))
                _movedIn[toDay] = list = new List<(DateTime, double)>();
            list.Add((fromDay, hours));
        }

        public static double GetMovedOut(DateTime day)
            => _movedOut.TryGetValue(day.Date, out var h) ? h : 0.0;

        public static IReadOnlyList<(DateTime from, double hours)> GetMovedInDetails(DateTime day)
            => _movedIn.TryGetValue(day.Date, out var list) ? list : Array.Empty<(DateTime, double)>();
    }
}
