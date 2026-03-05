using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace MyNewsFeeder.Views
{
    public partial class SettingsDialog : Window
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

        private Dictionary<string, FrameworkElement> _topicPanels;

        public SettingsDialog()
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

        private void SettingsDialog_Loaded(object sender, RoutedEventArgs e)
        {
            _topicPanels = new Dictionary<string, FrameworkElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["general"] = GeneralPanel,
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
            Close();
        }
    }
}