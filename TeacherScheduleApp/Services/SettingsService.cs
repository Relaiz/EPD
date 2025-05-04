using System;
using System.Linq;
using TeacherScheduleApp.Data;
using TeacherScheduleApp.Models;

namespace TeacherScheduleApp.Services
{
    public static class SettingsService
    {
        public static UserSettings GetUserSettingsForDate(DateTime date)
        {
            using (var db = new AppDbContext())
            {
                
                var settings = db.UserSettings
                    .FirstOrDefault(x => x.Date.Date == date.Date);
                return settings;
            }
        }

        public static void DeleteUserSettingsForDate(DateTime date)
        {
            using var db = new AppDbContext();
            var us = db.UserSettings
                       .FirstOrDefault(x => x.Date == date.Date);
            if (us != null)
            {
                db.UserSettings.Remove(us);
                db.SaveChanges();
            }
        }
        public static void SaveUserSettingsForDate( DateTime date, TimeSpan arrival, TimeSpan departure, TimeSpan lunchStart, TimeSpan lunchEnd)
        {
            using (var db = new AppDbContext())
            {
                var existing = db.UserSettings
                    .FirstOrDefault(x => x.Date.Date == date.Date);

                if (existing == null)
                {
                    existing = new UserSettings
                    {
                        Date = date.Date,
                        ArrivalTime = arrival,
                        DepartureTime = departure,
                        LunchStart = lunchStart,
                        LunchEnd = lunchEnd
                    };
                    db.UserSettings.Add(existing);
                }
                else
                {
                    existing.ArrivalTime = arrival;
                    existing.DepartureTime = departure;
                    existing.LunchStart = lunchStart;
                    existing.LunchEnd = lunchEnd;
                }

                db.SaveChanges();
            }
        }
    }

}
