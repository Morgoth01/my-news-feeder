using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using MyNewsFeeder.Models;
using System.Linq;
using System.Windows.Input;
using MyNewsFeeder.Services;
using MyNewsFeeder.ViewModels;
using MaterialDesignThemes.Wpf;

namespace MyNewsFeeder.Views
{
    public partial class MainWindow : Window
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

        private MainViewModel _viewModel;
        private bool _warmupDone;

        public MainWindow()
        {
            InitializeComponent();

            _viewModel = new MainViewModel(new FeedService(), new SettingsService(), new BrowserService());
            DataContext = _viewModel;
            try
            {
                if (FindName("RefreshSummarySnackbar") is Snackbar snackbar)
                {
                    snackbar.MessageQueue = _viewModel.SnackbarMessageQueue as SnackbarMessageQueue;
                }
            }
            catch
            {
                // If binding fails, the XAML binding will still attempt to attach the queue.
            }
            _viewModel.SelectionRestoreRequested += ViewModel_SelectionRestoreRequested;
            _viewModel.RequestTreeScrollOffset = GetTreeScrollOffset;
            _viewModel.ScrollOffsetRestoreRequested += ViewModel_ScrollOffsetRestoreRequested;

            Loaded += MainWindow_Loaded;
            PreviewKeyDown += MainWindow_PreviewKeyDown;
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Let text inputs handle their own navigation
            if (Keyboard.FocusedElement is System.Windows.Controls.TextBox ||
                Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase)
            {
                return;
            }

            if (DataContext is MainViewModel vm)
            {
                if (e.Key == Key.Up && vm.NavigateSelectionCommand?.CanExecute(-1) == true)
                {
                    vm.NavigateSelectionCommand.Execute(-1);
                    e.Handled = true;
                }
                else if (e.Key == Key.Down && vm.NavigateSelectionCommand?.CanExecute(1) == true)
                {
                    vm.NavigateSelectionCommand.Execute(1);
                    e.Handled = true;
                }
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // dark title‐bar on Win10+
            var hwnd = new WindowInteropHelper(this).Handle;
            int useDark = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, Marshal.SizeOf(typeof(int)));

            // initialize webviews
            _viewModel = DataContext as MainViewModel;
            var env = await _viewModel.GetSharedWebViewEnvironmentAsync();
            await linkWebView.EnsureCoreWebView2Async(env);
            await articleWebView.EnsureCoreWebView2Async(env);
            _viewModel.SetWebView(linkWebView);
            _viewModel.SetArticleWebView(articleWebView);
            _viewModel.ClearBrowserOnStartup();
            await PreWarmBrowserWebViewAsync();
        }

        private async Task PreWarmBrowserWebViewAsync()
        {
            if (_warmupDone || linkWebView?.CoreWebView2 == null)
            {
                return;
            }

            _warmupDone = true;
            try
            {
                // Hide without breaking bindings
                linkWebView.SetCurrentValue(VisibilityProperty, Visibility.Hidden);

                var tcs = new TaskCompletionSource<bool>();
                void Handler(object s, CoreWebView2NavigationCompletedEventArgs e)
                {
                    linkWebView.CoreWebView2.NavigationCompleted -= Handler;
                    tcs.TrySetResult(true);
                }
                linkWebView.CoreWebView2.NavigationCompleted += Handler;

                linkWebView.CoreWebView2.Navigate("https://github.com/Morgoth01/my-news-feeder");
                await Task.WhenAny(tcs.Task, Task.Delay(3000));
                linkWebView.CoreWebView2.Navigate("about:blank");
            }
            catch
            {
                // ignore warm-up failures
            }
            finally
            {
                // Restore binding-driven visibility
                linkWebView.ClearValue(VisibilityProperty);
            }
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var aboutWindow = new AboutWindow { Owner = this };
                aboutWindow.ShowDialog();
            }
            catch (Exception)
            {
                // Ignore failures when opening the About window.
            }
        }

        private void CloseSettingsPopup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var popupBox = FindName("SettingsPopupBox") as PopupBox;
                if (popupBox != null)
                    popupBox.IsPopupOpen = false;
            }
            catch { }
        }

        private void GridSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            try
            {
                if (_viewModel != null &&
                    ((FrameworkElement)sender).Parent is Grid mainGrid &&
                    mainGrid.RowDefinitions.Count >= 3)
                {
                    _viewModel.ArticleWindowHeight = mainGrid.RowDefinitions[0].ActualHeight;
                    _viewModel.BrowserWindowHeight = mainGrid.RowDefinitions[2].ActualHeight;
                }
            }
            catch { }
        }

        private void SystemEvents_DisplaySettingsChanged(object sender, EventArgs e)
        {
            // handled in ViewModel
        }

        protected override void OnClosed(EventArgs e)
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
            base.OnClosed(e);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.SaveSectionExpandedStates();
                vm.SaveCategoryExpandedStates();
                vm.SaveFeedExpandedStates();
            }
        }

        private void ViewModel_SelectionRestoreRequested(string linkToRestore)
        {
            if (string.IsNullOrWhiteSpace(linkToRestore) || _viewModel?.ArticleSections == null)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    foreach (var section in _viewModel.ArticleSections)
                    {
                        foreach (var child in section.Items)
                        {
                            if (child is CategoryGroupViewModel category)
                            {
                                if (category.Feeds == null) continue;
                                foreach (var feed in category.Feeds)
                                {
                                    if (feed.Items == null) continue;
                                    var match = feed.Items.FirstOrDefault(a =>
                                        !string.IsNullOrWhiteSpace(a.Link) &&
                                        string.Equals(a.Link.Trim(), linkToRestore.Trim(), StringComparison.OrdinalIgnoreCase));

                                    if (match != null)
                                    {
                                        _viewModel.OnArticleSelected(match);
                                        return;
                                    }
                                }
                            }
                            else if (child is FeedGroupViewModel feedGroup)
                            {
                                if (feedGroup.Items == null) continue;
                                var match = feedGroup.Items.FirstOrDefault(a =>
                                    !string.IsNullOrWhiteSpace(a.Link) &&
                                    string.Equals(a.Link.Trim(), linkToRestore.Trim(), StringComparison.OrdinalIgnoreCase));

                                if (match != null)
                                {
                                    _viewModel.OnArticleSelected(match);
                                    return;
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore selection restore failures; user selection remains unchanged.
                }
            }), DispatcherPriority.Background);
        }

        private double GetTreeScrollOffset()
        {
            return SectionScrollViewer?.VerticalOffset ?? 0;
        }

        private void ViewModel_ScrollOffsetRestoreRequested(double offset)
        {
            if (SectionScrollViewer == null)
            {
                return;
            }

            void ApplyOffset()
            {
                try
                {
                    SectionScrollViewer.UpdateLayout();
                    if (SectionScrollViewer != null)
                    {
                        SectionScrollViewer.ScrollToVerticalOffset(offset);
                    }
                }
                catch
                {
                    // Ignore scrolling failures.
                }
            }

            // Apply once after current layout work, then once more after render to fight virtualization resets.
            Dispatcher.BeginInvoke(new Action(ApplyOffset), DispatcherPriority.ContextIdle);
            Dispatcher.BeginInvoke(new Action(ApplyOffset), DispatcherPriority.ApplicationIdle);
        }

        private static ScrollViewer FindScrollViewer(DependencyObject root)
        {
            if (root == null) return null;
            if (root is ScrollViewer viewer) return viewer;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var result = FindScrollViewer(child);
                if (result != null) return result;
            }

            return null;
        }
    }
}