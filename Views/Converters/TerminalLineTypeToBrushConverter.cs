using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using MyNewsFeeder.ViewModels;

namespace MyNewsFeeder.Views.Converters
{
    public class TerminalLineTypeToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TerminalLineType lineType)
            {
                return lineType switch
                {
                    TerminalLineType.Command => new SolidColorBrush(Color.FromRgb(0xFF, 0x55, 0xFF)),
                    TerminalLineType.Error => new SolidColorBrush(Color.FromRgb(0xFF, 0x55, 0x55)),
                    TerminalLineType.Success => new SolidColorBrush(Color.FromRgb(0x55, 0xFF, 0x55)),
                    TerminalLineType.Warning => new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0x55)),
                    TerminalLineType.Dim => new SolidColorBrush(Color.FromRgb(0x2F, 0x8F, 0x2F)),
                    TerminalLineType.Header => new SolidColorBrush(Color.FromRgb(0x55, 0xFF, 0xFF)),
                    _ => new SolidColorBrush(Color.FromRgb(0x5E, 0xE7, 0x5E))
                };
            }
            return new SolidColorBrush(Color.FromRgb(0x5E, 0xE7, 0x5E));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}