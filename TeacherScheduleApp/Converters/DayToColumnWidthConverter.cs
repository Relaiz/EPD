using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TeacherScheduleApp.Converters
{
    public class DayToColumnWidthConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo _)
        {
            double total = 700;
            int days = System.Convert.ToInt32(p);
            return total / days;
        }

        object? IValueConverter.ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
