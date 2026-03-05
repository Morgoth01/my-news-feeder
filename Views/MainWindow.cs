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
        private HelpWindow _helpWindow;
        private SettingsDialog _settingsWindow;

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
            _viewModel.ScrollSelectionToTopRequested += ViewModel_ScrollSelectionToTopRequested;

            Loaded += MainWindow_Loaded;
            PreviewKeyDown += MainWindow_PreviewKeyDown;
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
            {
                KeywordTextBox?.Focus();
                KeywordTextBox?.SelectAll();
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.H)
            {
                ShowHelpWindow();
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.OemComma)
            {
                ShowSettingsWindow();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                if (_settingsWindow != null && _settingsWindow.IsLoaded && _settingsWindow.IsActive)
                {
                    _settingsWindow.Close();
                    e.Handled = true;
                    return;
                }
            }

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

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            ShowHelpWindow();
        }

        private void HelpMenuContent_Click(object sender, RoutedEventArgs e)
        {
            CloseHelpMenuPopup();
            ShowHelpWindow();
        }

        private void HelpMenuShortcuts_Click(object sender, RoutedEventArgs e)
        {
            CloseHelpMenuPopup();
            ShowHelpWindow("shortcuts");
        }

        private void HelpMenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.ContextMenu != null)
            {
                button.ContextMenu.PlacementTarget = button;
                button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                button.ContextMenu.IsOpen = true;
            }
        }

        private async void HelpMenuCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            CloseHelpMenuPopup();
            try
            {
                await UpdateChecker.RunInteractiveCheckAsync(this, showUpToDateMessage: true, showFailureMessage: true);
            }
            catch
            {
                // Ignore update check failures; UpdateChecker handles user-facing messages.
            }
        }

        private void HelpMenuAbout_Click(object sender, RoutedEventArgs e)
        {
            CloseHelpMenuPopup();
            AboutButton_Click(sender, e);
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsWindow();
        }

        private void ShowHelpWindow(string topicId = null)
        {
            try
            {
                if (_helpWindow == null || !_helpWindow.IsLoaded)
                {
                    _helpWindow = new HelpWindow
                    {
                        Owner = this
                    };
                    _helpWindow.Closed += HelpWindow_Closed;
                    _helpWindow.Show();
                    if (!string.IsNullOrWhiteSpace(topicId))
                    {
                        _helpWindow.SelectTopicById(topicId);
                    }
                    return;
                }

                if (_helpWindow.WindowState == WindowState.Minimized)
                {
                    _helpWindow.WindowState = WindowState.Normal;
                }

                if (!string.IsNullOrWhiteSpace(topicId))
                {
                    _helpWindow.SelectTopicById(topicId);
                }

                _helpWindow.Activate();
            }
            catch (Exception)
            {
                // Ignore failures when opening the Help window.
            }
        }

        private void HelpWindow_Closed(object sender, EventArgs e)
        {
            if (_helpWindow != null)
            {
                _helpWindow.Closed -= HelpWindow_Closed;
                _helpWindow = null;
            }
        }

        private void ShowSettingsWindow()
        {
            try
            {
                if (_settingsWindow == null || !_settingsWindow.IsLoaded)
                {
                    _settingsWindow = new SettingsDialog
                    {
                        Owner = this,
                        DataContext = DataContext
                    };
                    _settingsWindow.Closed += SettingsWindow_Closed;
                    _settingsWindow.Show();
                    return;
                }

                if (_settingsWindow.WindowState == WindowState.Minimized)
                {
                    _settingsWindow.WindowState = WindowState.Normal;
                }

                _settingsWindow.Activate();
            }
            catch
            {
                // Ignore failures when opening the Settings window.
            }
        }

        private void SettingsWindow_Closed(object sender, EventArgs e)
        {
            if (_settingsWindow != null)
            {
                _settingsWindow.Closed -= SettingsWindow_Closed;
                _settingsWindow = null;
            }
        }

        private void CloseHelpMenuPopup()
        {
            try
            {
                if (FindName("HelpMenuButton") is Button button && button.ContextMenu != null)
                {
                    button.ContextMenu.IsOpen = false;
                }
            }
            catch
            {
                // Ignore popup close failures.
            }
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

            try
            {
                if (_helpWindow != null)
                {
                    _helpWindow.Close();
                    _helpWindow = null;
                }

                if (_settingsWindow != null)
                {
                    _settingsWindow.Close();
                    _settingsWindow = null;
                }
            }
            catch
            {
                // Ignore helper window shutdown issues during app close.
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
                                        _viewModel.OnArticleSelected(
                                            match,
                                            enableAutoScroll: false,
                                            preserveOpenWebViews: true);
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
                                    _viewModel.OnArticleSelected(
                                        match,
                                        enableAutoScroll: false,
                                        preserveOpenWebViews: true);
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

        private void ViewModel_ScrollSelectionToTopRequested(FeedItem targetItem)
        {
            if (targetItem == null || SectionScrollViewer == null)
            {
                return;
            }

            void Align()
            {
                TryAlignFeedItemToTop(targetItem);
            }

            // Run across multiple priorities to handle deferred container generation.
            Dispatcher.BeginInvoke(new Action(Align), DispatcherPriority.Loaded);
            Dispatcher.BeginInvoke(new Action(Align), DispatcherPriority.ContextIdle);
            Dispatcher.BeginInvoke(new Action(Align), DispatcherPriority.ApplicationIdle);
        }

        private bool TryAlignFeedItemToTop(FeedItem targetItem)
        {
            if (targetItem == null || SectionScrollViewer == null)
            {
                return false;
            }

            SectionScrollViewer.UpdateLayout();
            var element = FindElementForDataContext(SectionScrollViewer, targetItem);
            if (element == null)
            {
                return false;
            }

            try
            {
                const double headerTopPadding = 6;
                const double itemTopPaddingFallback = 56;

                var anchor = (FrameworkElement)FindAncestor<Expander>(element) ?? element;
                var relativePoint = anchor.TransformToAncestor(SectionScrollViewer).Transform(new Point(0, 0));
                var padding = ReferenceEquals(anchor, element) ? itemTopPaddingFallback : headerTopPadding;
                var desiredOffset = Math.Max(0, SectionScrollViewer.VerticalOffset + relativePoint.Y - padding);
                SectionScrollViewer.ScrollToVerticalOffset(desiredOffset);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static T FindAncestor<T>(DependencyObject child) where T : DependencyObject
        {
            var current = child;
            while (current != null)
            {
                if (current is T typed)
                {
                    return typed;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private static FrameworkElement FindElementForDataContext(DependencyObject root, object dataContext)
        {
            if (root == null || dataContext == null)
            {
                return null;
            }

            if (root is FrameworkElement element &&
                ReferenceEquals(element.DataContext, dataContext) &&
                element.ActualHeight > 0 &&
                element.Visibility == Visibility.Visible)
            {
                return element;
            }

            var childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var result = FindElementForDataContext(child, dataContext);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
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