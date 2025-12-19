using System;
using System.Globalization;
using System.Net;
using System.Windows.Data;
using HtmlAgilityPack;

namespace MyNewsFeeder.Converters
{
    public class HtmlToPlainTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var html = value as string;
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            try
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                var text = doc.DocumentNode.InnerText;
                return WebUtility.HtmlDecode(text).Trim();
            }
            catch
            {
                return WebUtility.HtmlDecode(html);
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }
}