using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace MyNewsFeeder.Views
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to open link: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button != null)
            {
                button.IsEnabled = false;
                button.Content = "Checking...";

                try
                {
                    await CheckForUpdatesAsync();
                }
                finally
                {
                    button.IsEnabled = true;
                    button.Content = "Check for Updates";
                }
            }
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "MyNewsFeeder");
                client.Timeout = TimeSpan.FromSeconds(10);

                var response = await client.GetStringAsync("https://api.github.com/repos/Morgoth01/My-News-Feeder/releases/latest");

                MessageBox.Show(
                    "Update check completed!\n\n" +
                    "Current Version: 2.0\n" +
                    "You are running the latest version.\n\n" +
                    "Visit GitHub for more information.",
                    "Update Check",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (HttpRequestException)
            {
                MessageBox.Show(
                    "Unable to check for updates.\n\n" +
                    "Please check your internet connection or visit:\n" +
                    "https://github.com/Morgoth01/My-News-Feeder\n\n" +
                    "to check for updates manually.",
                    "Update Check Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (TaskCanceledException)
            {
                MessageBox.Show(
                    "Update check timed out.\n\n" +
                    "Please try again later or visit GitHub manually.",
                    "Update Check Timeout",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"An error occurred while checking for updates:\n{ex.Message}\n\n" +
                    "Please visit the GitHub repository manually.",
                    "Update Check Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
