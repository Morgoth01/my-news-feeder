using System.Windows;
using System.Windows.Controls;
using MyNewsFeeder.ViewModels;

namespace MyNewsFeeder.Views
{
    public class TreeViewItemStyleSelector : StyleSelector
    {
        public Style CategoryStyle { get; set; }
        public Style FeedStyle { get; set; }
        public Style DefaultStyle { get; set; }

        public override Style SelectStyle(object item, DependencyObject container)
        {
            if (item is CategoryGroupViewModel) return CategoryStyle;
            if (item is FeedGroupViewModel) return FeedStyle;
            return DefaultStyle;
        }
    }
}