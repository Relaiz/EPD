using System;

namespace TeacherScheduleApp.Models
{
    public class UserSettings
    {
        public int Id { get; set; }
        public DateTime Date { get; set; } = DateTime.Today;
        public TimeSpan ArrivalTime { get; set; }
        public TimeSpan DepartureTime { get; set; }
        public TimeSpan LunchStart { get; set; }
        public TimeSpan LunchEnd { get; set; }
    }
}
