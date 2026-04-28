using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
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

        private static void WriteDiagnosticLog(string source, Exception ex = null, string details = null)
        {
            try
            {
                var logDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MyNewsFeeder",
                    "logs");
                Directory.CreateDirectory(logDirectory);

                var logPath = Path.Combine(logDirectory, "app-diagnostics.log");
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{source}]";
                if (!string.IsNullOrWhiteSpace(details))
                {
                    line += $" {details}";
                }

                if (ex != null)
                {
                    line += $"{Environment.NewLine}{ex}";
                }

                File.AppendAllText(logPath, line + Environment.NewLine + Environment.NewLine);
            }
            catch
            {
                // Ignore diagnostics logging failures.
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += (_, args) =>
            {
                WriteDiagnosticLog("DispatcherUnhandledException", args.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                WriteDiagnosticLog(
                    "AppDomainUnhandledException",
                    args.ExceptionObject as Exception,
                    $"IsTerminating={args.IsTerminating}");
            };
            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                WriteDiagnosticLog("TaskSchedulerUnobservedTaskException", args.Exception);
            };
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