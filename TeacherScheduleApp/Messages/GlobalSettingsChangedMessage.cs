using TeacherScheduleApp.Models;

namespace TeacherScheduleApp.Messages
{
    public class GlobalSettingsChangedMessage
    {
        public SemesterType Semester { get; }

        public GlobalSettingsChangedMessage(SemesterType sem)
        {
            Semester = sem;
        }
    }
}