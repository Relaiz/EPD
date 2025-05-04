using System;
using System.Linq;
using TeacherScheduleApp.Data;
using TeacherScheduleApp.Models;
using static TeacherScheduleApp.Models.GlobalSettings;

namespace TeacherScheduleApp.Services
{
    public static class GlobalSettingsService
    {
        public static void SaveGlobalSettings(string globalStartTime, string globalEndTime)
        {
            using var db = new AppDbContext();        
            var record = db.GlobalSettings.FirstOrDefault(r => r.Id == 1);
            if (record == null)
            {
                record = new GlobalSettings
                {
                    Id = 1,
                    GlobalStartTime = globalStartTime,
                    GlobalEndTime = globalEndTime
                };
                db.GlobalSettings.Add(record);
            }
            else
            {
                record.GlobalStartTime = globalStartTime;
                record.GlobalEndTime = globalEndTime;
            }
            db.SaveChanges();
        }

        public static void SaveGlobalSettings(GlobalSettings.SemesterType semester, GlobalSettings settings)
        {
            using var db = new AppDbContext();
            var record = db.GlobalSettings.FirstOrDefault(s => s.Semester == semester);
            if (record == null)
            {
                record = new GlobalSettings
                {
                    Semester = semester,
                    GlobalStartTime = settings.GlobalStartTime,
                    GlobalEndTime = settings.GlobalEndTime,
                    EmployeeName = settings.EmployeeName,
                    Department = settings.Department,
                    MondayArrival = settings.MondayArrival,
                    MondayDeparture = settings.MondayDeparture,
                    MondayLunchStart = settings.MondayLunchStart,
                    MondayLunchEnd = settings.MondayLunchEnd,

                    TuesdayArrival = settings.TuesdayArrival,
                    TuesdayDeparture = settings.TuesdayDeparture,
                    TuesdayLunchStart = settings.TuesdayLunchStart,
                    TuesdayLunchEnd = settings.TuesdayLunchEnd,

                    WednesdayArrival = settings.WednesdayArrival,
                    WednesdayDeparture = settings.WednesdayDeparture,
                    WednesdayLunchStart = settings.WednesdayLunchStart,
                    WednesdayLunchEnd = settings.WednesdayLunchEnd,

                    ThursdayArrival = settings.ThursdayArrival,
                    ThursdayDeparture = settings.ThursdayDeparture,
                    ThursdayLunchStart = settings.ThursdayLunchStart,
                    ThursdayLunchEnd = settings.ThursdayLunchEnd,

                    FridayArrival = settings.FridayArrival,
                    FridayDeparture = settings.FridayDeparture,
                    FridayLunchStart = settings.FridayLunchStart,
                    FridayLunchEnd = settings.FridayLunchEnd,

                    MinBreakDuration = settings.MinBreakDuration,
                    MaxBreakDuration = settings.MaxBreakDuration,
                    AutoEventNamePreLunch = settings.AutoEventNamePreLunch,
                    AutoEventNameLunch = settings.AutoEventNameLunch,
                    AutoEventNamePostLunch = settings.AutoEventNamePostLunch
                };
                db.GlobalSettings.Add(record);
            }
            else
            {
                record.GlobalStartTime = settings.GlobalStartTime;
                record.GlobalEndTime = settings.GlobalEndTime;
                record.EmployeeName = settings.EmployeeName;
                record.Department = settings.Department;
                record.MondayArrival = settings.MondayArrival;
                record.MondayDeparture = settings.MondayDeparture;
                record.MondayLunchStart = settings.MondayLunchStart;
                record.MondayLunchEnd = settings.MondayLunchEnd;

                record.TuesdayArrival = settings.TuesdayArrival;
                record.TuesdayDeparture = settings.TuesdayDeparture;
                record.TuesdayLunchStart = settings.TuesdayLunchStart;
                record.TuesdayLunchEnd = settings.TuesdayLunchEnd;

                record.WednesdayArrival = settings.WednesdayArrival;
                record.WednesdayDeparture = settings.WednesdayDeparture;
                record.WednesdayLunchStart = settings.WednesdayLunchStart;
                record.WednesdayLunchEnd = settings.WednesdayLunchEnd;

                record.ThursdayArrival = settings.ThursdayArrival;
                record.ThursdayDeparture = settings.ThursdayDeparture;
                record.ThursdayLunchStart = settings.ThursdayLunchStart;
                record.ThursdayLunchEnd = settings.ThursdayLunchEnd;

                record.FridayArrival = settings.FridayArrival;
                record.FridayDeparture = settings.FridayDeparture;
                record.FridayLunchStart = settings.FridayLunchStart;
                record.FridayLunchEnd = settings.FridayLunchEnd;

                record.MinBreakDuration = settings.MinBreakDuration;
                record.MaxBreakDuration = settings.MaxBreakDuration;
                record.AutoEventNamePreLunch = settings.AutoEventNamePreLunch;
                record.AutoEventNameLunch = settings.AutoEventNameLunch;
                record.AutoEventNamePostLunch = settings.AutoEventNamePostLunch;
            }
            db.SaveChanges();
        }

