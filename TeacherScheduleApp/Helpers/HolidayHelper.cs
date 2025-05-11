using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace TeacherScheduleApp.Helpers
{
    public static class HolidayHelper
    {
        private static readonly HttpClient _client;
        private static readonly ConcurrentDictionary<int, HashSet<DateTime>> _cache = new ConcurrentDictionary<int, HashSet<DateTime>>();
        static HolidayHelper()
        {
            var handler = new SocketsHttpHandler
            {
                UseProxy = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            _client = new HttpClient(handler)
            {
                DefaultRequestVersion = HttpVersion.Version11
            };
            _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("TeacherScheduleApp/1.0 (+https://github.com/Relaiz/BCLokal)");
        }
        public static bool IsCzechHoliday(DateTime date)
        {
            var year = date.Year;
            var holidays = _cache.GetOrAdd(year, BuildHolidaysForYear);
            return holidays.Contains(date.Date);
        }
        private static HashSet<DateTime> BuildHolidaysForYear(int year)
        {
            var url = $"https://date.nager.at/api/v3/PublicHolidays/{year}/CZ";
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get, url)
                {
                    Version = HttpVersion.Version11
                };
                var resp = _client.SendAsync(req).GetAwaiter().GetResult();
                var content = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!resp.IsSuccessStatusCode)
                {
                    return new HashSet<DateTime>();
                }
                var list = JsonSerializer.Deserialize<List<PublicHoliday>>(content, new JsonSerializerOptions
                {
                   PropertyNameCaseInsensitive = true
                }) ?? new List<PublicHoliday>();
                return list.Select(h => h.Date.Date).ToHashSet();
            }
            catch (Exception ex)
            {
                return new HashSet<DateTime>();
            }
        }
        private class PublicHoliday
        {
            public DateTime Date { get; set; }
        }
    }
}