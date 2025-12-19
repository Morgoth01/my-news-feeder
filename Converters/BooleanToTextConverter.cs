using System;
using System.Globalization;
using System.Windows.Data;

namespace MyNewsFeeder.Converters
{
    /// <summary>
    /// Converts a boolean to one of two strings provided via ConverterParameter in the form "TrueText|FalseText".
    /// </summary>
    public class BooleanToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter is not string param || !param.Contains("|"))
            {
                return value is bool flag && flag ? "On" : "Off";
            }

            var parts = param.Split('|');
            var trueText = parts.Length > 0 ? parts[0] : string.Empty;
            var falseText = parts.Length > 1 ? parts[1] : string.Empty;

            return value is bool flagValue && flagValue ? trueText : falseText;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}