        public static GlobalSettings LoadGlobalSettings(GlobalSettings.SemesterType semester)
        {
            using var db = new AppDbContext();
            var record = db.GlobalSettings.FirstOrDefault(s => s.Semester == semester);
            if (record == null)
                return null;
            return new GlobalSettings
            {
                GlobalStartTime = record.GlobalStartTime,
                GlobalEndTime = record.GlobalEndTime,
                EmployeeName = record.EmployeeName,
                Department = record.Department,
                MondayArrival = record.MondayArrival,
                MondayDeparture = record.MondayDeparture,
                MondayLunchStart = record.MondayLunchStart,
                MondayLunchEnd = record.MondayLunchEnd,

                TuesdayArrival = record.TuesdayArrival,
                TuesdayDeparture = record.TuesdayDeparture,
                TuesdayLunchStart = record.TuesdayLunchStart,
                TuesdayLunchEnd = record.TuesdayLunchEnd,

                WednesdayArrival = record.WednesdayArrival,
                WednesdayDeparture = record.WednesdayDeparture,
                WednesdayLunchStart = record.WednesdayLunchStart,
                WednesdayLunchEnd = record.WednesdayLunchEnd,

                ThursdayArrival = record.ThursdayArrival,
                ThursdayDeparture = record.ThursdayDeparture,
                ThursdayLunchStart = record.ThursdayLunchStart,
                ThursdayLunchEnd = record.ThursdayLunchEnd,

                FridayArrival = record.FridayArrival,
                FridayDeparture = record.FridayDeparture,
                FridayLunchStart = record.FridayLunchStart,
                FridayLunchEnd = record.FridayLunchEnd,

                MinBreakDuration = record.MinBreakDuration,
                MaxBreakDuration = record.MaxBreakDuration,
                AutoEventNamePreLunch = record.AutoEventNamePreLunch,
                AutoEventNameLunch = record.AutoEventNameLunch,
                AutoEventNamePostLunch = record.AutoEventNamePostLunch,
                Semester = record.Semester
            };
        }
        public static SemesterType GetSemesterForDate(DateTime date)
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
        public static GlobalSettings GetDefaultSettings(SemesterType sem)
        {

            if (sem == SemesterType.Winter)
            {
                return new GlobalSettings
                {
                    Semester = SemesterType.Winter,
                    GlobalStartTime = "08:00",
                    GlobalEndTime = "16:30",
                    EmployeeName = "Radek Matoušek",
                    Department = "Katedra informačních technologií",
                    MondayArrival = "08:00",
                    MondayDeparture = "16:30",
                    MondayLunchStart = "12:00",
                    MondayLunchEnd = "12:30",
                    TuesdayArrival = "08:00",
                    TuesdayDeparture = "16:30",
                    TuesdayLunchStart = "12:00",
                    TuesdayLunchEnd = "12:30",
                    WednesdayArrival = "08:00",
                    WednesdayDeparture = "16:30",
                    WednesdayLunchStart = "12:00",
                    WednesdayLunchEnd = "12:30",
                    ThursdayArrival = "08:00",
                    ThursdayDeparture = "16:30",
                    ThursdayLunchStart = "12:00",
                    ThursdayLunchEnd = "12:30",
                    FridayArrival = "08:00",
                    FridayDeparture = "16:30",
                    FridayLunchStart = "12:00",
                    FridayLunchEnd = "12:30",
                    MinBreakDuration = "00:15",
                    MaxBreakDuration = "01:00",
                    AutoEventNamePreLunch = "Ranní výuka",
                    AutoEventNameLunch = "Oběd",
                    AutoEventNamePostLunch = "Odpolední výuka",
                };
            }
            else
            {
                // Letní
                return new GlobalSettings
                {
                    Semester = SemesterType.Summer,
                    GlobalStartTime = "08:30",
                    GlobalEndTime = "17:00",
                    EmployeeName = "Radek Matoušek",
                    Department = "Katedra informačních technologií",
                    MondayArrival = "08:30",
                    MondayDeparture = "17:00",
                    MondayLunchStart = "12:30",
                    MondayLunchEnd = "13:00",
                    TuesdayArrival = "08:30",
                    TuesdayDeparture = "17:00",
                    TuesdayLunchStart = "12:30",
                    TuesdayLunchEnd = "13:00",
                    WednesdayArrival = "08:30",
                    WednesdayDeparture = "17:00",
                    WednesdayLunchStart = "12:30",
                    WednesdayLunchEnd = "13:00",
                    ThursdayArrival = "08:30",
                    ThursdayDeparture = "17:00",
                    ThursdayLunchStart = "12:30",
                    ThursdayLunchEnd = "13:00",
                    FridayArrival = "08:30",
                    FridayDeparture = "17:00",
                    FridayLunchStart = "12:30",
                    FridayLunchEnd = "13:00",
                    MinBreakDuration = "00:15",
                    MaxBreakDuration = "01:00",
                    AutoEventNamePreLunch = "Ranní výuka",
                    AutoEventNameLunch = "Oběd",
                    AutoEventNamePostLunch = "Odpolední výuka",
                };
            }

        }
    }
}
