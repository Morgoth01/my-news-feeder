using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MyNewsFeeder.Converters
{
    public class CategoryVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string categoryName && categoryName == "Default")
            {
                return Visibility.Collapsed;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}