using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TeacherScheduleApp.Models;

namespace TeacherScheduleApp.Messages
{
    public class GlobalSettingsChangedMessage
    {
        public GlobalSettings.SemesterType Semester { get; }
        public GlobalSettingsChangedMessage(GlobalSettings.SemesterType sem) => Semester = sem;
    }

}
