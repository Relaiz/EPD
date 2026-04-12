using System;

namespace TeacherScheduleApp.Models
{
    public class BalanceSelfTrim
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public DateTime Day { get; set; }
        public int Edge { get; set; }
        public int Minutes { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
}