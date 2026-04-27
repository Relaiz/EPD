using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace TeacherScheduleApp.Converters
{
    public class EnumToBooleanConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;

            string checkValue = value.ToString() ?? string.Empty;
            string targetValue = parameter.ToString() ?? string.Empty;

            return checkValue.Equals(targetValue, StringComparison.InvariantCultureIgnoreCase);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (parameter == null)
                return null;

            var enumValue = parameter.ToString();
            return enumValue == null ? null : Enum.Parse(targetType, enumValue);
        }
    }
}
