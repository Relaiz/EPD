using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TeacherScheduleApp.Converters
{
    public class DayToCanvasLeftConverter : IValueConverter
    {
        public object Convert(object? v, Type t, object? p, CultureInfo _)
        {
            int day = System.Convert.ToInt32(v);
            return day * 100;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
