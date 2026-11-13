using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using MyNewsFeeder.ViewModels;

namespace MyNewsFeeder.Views
{
    public partial class SettingsDialog : Window
    {
        private Dictionary<string, FrameworkElement> _topicPanels;

        public SettingsDialog()
        {
            InitializeComponent();
            WindowThemeHelper.Prepare(this);
        }

        private void SettingsDialog_Loaded(object sender, RoutedEventArgs e)
        {
            _topicPanels = new Dictionary<string, FrameworkElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["general"] = GeneralPanel,
                ["terminal"] = TerminalPanel,
                ["refresh"] = RefreshPanel,
                ["feed"] = FeedPanel,
                ["adblocker"] = AdblockerPanel,
                ["filter"] = FilterPanel,
                ["tools"] = ToolsPanel
            };

            ApplySelectedTopic();
        }

        private void TopicsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplySelectedTopic();
        }

        private void ApplySelectedTopic()
        {
            if (_topicPanels == null)
            {
                return;
            }

            foreach (var panel in _topicPanels.Values)
            {
                panel.Visibility = Visibility.Collapsed;
            }

            if (TopicsListBox.SelectedItem is not ListBoxItem selectedItem)
            {
                return;
            }

            var topicKey = selectedItem.Tag as string;
            if (string.IsNullOrWhiteSpace(topicKey))
            {
                return;
            }

            if (_topicPanels.TryGetValue(topicKey, out var selectedPanel))
            {
                selectedPanel.Visibility = Visibility.Visible;
                SelectedTopicTitle.Text = selectedItem.Content?.ToString() ?? "Settings";
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.DiscardPendingSettingsChanges();
            }

            Close();
        }
    }
}