using System;
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
                if (Current.MainWindow is MainWindow mainWindow &&
                    mainWindow.DataContext is MainViewModel viewModel)
                {
                    viewModel.Cleanup();
                }
            }
            catch (Exception)
            {
                // Ignore cleanup failures during shutdown.
            }

            base.OnExit(e);
        }
    }
}