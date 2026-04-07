using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace TeacherScheduleApp.Models
{
    public class ImportBatch
    {
        [Key]
        public string Id { get; set; } = string.Empty;

        public string? Label { get; set; }
        public DateTime ImportedAt { get; set; }

        public ICollection<Event> Events { get; set; } = new List<Event>();
    }
}