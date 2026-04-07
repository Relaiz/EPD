using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TeacherScheduleApp.Models
{
    public class DaySettings
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public int EmployeeId { get; set; }
        public DateTime Date { get; set; } = DateTime.Today;

        public TimeSpan ArrivalTime { get; set; }
        public TimeSpan DepartureTime { get; set; }
        public TimeSpan LunchStart { get; set; }
        public TimeSpan LunchEnd { get; set; }

        public Employee Employee { get; set; } = null!;
        public bool IsManualOverride { get; set; }
    }
}