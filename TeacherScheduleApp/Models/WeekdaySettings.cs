using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TeacherScheduleApp.Models
{
    public class WeekdaySettings
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public int SemesterSettingsId { get; set; }

        /// <summary>
        /// 1 = Monday, 2 = Tuesday, 3 = Wednesday, 4 = Thursday, 5 = Friday
        /// </summary>
        public int DayOfWeek { get; set; }

        public TimeSpan ArrivalTime { get; set; }
        public TimeSpan DepartureTime { get; set; }
        public TimeSpan LunchStart { get; set; }
        public TimeSpan LunchEnd { get; set; }

        public SemesterSettings SemesterSettings { get; set; } = null!;
    }
}