using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TeacherScheduleApp.Data;
using TeacherScheduleApp.Models;
using static TeacherScheduleApp.Models.GlobalSettings;

namespace TeacherScheduleApp.Services
{
    public static class GlobalSettingsService
    {
        public static async Task SaveGlobalSettingsAsync(int year, SemesterType semester, GlobalSettings settings)
        {
            using var db = new AppDbContext();
            var record = await db.GlobalSettings
                .FirstOrDefaultAsync(s => s.Year == year && s.Semester == semester);
            if (record == null)
            {
                record = new GlobalSettings { Year = year, Semester = semester };
                db.GlobalSettings.Add(record);
            }
            CopyValues(settings, record);
            await db.SaveChangesAsync();
        }


        private static void CopyValues(GlobalSettings src, GlobalSettings dst)
        {
           
            dst.GlobalStartTime = src.GlobalStartTime;
            dst.GlobalEndTime = src.GlobalEndTime;
            dst.EmployeeName = src.EmployeeName;
            dst.Department = src.Department;
            dst.MondayArrival = src.MondayArrival;
            dst.MondayDeparture = src.MondayDeparture;
            dst.MondayLunchStart = src.MondayLunchStart;
            dst.MondayLunchEnd = src.MondayLunchEnd;
            dst.TuesdayArrival = src.TuesdayArrival;
            dst.TuesdayDeparture = src.TuesdayDeparture;
            dst.TuesdayLunchStart = src.TuesdayLunchStart;
            dst.TuesdayLunchEnd = src.TuesdayLunchEnd;
            dst.WednesdayArrival = src.WednesdayArrival;
            dst.WednesdayDeparture = src.WednesdayDeparture;
            dst.WednesdayLunchStart = src.WednesdayLunchStart;
            dst.WednesdayLunchEnd = src.WednesdayLunchEnd;
            dst.ThursdayArrival = src.ThursdayArrival;
            dst.ThursdayDeparture = src.ThursdayDeparture;
            dst.ThursdayLunchStart = src.ThursdayLunchStart;
            dst.ThursdayLunchEnd = src.ThursdayLunchEnd;
            dst.FridayArrival = src.FridayArrival;
            dst.FridayDeparture = src.FridayDeparture;
            dst.FridayLunchStart = src.FridayLunchStart;
            dst.FridayLunchEnd = src.FridayLunchEnd;
            dst.MinBreakDuration = src.MinBreakDuration;
            dst.MaxBreakDuration = src.MaxBreakDuration;
            dst.AutoEventNamePreLunch = src.AutoEventNamePreLunch;
            dst.AutoEventNameLunch = src.AutoEventNameLunch;
            dst.AutoEventNamePostLunch = src.AutoEventNamePostLunch;
        }

        public static GlobalSettings LoadGlobalSettings(int year, SemesterType semester)
        {
            using var db = new AppDbContext();
            var record = db.GlobalSettings
                .FirstOrDefault(s => s.Year == year && s.Semester == semester);
            if (record == null) return null;
            return record;
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
        public static List<int> GetYearsWithData()
        {
            using var db = new AppDbContext();
            var years =
                db.GlobalSettings.AsNoTracking().Select(g => g.Year)
                .Concat(db.Events.AsNoTracking().Where(e => !e.IsDeleted).Select(e => e.StartTime.Year))
                .Concat(db.Events.AsNoTracking().Where(e => !e.IsDeleted).Select(e => e.EndTime.Year))
                .Concat(db.UserSettings.AsNoTracking().Select(u => u.Date.Year))
                .Distinct()
                .OrderBy(y => y)
                .ToList();

            return years;
        }
        public static GlobalSettings GetDefaultSettings(int year,SemesterType sem)
        {

            if (sem == SemesterType.Winter)
            {
                return new GlobalSettings
                {
                    Semester = SemesterType.Winter,
                    Year = year,
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
                    AutoEventNamePreLunch = "Dopolední pracovní doba",
                    AutoEventNameLunch = "Oběd",
                    AutoEventNamePostLunch = "Odpolední pracovní doba",
                }; 
            }
            else
            {
                // Letní
                return new GlobalSettings
                {
                    Semester = SemesterType.Summer,
                    Year = year,
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
                    AutoEventNamePreLunch = "Dopolední pracovní doba",
                    AutoEventNameLunch = "Oběd",
                    AutoEventNamePostLunch = "Odpolední pracovní doba",
                };
            }

        }
    }
}
