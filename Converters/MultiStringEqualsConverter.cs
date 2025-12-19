using System;
using System.Globalization;
using System.Windows.Data;

namespace MyNewsFeeder.Converters
{
    public class MultiStringEqualsConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
            {
                return false;
            }

            var first = values[0]?.ToString();
            var second = values[1]?.ToString();
            if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
            {
                return false;
            }

            return string.Equals(first.Trim(), second.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException("ConvertBack is not supported for MultiStringEqualsConverter.");
        }
    }
}