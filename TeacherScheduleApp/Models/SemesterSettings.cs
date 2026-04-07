using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TeacherScheduleApp.Models
{
    public class SemesterSettings
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public int EmployeeId { get; set; }
        public int Year { get; set; }
        public SemesterType Semester { get; set; }

        public string GlobalStartTime { get; set; } = string.Empty;
        public string GlobalEndTime { get; set; } = string.Empty;
        public string MinBreakDuration { get; set; } = string.Empty;
        public string MaxBreakDuration { get; set; } = string.Empty;

        public string AutoEventNamePreLunch { get; set; } = string.Empty;
        public string AutoEventNameLunch { get; set; } = string.Empty;
        public string AutoEventNamePostLunch { get; set; } = string.Empty;

        public Employee Employee { get; set; } = null!;
        public ICollection<WeekdaySettings> WeekdaySettings { get; set; } = new List<WeekdaySettings>();
    }
}