using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MyNewsFeeder.Converters
{
    /// <summary>
    /// Converts double values to GridLength for dynamic row/column sizing.
    /// </summary>
    public class DoubleToGridLengthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (value is double doubleValue && doubleValue > 0)
                {
                    return new GridLength(doubleValue, GridUnitType.Pixel);
                }

                // Default fallback
                return new GridLength(350, GridUnitType.Pixel);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in DoubleToGridLengthConverter: {ex.Message}");
                return new GridLength(350, GridUnitType.Pixel);
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (value is GridLength gridLength && gridLength.GridUnitType == GridUnitType.Pixel)
                {
                    return gridLength.Value;
                }

                return 350.0; // Default fallback
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in DoubleToGridLengthConverter ConvertBack: {ex.Message}");
                return 350.0;
            }
        }
    }
}
