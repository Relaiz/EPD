using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TeacherScheduleApp.Models;

namespace TeacherScheduleApp.Models
{
    public class TeacherLesson
    {
        public DateTime Date { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public int Rok { get; set; }
        public int WeekFrom { get; set; }
        public int WeekTo { get; set; }
        public string WeekType { get; set; } 
        public int DayOfWeek { get; set; } 
    }
}