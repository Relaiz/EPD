using Avalonia.Data.Converters;
using System;
using System.Globalization;


namespace TeacherScheduleApp.Converters
{
    public sealed class BoolNotConverter : IValueConverter
    {
        public static readonly BoolNotConverter Instance = new();
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is bool b ? !b : value;
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is bool b ? !b : value;
    }
}
