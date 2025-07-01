using System;
using System.Threading.Tasks;
using System.Windows;
using MyNewsFeeder.Views;
using MyNewsFeeder.ViewModels;
using MyNewsFeeder.Services;

namespace MyNewsFeeder
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var settingsService = new SettingsService();
            var feedService = new FeedService();
            var browserService = new BrowserService();

            var mainViewModel = new MainViewModel(feedService, settingsService, browserService);

            var mainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };

            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                var mainWindow = Current.MainWindow as MainWindow;
                var viewModel = mainWindow?.DataContext as MainViewModel;

                if (viewModel != null)
                {
                    viewModel.Cleanup();
                    System.Diagnostics.Debug.WriteLine("🧹 App cleanup completed");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error during app exit: {ex.Message}");
            }

            base.OnExit(e);
        }
    }
}