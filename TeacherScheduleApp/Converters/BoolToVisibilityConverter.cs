using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace TeacherScheduleApp.Converters
{
    public class BoolToIsVisibleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool flag = value is bool b && b;
            return flag; 
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b;
            return false;
        }
    }
}
