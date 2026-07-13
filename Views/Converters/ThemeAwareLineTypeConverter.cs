using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using MyNewsFeeder.ViewModels;

namespace MyNewsFeeder.Views.Converters
{
    public class ThemeAwareLineTypeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TerminalLineType lineType)
            {
                var theme = TerminalViewModel.StaticCurrentTheme;
                return lineType switch
                {
                    TerminalLineType.Command => new SolidColorBrush(theme.Command),
                    TerminalLineType.Error => new SolidColorBrush(theme.Error),
                    TerminalLineType.Success => new SolidColorBrush(theme.Success),
                    TerminalLineType.Warning => new SolidColorBrush(theme.Warning),
                    TerminalLineType.Dim => new SolidColorBrush(theme.Dim),
                    TerminalLineType.Header => new SolidColorBrush(theme.Header),
                    _ => new SolidColorBrush(theme.Foreground)
                };
            }
            return new SolidColorBrush(TerminalViewModel.StaticCurrentTheme.Foreground);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}