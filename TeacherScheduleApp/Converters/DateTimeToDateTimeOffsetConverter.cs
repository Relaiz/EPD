using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace TeacherScheduleApp.Converters;
public class DateTimeToDateTimeOffsetConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DateTime dt)
            return (DateTimeOffset?)new DateTimeOffset(dt);
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DateTimeOffset dto)
            return dto.DateTime;
        return DateTime.MinValue;
    }
}
