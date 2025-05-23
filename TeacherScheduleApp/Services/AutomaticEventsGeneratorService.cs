﻿using MsBox.Avalonia.Enums;
using MsBox.Avalonia;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TeacherScheduleApp.Models;
using TeacherScheduleApp.ViewModels;
using Avalonia.Controls.Documents;
using ReactiveUI;
using TeacherScheduleApp.Messages;
using static TeacherScheduleApp.Models.GlobalSettings;
using TeacherScheduleApp.Helpers;
using System.Globalization;

namespace TeacherScheduleApp.Services
{
  
    public class AutomaticEventsGeneratorService
    {
        private readonly EventService _eventService;
        private readonly Func<string, Task<bool>> _askCollision;

        public AutomaticEventsGeneratorService(EventService eventService, Func<string, Task<bool>> askCollision)
        {
            _eventService = eventService;
            _askCollision = askCollision;
        }

      
        public async Task RegenerateDailyEvents(DateTime date)
        {
            var day = date.Date;

            if (HolidayHelper.IsCzechHoliday(day) ||
               day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                return;
            if (IsInVacation(day, out var globalSet))
            {
                _eventService.RemoveAutoGeneratedEvents(day);

                
                var userSet = SettingsService.GetUserSettingsForDate(day);
                var (arr, dep, _, _) = GetDaySpans(globalSet, userSet, day.DayOfWeek);

                var vac = new Event
                {
                    Title = "Prázdniny",
                    StartTime = day + arr,
                    EndTime = day + dep,
                    EventType = EventType.Vacation,
                    IsAutoGenerated = true,
                    AutoGeneratedForDate = day
                };
                _eventService.CreateEvent(vac);
                return;
            }
            _eventService.RemoveAutoGeneratedEvents(day);

            var semester = GetSemesterForDate(day);
            var global = GlobalSettingsService.LoadGlobalSettings(semester) ?? GlobalSettingsService.GetDefaultSettings(semester); ;

            string arrStr, depStr, lunchStartStr, lunchEndStr;
            switch (day.DayOfWeek)
            {
                case DayOfWeek.Monday:
                    arrStr = global.MondayArrival;
                    depStr = global.MondayDeparture;
                    lunchStartStr = global.MondayLunchStart;
                    lunchEndStr = global.MondayLunchEnd;
                    break;
                case DayOfWeek.Tuesday:
                    arrStr = global.TuesdayArrival;
                    depStr = global.TuesdayDeparture;
                    lunchStartStr = global.TuesdayLunchStart;
                    lunchEndStr = global.TuesdayLunchEnd;
                    break;
                case DayOfWeek.Wednesday:
                    arrStr = global.WednesdayArrival;
                    depStr = global.WednesdayDeparture;
                    lunchStartStr = global.WednesdayLunchStart;
                    lunchEndStr = global.WednesdayLunchEnd;
                    break;
                case DayOfWeek.Thursday:
                    arrStr = global.ThursdayArrival;
                    depStr = global.ThursdayDeparture;
                    lunchStartStr = global.ThursdayLunchStart;
                    lunchEndStr = global.ThursdayLunchEnd;
                    break;
                case DayOfWeek.Friday:
                    arrStr = global.FridayArrival;
                    depStr = global.FridayDeparture;
                    lunchStartStr = global.FridayLunchStart;
                    lunchEndStr = global.FridayLunchEnd;
                    break;
                default:                 
                    return;
            }

         
            var arrival = TimeSpan.Parse(arrStr);
            var departure = TimeSpan.Parse(depStr);
            var lunchStart = TimeSpan.Parse(lunchStartStr);
            var lunchEnd = TimeSpan.Parse(lunchEndStr);


            var user = SettingsService.GetUserSettingsForDate(day);
            if (user != null)
            {
                arrival = user.ArrivalTime;
                departure = user.DepartureTime;
                lunchStart = user.LunchStart;
                lunchEnd = user.LunchEnd;
            }


            var evs = new List<Event>
            {
                new Event 
                {
                    Title               = global.AutoEventNamePreLunch,
                    StartTime           = day + arrival,
                    EndTime             = day + lunchStart,
                    EventType           = EventType.Work,
                    IsAutoGenerated     = true,
                    AutoGeneratedForDate= day
                },
                new Event 
                {
                    Title               = global.AutoEventNameLunch,
                    StartTime           = day + lunchStart,
                    EndTime             = day + lunchEnd,
                    EventType           = EventType.Lunch,
                    IsAutoGenerated     = true,
                    AutoGeneratedForDate= day
                },
                new Event 
                {
                    Title               = global.AutoEventNamePostLunch,
                    StartTime           = day + lunchEnd,
                    EndTime             = day + departure,
                    EventType           = EventType.Work,
                    IsAutoGenerated     = true,
                    AutoGeneratedForDate= day
                }
            };

    
            foreach (var ev in evs)
                _eventService.CreateEvent(ev);
        }

       
        public SemesterType GetSemesterForDate(DateTime date)
        {
            if ((date.Month >= 9) || (date.Month == 2 && date.Day <= 9))
            {
                return SemesterType.Winter;
            }

            if (date.Month >= 2 && date.Month <= 8)
            {
                return SemesterType.Summer;
            }

            return SemesterType.Winter;
        }           
        public async Task RegenerateAllAutoEventsForSemester(GlobalSettings.SemesterType sem)
        {
            var allDates = _eventService
                .GetAllEvents()
                .Where(e => e.IsAutoGenerated
                            && e.AutoGeneratedForDate.HasValue
                            && GetSemesterForDate(e.AutoGeneratedForDate.Value) == sem)
                .Select(e => e.AutoGeneratedForDate.Value.Date)
                .Distinct()
                .ToList();

            foreach (var date in allDates)
                RegenerateDailyEventsAsync(date);
        }
        private void CreateAuto(DateTime start, DateTime end, EventType type, string title)
        {
            _eventService.CreateEvent(new Event
            {
                Title = title,
                StartTime = start,
                EndTime = end,
                EventType = type,
                IsAutoGenerated = true,
                AutoGeneratedForDate = start.Date
            });
        }

        public async Task RegenerateDailyEventsAsync(DateTime date)
        {
            var day = date.Date;
            if (HolidayHelper.IsCzechHoliday(day) ||
                day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                return;

            _eventService.RemoveAutoGeneratedEvents(day);
            var lessons = _eventService
                .GetEventsForDay(day)
                .Where(e => !e.IsAutoGenerated)
                .OrderBy(e => e.StartTime)
                .ToList();
            for (int i = 0; i < lessons.Count; i++)
                for (int j = i + 1; j < lessons.Count; j++)
                    if (Overlaps(
                        lessons[i].StartTime.TimeOfDay, lessons[i].EndTime.TimeOfDay,
                        lessons[j].StartTime.TimeOfDay, lessons[j].EndTime.TimeOfDay))
                    {
                        lessons[j].HasCollision = true;
                        _eventService.UpdateEvent(lessons[j]);
                    }

            var sem = GetSemesterForDate(day);
            var global = GlobalSettingsService.LoadGlobalSettings(sem) ?? GlobalSettingsService.GetDefaultSettings(sem);
            var user = SettingsService.GetUserSettingsForDate(day);
            TimeSpan gStart = TimeSpan.Parse(global.GlobalStartTime, CultureInfo.InvariantCulture);
            TimeSpan gEnd = TimeSpan.Parse(global.GlobalEndTime, CultureInfo.InvariantCulture);
            TimeSpan dStart = user?.ArrivalTime ?? gStart;
            TimeSpan dEnd = user?.DepartureTime ?? gEnd;
            TimeSpan defaultLS, defaultLE;
            (_, _, defaultLS, defaultLE) = PdfService.GetWeekdayDefaults(global, day.DayOfWeek);

            
            var arrival = day + dStart;
            var departure = day + dEnd;
            var lunchStart = user is null ? day + defaultLS : day + user.LunchStart;
            var lunchEnd = user is null ? day + defaultLE : day + user.LunchEnd;
            var specialIntervals = MergeIntervals(lessons.Where(e => e.EventType != EventType.Work && e.EventType != EventType.Lunch)
                .Select(e => (Start: e.StartTime, End: e.EndTime))
                .ToList());

            if (specialIntervals.Any(iv =>
                    iv.start.TimeOfDay <= dStart &&
                    iv.end.TimeOfDay >= dEnd))
            {
                return;
            }

            if (lessons.Any())
            {
                var first = lessons.Min(l => l.StartTime);
                var last = lessons.Max(l => l.EndTime);
                if (first < arrival) arrival = first;
                if (last > departure) departure = last;
            }

            bool collision = lessons.Any(l => Overlaps(
                lunchStart.TimeOfDay, lunchEnd.TimeOfDay,
                l.StartTime.TimeOfDay, l.EndTime.TimeOfDay));
            if (collision && await _askCollision(
                $"Oběd {lunchStart:hh\\:mm}-{lunchEnd:hh\\:mm} se překrývá s lekcí dne {day:dd.MM.yyyy}. Chcete ho přesunout?"))
            {
                var lastEnd = lessons.Where(l => Overlaps(lunchStart.TimeOfDay, lunchEnd.TimeOfDay,l.StartTime.TimeOfDay, l.EndTime.TimeOfDay)).Max(l => l.EndTime.TimeOfDay);
                var dur = lunchEnd - lunchStart;
                lunchStart = day + lastEnd;
                lunchEnd = lunchStart + dur;
            }


            double dailyNorm = (gEnd - gStart - (defaultLE - defaultLS)).TotalHours;

            double totalSpecialHours = specialIntervals.Sum(iv => (iv.end - iv.start).TotalHours);
            if (totalSpecialHours >= dailyNorm)
            {
                if (dailyNorm > 4)
                    CreateAuto(lunchStart, lunchEnd, EventType.Lunch, global.AutoEventNameLunch);

                SaveUserSettingsFromEvents(day);
                return;
            }

            var busyCandidates = new List<(DateTime Start, DateTime End)>();
            busyCandidates.AddRange(lessons.Select(l => (l.StartTime, l.EndTime)));
            busyCandidates.Add((lunchStart, lunchEnd));
            busyCandidates.AddRange(specialIntervals);

            var busy = MergeIntervals(busyCandidates);
            var gaps = new List<(DateTime s, DateTime e)>();
            if (busy[0].start > arrival)
                gaps.Add((arrival, busy[0].start));
            for (int i = 0; i + 1 < busy.Count; i++)
                gaps.Add((busy[i].end, busy[i + 1].start));
            if (busy[^1].end < departure)
                gaps.Add((busy[^1].end, departure));
            var manualIntervals = lessons.Select(l => (Start: l.StartTime, End: l.EndTime)).ToList();
            var mergedManual = MergeIntervals(lessons
                  .Where(e => e.EventType is EventType.Work or EventType.BusinessTrip)
                  .Select(l => (Start: l.StartTime, End: l.EndTime))
                  .ToList()
            );
            double manualHours = mergedManual.Sum(seg => (seg.end - seg.start).TotalHours);

            double needed = Math.Max(0, dailyNorm - manualHours - totalSpecialHours);
            var newEvents = new List<Event>();
            newEvents.Add(new Event
            {
                Title = global.AutoEventNameLunch,
                StartTime = lunchStart,
                EndTime = lunchEnd,
                EventType = EventType.Lunch,
                IsAutoGenerated = true,
                AutoGeneratedForDate = day,
                HasCollision = collision
            });

            var workGaps = new List<(DateTime s, DateTime e, bool isPost)>();
            workGaps.AddRange(gaps.Where(g => g.e <= lunchStart).Select(g => (g.s, g.e, false)));
            workGaps.AddRange(gaps.Where(g => g.s >= lunchEnd).Select(g => (g.s, g.e, true)));
            var sortedGaps = workGaps.OrderBy(g => (g.e - g.s).TotalMinutes).ThenByDescending(g => g.isPost).ThenBy(g => g.s).ToList();
            foreach (var (s, e, isPost) in sortedGaps)
            {
                if (needed <= 0) break;
                var slotH = (e - s).TotalHours;
                var take = Math.Min(slotH, needed);

                DateTime evStart, evEnd;
                if (isPost)
                {
                    evStart = s;
                    evEnd = s + TimeSpan.FromHours(take);
                }
                else
                {
                    bool stickToStart = lessons.Any(l => l.EndTime == s);
                    if (stickToStart)
                    {
                        evStart = s;
                        evEnd = s + TimeSpan.FromHours(take);
                    }
                    else
                    {
                        evStart = e - TimeSpan.FromHours(take);
                        evEnd = e;
                    }
                }

                newEvents.Add(new Event
                {
                    Title = isPost ? global.AutoEventNamePostLunch : global.AutoEventNamePreLunch,
                    StartTime = evStart,
                    EndTime = evEnd,
                    EventType = EventType.Work,
                    IsAutoGenerated = true,
                    AutoGeneratedForDate = day
                });

                needed -= take;
            }            
            foreach (var ev in newEvents)
                _eventService.CreateEvent(ev);
            SaveUserSettingsFromEvents(day);
        }

        private void SaveUserSettingsFromEvents(DateTime day)
        {
            var evs = _eventService.GetEventsForDay(day)
                        .Where(e => !e.IsDeleted)
                        .OrderBy(e => e.StartTime)
                        .ToList();

            if (!evs.Any())
            {
                var sem = GetSemesterForDate(day);
                var global = GlobalSettingsService.LoadGlobalSettings(sem)
                             ?? GlobalSettingsService.GetDefaultSettings(sem);
                var (arr, dep, ls, le) = PdfService.GetWeekdayDefaults(global, day.DayOfWeek);
                SettingsService.SaveUserSettingsForDate(day, arr, dep, ls, le);
                return;
            }

            var arrival = evs.First().StartTime.TimeOfDay;
            var departure = evs.Last().EndTime.TimeOfDay;

            var lunchEv = evs.FirstOrDefault(e => e.EventType == EventType.Lunch);
            var ls_ = lunchEv?.StartTime.TimeOfDay ?? arrival;
            var le_ = lunchEv?.EndTime.TimeOfDay ?? departure;

            SettingsService.SaveUserSettingsForDate(day, arrival, departure, ls_, le_);
        }
       
        private static bool Overlaps(TimeSpan a0, TimeSpan a1, TimeSpan b0, TimeSpan b1)
            => a0 < b1 && b0 < a1;

        private (TimeSpan arr, TimeSpan dep, TimeSpan lunchStart, TimeSpan lunchEnd)
        GetDaySpans(GlobalSettings g, UserSettings u, DayOfWeek wd)
        {
            string sa, sd, s0, s1;
            switch (wd)
            {
                case DayOfWeek.Monday:
                    (sa, sd, s0, s1) =
                      (g.MondayArrival, g.MondayDeparture,
                       g.MondayLunchStart, g.MondayLunchEnd);
                    break;
                case DayOfWeek.Tuesday:
                    (sa, sd, s0, s1) =
                      (g.TuesdayArrival, g.TuesdayDeparture,
                       g.TuesdayLunchStart, g.TuesdayLunchEnd);
                    break;
                case DayOfWeek.Wednesday:
                    (sa, sd, s0, s1) =
                      (g.WednesdayArrival, g.WednesdayDeparture,
                       g.WednesdayLunchStart, g.WednesdayLunchEnd);
                    break;
                case DayOfWeek.Thursday:
                    (sa, sd, s0, s1) =
                      (g.ThursdayArrival, g.ThursdayDeparture,
                       g.ThursdayLunchStart, g.ThursdayLunchEnd);
                    break;
                case DayOfWeek.Friday:
                    (sa, sd, s0, s1) =
                      (g.FridayArrival, g.FridayDeparture,
                       g.FridayLunchStart, g.FridayLunchEnd);
                    break;
                default:
                    throw new InvalidOperationException("Není pracovný den");
            }

            if (u != null)
            {
                sa = u.ArrivalTime.ToString(@"hh\:mm");
                sd = u.DepartureTime.ToString(@"hh\:mm");
                s0 = u.LunchStart.ToString(@"hh\:mm");
                s1 = u.LunchEnd.ToString(@"hh\:mm");
            }
            if (string.IsNullOrWhiteSpace(sa) || string.IsNullOrWhiteSpace(sd))
            {
                sa = g.GlobalStartTime;
                sd = g.GlobalEndTime;
            }

            var arr = TimeSpan.Parse(sa, CultureInfo.InvariantCulture);
            var dep = TimeSpan.Parse(sd, CultureInfo.InvariantCulture);

           
            TimeSpan lunchStart, lunchEnd;
            if (string.IsNullOrWhiteSpace(s0) || string.IsNullOrWhiteSpace(s1))
            {
                var halfSpan = (dep - arr) / 2;
                lunchStart = arr + halfSpan - TimeSpan.FromMinutes(15);
                lunchEnd = lunchStart + TimeSpan.FromMinutes(30);
            }
            else
            {
                lunchStart = TimeSpan.Parse(s0, CultureInfo.InvariantCulture);
                lunchEnd = TimeSpan.Parse(s1, CultureInfo.InvariantCulture);
            }


            return (arr, dep, lunchStart, lunchEnd);
        }
        static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
        static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
    
        public async Task RegenerateRangeEventsAsync(DateTime start, DateTime end)
        {
            for (var d = start.Date; d <= end.Date; d = d.AddDays(1))
                await RegenerateDailyEventsAsync(d);

            MessageBus.Current.SendMessage(new AutoEventsGeneratedMessage());
        }            
        private bool IsInVacation(DateTime day, out GlobalSettings global)
        {
            var sem = GetSemesterForDate(day);
            global = GlobalSettingsService
                        .LoadGlobalSettings(sem)
                     ?? GlobalSettingsService.GetDefaultSettings(sem);

            int md = day.Month * 100 + day.Day;

            int winterStart = 12 * 100 + 23;
            int winterEnd = 1 * 100 + 1;
            bool inWinter = (md >= winterStart) || (md <= winterEnd);

            int summerStart = 6 * 100 + 30;
            int summerEnd = 8 * 100 + 10;
            bool inSummer = md >= summerStart && md <= summerEnd;

            return inWinter || inSummer;
        }
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
    }
}
