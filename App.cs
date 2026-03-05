using System;
using System.Runtime.InteropServices;
using System.Windows;
using MyNewsFeeder.Views;
using MyNewsFeeder.ViewModels;

namespace MyNewsFeeder
{
    public partial class App : Application
    {
        private const string AppUserModelId = "MyNewsFeeder";

        [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(string appID);

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            try
            {
                _ = SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
            }
            catch
            {
                // Ignore failures; app can still run without an explicit AppUserModelID.
            }

            var mainWindow = new MainWindow();

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