using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MyNewsFeeder.Models;
using MyNewsFeeder.ViewModels;

namespace MyNewsFeeder.Views
{
    public partial class FeedManagerWindow : Window
    {
        private bool _isDragging = false;
        private Feed _draggedFeed;

        public FeedManagerWindow()
        {
            InitializeComponent();
        }

        // Event-Handler nur für das Drag Handle
        private void DragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border)
            {
                // Finde die zugehörige DataGridRow
                var row = FindParent<DataGridRow>(border);
                if (row?.DataContext is Feed feed)
                {
                    _draggedFeed = feed;
                    _isDragging = true;

                    // Start drag operation
                    DragDrop.DoDragDrop(border, feed, DragDropEffects.Move);
                    _isDragging = false;
                }
            }
        }

        private void DragHandle_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(Feed)))
            {
                e.Effects = DragDropEffects.Move;

                // Visual feedback auf der Row
                var border = sender as Border;
                var row = FindParent<DataGridRow>(border);
                if (row != null)
                {
                    row.Background = System.Windows.Media.Brushes.LightBlue;
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
            if (e.Data.GetDataPresent(typeof(Feed)))
            {
                var border = sender as Border;
                var row = FindParent<DataGridRow>(border);
                if (row != null)
                {
                    row.Background = System.Windows.Media.Brushes.LightGreen;
                }
            }
        }

        private void DragHandle_DragLeave(object sender, DragEventArgs e)
        {
            var border = sender as Border;
            var row = FindParent<DataGridRow>(border);
            if (row != null)
            {
                row.ClearValue(DataGridRow.BackgroundProperty);
            }
        }

        private void DragHandle_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(Feed)))
            {
                var draggedFeed = e.Data.GetData(typeof(Feed)) as Feed;
                var border = sender as Border;
                var targetRow = FindParent<DataGridRow>(border);

                if (targetRow?.DataContext is Feed targetFeed &&
                    draggedFeed != null && draggedFeed != targetFeed)
                {
                    // Get ViewModel and perform reorder
                    if (DataContext is FeedManagerViewModel viewModel)
                    {
                        viewModel.ReorderFeed(draggedFeed, targetFeed);
                    }
                }

                // Clear visual feedback
                if (targetRow != null)
                {
                    targetRow.ClearValue(DataGridRow.BackgroundProperty);
                }
            }

            e.Handled = true;
        }

        // Helper method to find parent element
        private T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = System.Windows.Media.VisualTreeHelper.GetParent(child);

            if (parentObject == null) return null;

            T parent = parentObject as T;
            if (parent != null)
                return parent;
            else
                return FindParent<T>(parentObject);
        }
    }
}
