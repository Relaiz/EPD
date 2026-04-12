using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TeacherScheduleApp.Models
{
    public class BalanceTransfer
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public DateTime FromDay { get; set; }
        public DateTime ToDay { get; set; }
        public int Edge { get; set; }
        public int Minutes { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
}
