namespace TeacherScheduleApp.Models
{
    public class GlobalSettings
    {
        public enum SemesterType
        {
            Winter,
            Summer
        }

        public int Id { get; set; }

        public string GlobalStartTime { get; set; }
        public string GlobalEndTime { get; set; }
        public string EmployeeName { get; set; }
        public string Department { get; set; }
        
        public string MondayArrival { get; set; }
        public string MondayDeparture { get; set; }
        public string MondayLunchStart { get; set; }
        public string MondayLunchEnd { get; set; }

        public string TuesdayArrival { get; set; }
        public string TuesdayDeparture { get; set; }
        public string TuesdayLunchStart { get; set; }
        public string TuesdayLunchEnd { get; set; }

        public string WednesdayArrival { get; set; }
        public string WednesdayDeparture { get; set; }
        public string WednesdayLunchStart { get; set; }
        public string WednesdayLunchEnd { get; set; }

  
        public string ThursdayArrival { get; set; }
        public string ThursdayDeparture { get; set; }
        public string ThursdayLunchStart { get; set; }
        public string ThursdayLunchEnd { get; set; }

      
        public string FridayArrival { get; set; }
        public string FridayDeparture { get; set; }
        public string FridayLunchStart { get; set; }
        public string FridayLunchEnd { get; set; }

 
        public string MinBreakDuration { get; set; }
        public string MaxBreakDuration { get; set; }

        public string AutoEventNamePreLunch { get; set; }
        public string AutoEventNameLunch { get; set; }
        public string AutoEventNamePostLunch { get; set; }

        public SemesterType Semester { get; set; }
        public GlobalSettings Clone() => (GlobalSettings)MemberwiseClone();
    }
}
