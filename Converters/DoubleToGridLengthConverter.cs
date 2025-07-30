using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MyNewsFeeder.Converters
{
    [ValueConversion(typeof(double), typeof(GridLength))]
    public class DoubleToGridLengthConverter : IValueConverter
    {
        // ViewModel -> XAML
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d && d >= 0)
                return new GridLength(d, GridUnitType.Pixel);
            return DependencyProperty.UnsetValue;
        }

        // XAML -> ViewModel
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is GridLength gl && gl.IsAbsolute)
                return gl.Value;
            return DependencyProperty.UnsetValue;
        }
    }
}
