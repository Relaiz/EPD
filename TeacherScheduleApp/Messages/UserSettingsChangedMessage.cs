using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TeacherScheduleApp.Messages
{
    public class UserSettingsChangedMessage
    {
        public DateTime Date { get; }
        public UserSettingsChangedMessage(DateTime date) => Date = date;
    }

}
