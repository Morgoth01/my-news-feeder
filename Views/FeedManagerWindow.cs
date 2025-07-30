using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using MyNewsFeeder.Models;
using MyNewsFeeder.ViewModels;

namespace MyNewsFeeder.Views
{
    public partial class FeedManagerWindow : Window
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

        private Feed _draggedFeed;

        public FeedManagerWindow()
        {
            InitializeComponent();
            SourceInitialized += (_, __) => EnableDarkTitleBar();
        }

        private void EnableDarkTitleBar()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int useDark = 1;
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, Marshal.SizeOf<int>()) != 0)
            {
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref useDark, Marshal.SizeOf<int>());
            }
        }

        private void DragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border)
            {
                var row = FindParent<DataGridRow>(border);
                if (row?.DataContext is Feed feed)
                {
                    _draggedFeed = feed;
                    DragDrop.DoDragDrop(border, feed, DragDropEffects.Move);
                }
            }
        }

        private void DragHandle_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(Feed)))
            {
                e.Effects = DragDropEffects.Move;
                if (sender is Border border)
                {
                    var row = FindParent<DataGridRow>(border);
                    row?.SetValue(DataGridRow.BackgroundProperty, System.Windows.Media.Brushes.LightBlue);
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void DragHandle_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(Feed)) && sender is Border border)
            {
                var row = FindParent<DataGridRow>(border);
                row?.SetValue(DataGridRow.BackgroundProperty, System.Windows.Media.Brushes.LightGreen);
            }
        }

        private void DragHandle_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is Border border)
            {
                var row = FindParent<DataGridRow>(border);
                row?.ClearValue(DataGridRow.BackgroundProperty);
            }
        }

        private void DragHandle_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(Feed)) && sender is Border border)
            {
                var draggedFeed = e.Data.GetData(typeof(Feed)) as Feed;
                var targetRow = FindParent<DataGridRow>(border);

                if (targetRow?.DataContext is Feed targetFeed &&
                    draggedFeed != null && draggedFeed != targetFeed &&
                    DataContext is FeedManagerViewModel vm)
                {
                    vm.ReorderFeed(draggedFeed, targetFeed);
                }
                targetRow?.ClearValue(DataGridRow.BackgroundProperty);
            }
            e.Handled = true;
        }

        private T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = System.Windows.Media.VisualTreeHelper.GetParent(child);
            return parent is T t ? t : FindParent<T>(parent);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
            => Close();
    }
}
