using System;
using System.Globalization;
using System.Windows.Data;
using MyNewsFeeder.Models;
using MyNewsFeeder.ViewModels;

namespace MyNewsFeeder.Converters
{
    public class FeedMoveConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Feed feed && parameter is string directionStr)
            {
                var direction = directionStr.Equals("up", StringComparison.OrdinalIgnoreCase)
                    ? FeedMoveDirection.Up
                    : FeedMoveDirection.Down;

                return new FeedMoveRequest
                {
                    Feed = feed,
                    Direction = direction
                };
            }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}