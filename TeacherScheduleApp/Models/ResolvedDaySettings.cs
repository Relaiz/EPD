using System;

namespace TeacherScheduleApp.Models
{
    public class ResolvedDaySettings
    {
        public int EmployeeId { get; set; }
        public DateTime Date { get; set; }

        public TimeSpan ArrivalTime { get; set; }
        public TimeSpan DepartureTime { get; set; }
        public TimeSpan LunchStart { get; set; }
        public TimeSpan LunchEnd { get; set; }
        public string AutoEventNamePreLunch { get; set; } = "Dopolední pracovní doba";
        public string AutoEventNameLunch { get; set; } = "Oběd";
        public string AutoEventNamePostLunch { get; set; } = "Odpolední pracovní doba";

        public string MinBreakDuration { get; set; } = "00:15";
        public string MaxBreakDuration { get; set; } = "01:00";
        public int Year { get; set; }
        public SemesterType Semester { get; set; }
    }
}