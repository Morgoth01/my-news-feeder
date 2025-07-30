using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using System.Windows.Interop;

namespace MyNewsFeeder.Views
{
    public partial class AboutWindow : Window
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

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

        private async Task CheckForUpdatesAsync()
        {
            var currentVersion = Assembly.GetEntryAssembly()?.GetName()?.Version ?? new Version(1, 0, 0, 0);
            var current = currentVersion.ToString(2);
            JsonDocument doc = null;
            JsonElement release;

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd($"MyNewsFeeder/{current}");
                client.Timeout = TimeSpan.FromSeconds(10);
                var json = await client.GetStringAsync(
                    "https://api.github.com/repos/Morgoth01/My-News-Feeder/releases/latest");
                doc = JsonDocument.Parse(json);
                release = doc.RootElement;
            }
            catch
            {
                MessageBox.Show(
                    "Update check failed.\nVisit https://github.com/Morgoth01/My-News-Feeder/releases/latest",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                doc?.Dispose();
                return;
            }

            var tag = release.GetProperty("tag_name").GetString() ?? "";
            if (tag.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                tag = tag.Substring(1);
            if (!Version.TryParse(tag, out var latestVersion))
            {
                MessageBox.Show($"Could not parse version: {tag}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                doc.Dispose();
                return;
            }

            var latest = latestVersion.ToString(2);
            if (latestVersion <= currentVersion)
            {
                MessageBox.Show($"You are running the latest version ({current}).", "Up to Date", MessageBoxButton.OK, MessageBoxImage.Information);
                doc.Dispose();
                return;
            }

            if (MessageBox.Show(
                    $"A new version {latest} is available.\nYou have {current}.\n\nDownload now?",
                    "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Question)
                != MessageBoxResult.Yes)
            {
                doc.Dispose();
                return;
            }

            string zipUrl = null;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
                {
                    zipUrl = asset.GetProperty("browser_download_url").GetString();
                    break;
                }
            }

            if (zipUrl == null)
            {
                MessageBox.Show("No ZIP or 7z asset found in the latest release.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                doc.Dispose();
                return;
            }

            var fileName = Path.GetFileName(new Uri(zipUrl).LocalPath);
            var savePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads", fileName);

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd($"MyNewsFeeder/{current}");
                using var resp = await client.GetAsync(zipUrl, HttpCompletionOption.ResponseHeadersRead);
                resp.EnsureSuccessStatusCode();
                await using var fs = File.Create(savePath);
                await resp.Content.CopyToAsync(fs);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Download failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                doc.Dispose();
                return;
            }

            MessageBox.Show(
                $"Downloaded to:\n{savePath}\n\nPlease unzip and replace the old files.\nVisit https://github.com/Morgoth01/My-News-Feeder/releases/latest for release notes.",
                "Download Complete", MessageBoxButton.OK, MessageBoxImage.Information);

            doc.Dispose();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
