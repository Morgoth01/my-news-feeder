using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using System.Windows.Interop;
using System.Threading.Tasks;
using MyNewsFeeder.Services;

namespace MyNewsFeeder.Views
{
    public partial class AboutWindow : Window
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
        private readonly SettingsService _settingsService = new SettingsService();
        private Models.AppSettings _settings;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

        public AboutWindow()
        {
            InitializeComponent();

            SourceInitialized += (_, __) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int useDark = 1;
                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, Marshal.SizeOf<int>()) != 0)
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref useDark, Marshal.SizeOf<int>());
            };

            var ver = Assembly.GetEntryAssembly()?.GetName()?.Version;
            var shortVer = ver != null ? ver.ToString(2) : "1.0";
            VersionText.Text = $"Version {shortVer}";

            _settings = _settingsService.LoadSettings() ?? new Models.AppSettings();
            AutoUpdateToggle.IsChecked = _settings.AutoUpdateCheckEnabled;
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                e.Handled = true;
            }
            catch
            {
                MessageBox.Show($"Unable to open link: {e.Uri}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                btn.IsEnabled = false;
                btn.Content = "Checking...";
                try { await CheckForUpdatesAsync(); }
                finally
                {
                    btn.IsEnabled = true;
                    btn.Content = "Check for Updates";
                }
            }
        }

        private void AutoUpdateToggle_Checked(object sender, RoutedEventArgs e)
        {
            var enabled = AutoUpdateToggle.IsChecked == true;
            _settings.AutoUpdateCheckEnabled = enabled;
            _settingsService.SaveSettings(_settings);

            // propagate to main view model so later saves keep the preference
            if (Application.Current?.MainWindow?.DataContext is MyNewsFeeder.ViewModels.MainViewModel vm)
            {
                vm.SetAutoUpdatePreference(enabled);
            }
        }

        private async Task CheckForUpdatesAsync()
        {
            await UpdateChecker.RunInteractiveCheckAsync(this, showUpToDateMessage: true, showFailureMessage: true);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
