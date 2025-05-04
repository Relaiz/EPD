using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;

namespace TeacherScheduleApp.Helpers
{
    public static class HolidayHelper
    {
        private static readonly HttpClient _client = new HttpClient();

        private static readonly ConcurrentDictionary<int, HashSet<DateTime>> _cache
            = new ConcurrentDictionary<int, HashSet<DateTime>>();

        public static bool IsCzechHoliday(DateTime date)
        {
            var year = date.Year;
            var holidays = _cache.GetOrAdd(year, BuildHolidaysForYear);
            return holidays.Contains(date.Date);
        }

        private static HashSet<DateTime> BuildHolidaysForYear(int year)
        {
            var url = $"https://date.nager.at/api/v3/PublicHolidays/{year}/CZ";
            var json = _client.GetStringAsync(url).GetAwaiter().GetResult();

            var list = JsonSerializer.Deserialize<List<PublicHoliday>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new List<PublicHoliday>();

            return list
                .Select(h => h.Date.Date)
                .ToHashSet();
        }

        private class PublicHoliday
        {
            public DateTime Date { get; set; }
        }
    }
}
