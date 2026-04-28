using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using MyNewsFeeder.Models;

namespace MyNewsFeeder.Converters
{
    public class CollectionViewGroupCategoryConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not CollectionViewGroup group)
            {
                return string.Empty;
            }

            var firstItem = group.Items.OfType<FeedItem>().FirstOrDefault();
            return string.IsNullOrWhiteSpace(firstItem?.CategoryName)
                ? string.Empty
                : firstItem.CategoryName;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}