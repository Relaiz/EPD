using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TeacherScheduleApp.Models
{
    public class Employee
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public string FullName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;

        public ICollection<SemesterSettings> SemesterSettings { get; set; } = new List<SemesterSettings>();
        public ICollection<DaySettings> DaySettings { get; set; } = new List<DaySettings>();
        public ICollection<Event> Events { get; set; } = new List<Event>();
    }
}