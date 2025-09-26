using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        private Point _categoryStartPoint;
        private bool _categoryIsDragging = false;

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
                    _draggedFeed = null;
                }
            }
        }

        private void DragHandle_DragOver(object sender, DragEventArgs e)
        {
            if (sender is not Border border)
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            if (!e.Data.GetDataPresent(typeof(Feed)))
            {
                e.Effects = DragDropEffects.None;
                UpdateRowHighlight(border, null);
                e.Handled = true;
                return;
            }

            var draggedFeed = e.Data.GetData(typeof(Feed)) as Feed ?? _draggedFeed;
            var targetFeed = GetFeedFromBorder(border);

            var canDrop = CanDropOnTarget(draggedFeed, targetFeed);
            e.Effects = canDrop ? DragDropEffects.Move : DragDropEffects.None;
            UpdateRowHighlight(border, canDrop);
            e.Handled = true;
        }

        private void DragHandle_DragEnter(object sender, DragEventArgs e)
        {
            DragHandle_DragOver(sender, e);
        }

        private void DragHandle_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is Border border)
            {
                UpdateRowHighlight(border, null);
            }
        }

        private void DragHandle_Drop(object sender, DragEventArgs e)
        {
            if (sender is Border border)
            {
                UpdateRowHighlight(border, null);
            }

            if (!e.Data.GetDataPresent(typeof(Feed)))
            {
                e.Handled = true;
                return;
            }

            var draggedFeed = e.Data.GetData(typeof(Feed)) as Feed ?? _draggedFeed;
            var targetFeed = sender is Border dropBorder ? GetFeedFromBorder(dropBorder) : null;

            if (draggedFeed == null || targetFeed == null)
            {
                e.Handled = true;
                _draggedFeed = null;
                return;
            }

            if (!CanDropOnTarget(draggedFeed, targetFeed))
            {
                e.Handled = true;
                _draggedFeed = null;
                return;
            }

            if (draggedFeed != targetFeed && DataContext is FeedManagerViewModel vm)
            {
                vm.ReorderFeed(draggedFeed, targetFeed);
            }

            _draggedFeed = null;
            e.Handled = true;
        }

        private Feed GetFeedFromBorder(Border border)
        {
            if (border == null)
            {
                return null;
            }

            return border.DataContext as Feed ?? FindParent<DataGridRow>(border)?.DataContext as Feed;
        }

        private bool CanDropOnTarget(Feed draggedFeed, Feed targetFeed)
        {
            if (draggedFeed == null || targetFeed == null)
            {
                return false;
            }

            if (DataContext is FeedManagerViewModel vm && vm.GroupFeedsByCategory)
            {
                return string.Equals(NormalizeCategory(draggedFeed.Category),
                                     NormalizeCategory(targetFeed.Category),
                                     StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }

        private void UpdateRowHighlight(Border border, bool? canDrop)
        {
            if (border == null)
            {
                return;
            }

            var row = FindParent<DataGridRow>(border);
            if (row == null)
            {
                return;
            }

            if (!canDrop.HasValue)
            {
                row.ClearValue(DataGridRow.BackgroundProperty);
                return;
            }

            row.SetValue(DataGridRow.BackgroundProperty,
                canDrop.Value ? Brushes.LightBlue : Brushes.LightCoral);
        }

        private static string NormalizeCategory(string category)
        {
            return string.IsNullOrWhiteSpace(category) ? "Default" : category;
        }
        private void CategoryListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _categoryStartPoint = e.GetPosition(null);
            _categoryIsDragging = false;
        }

        private void CategoryListBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && !_categoryIsDragging)
            {
                Point mousePos = e.GetPosition(null);
                Vector diff = _categoryStartPoint - mousePos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    var listBox = sender as ListBox;
                    var selectedCategory = listBox?.SelectedItem as Category;

                    if (selectedCategory != null && selectedCategory.Name != "Default")
                    {
                        _categoryIsDragging = true;
                        DragDrop.DoDragDrop(listBox, selectedCategory, DragDropEffects.Move);
                        _categoryIsDragging = false;
                    }
                }
            }
        }

        private void CategoryListBox_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(Category)))
            {
                e.Effects = DragDropEffects.Move;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void CategoryListBox_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(Category)))
            {
                var draggedCategory = e.Data.GetData(typeof(Category)) as Category;
                var listBox = sender as ListBox;

                var dropTarget = GetCategoryFromPoint(listBox, e.GetPosition(listBox));

                if (draggedCategory != null && dropTarget != null &&
                    draggedCategory != dropTarget && draggedCategory.Name != "Default")
                {
                    var viewModel = DataContext as FeedManagerViewModel;
                    viewModel?.ReorderCategory(draggedCategory, dropTarget);
                }
            }
        }

        private Category GetCategoryFromPoint(ListBox listBox, Point point)
        {
            var element = listBox.InputHitTest(point) as UIElement;
            while (element != null)
            {
                if (element is ListBoxItem item)
                {
                    return item.DataContext as Category;
                }
                element = VisualTreeHelper.GetParent(element) as UIElement;
            }
            return null;
        }

        private T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            if (child == null)
            {
                return null;
            }

            var parent = System.Windows.Media.VisualTreeHelper.GetParent(child);
            return parent is T t ? t : FindParent<T>(parent);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
            => Close();
    }
}
