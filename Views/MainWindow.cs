using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        private bool _startupInitializationStarted;
        private HelpWindow _helpWindow;
        private SettingsDialog _settingsWindow;
        private bool _webViewShortcutHandlersAttached;
        private bool _contentFullscreenApplied;
        private GridLength _savedToolbarRowHeight;
        private GridLength _savedSectionsColumnWidth;
        private GridLength _savedSectionsSplitterColumnWidth;
        private GridLength _savedExplorerColumnWidth;
        private GridLength _savedExplorerSplitterColumnWidth;
        private GridLength _savedArticleListColumnWidth;
        private GridLength _savedArticleListSplitterColumnWidth;
        private GridLength _savedArticleContentRowHeight;
        private GridLength _savedArticleSplitterRowHeight;
        private GridLength _savedBrowserContentRowHeight;
        private GridLength _collapsedExplorerColumnWidth;
        private GridLength _collapsedExplorerSplitterColumnWidth;
        private GridLength _collapsedArticleListColumnWidth;
        private GridLength _collapsedArticleListSplitterColumnWidth;
        private double _savedExplorerColumnMinWidth;
        private double _savedArticleListColumnMinWidth;
        private double _savedExpandedExplorerWidth;
        private double _savedExpandedArticleListWidth;
        private double _savedArticleContentRowMinHeight;
        private int _savedArticleBrowserGridColumn;
        private int _savedArticleBrowserGridColumnSpan = 1;
        private WindowStyle _savedWindowStyle;
        private ResizeMode _savedResizeMode;
        private WindowState _savedWindowState;
        private bool _isFeedExplorerCollapsed;
        private bool _isArticleListCollapsed;

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
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;

            Loaded += MainWindow_Loaded;
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            PreviewMouseDown += MainWindow_PreviewMouseDown;
        }

        private void MainWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (KeywordTextBox == null || !KeywordTextBox.IsKeyboardFocusWithin)
            {
                return;
            }

            if (e.OriginalSource is not DependencyObject source)
            {
                return;
            }

            if (IsDescendantOf(source, KeywordTextBox))
            {
                return;
            }

            if (FindAncestor<TextBoxBase>(source) != null || FindAncestor<PasswordBox>(source) != null)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() => Focus()), DispatcherPriority.Input);
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

            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && e.Key == Key.A)
            {
                if (_viewModel?.OpenArchiveWindowCommand?.CanExecute(null) == true)
                {
                    _viewModel.OpenArchiveWindowCommand.Execute(null);
                    e.Handled = true;
                }

                return;
            }

            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && e.Key == Key.P)
            {
                if (_viewModel?.OpenLibraryWindowCommand?.CanExecute(null) == true)
                {
                    _viewModel.OpenLibraryWindowCommand.Execute(null);
                    e.Handled = true;
                }

                return;
            }

            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && e.Key == Key.L)
            {
                if (_viewModel?.OpenReadLaterWindowCommand?.CanExecute(null) == true)
                {
                    _viewModel.OpenReadLaterWindowCommand.Execute(null);
                    e.Handled = true;
                }

                return;
            }

            if (e.Key == Key.Escape)
            {
                if (_viewModel?.IsContentFullscreen == true)
                {
                    _viewModel.ExitContentFullscreen();
                    e.Handled = true;
                    return;
                }

                if (_settingsWindow != null && _settingsWindow.IsLoaded && _settingsWindow.IsActive)
                {
                    _settingsWindow.Close();
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.F11)
            {
                if (_viewModel?.ToggleContentFullscreenCommand?.CanExecute(null) == true)
                {
                    _viewModel.ToggleContentFullscreenCommand.Execute(null);
                    e.Handled = true;
                }
                return;
            }

            // Let text inputs handle their own navigation
            if (Keyboard.FocusedElement is System.Windows.Controls.TextBox ||
                Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase)
            {
                return;
            }

            if (DataContext is MainViewModel vm)
            {
                if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.PageUp && vm.NavigateFeedCommand?.CanExecute(-1) == true)
                {
                    vm.NavigateFeedCommand.Execute(-1);
                    e.Handled = true;
                }
                else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.PageDown && vm.NavigateFeedCommand?.CanExecute(1) == true)
                {
                    vm.NavigateFeedCommand.Execute(1);
                    e.Handled = true;
                }
                else if (e.Key == Key.PageUp && vm.NavigateSelectionCommand?.CanExecute(-5) == true)
                {
                    vm.NavigateSelectionCommand.Execute(-5);
                    e.Handled = true;
                }
                else if (e.Key == Key.PageDown && vm.NavigateSelectionCommand?.CanExecute(5) == true)
                {
                    vm.NavigateSelectionCommand.Execute(5);
                    e.Handled = true;
                }
                else if (e.Key == Key.Up && vm.NavigateSelectionCommand?.CanExecute(-1) == true)
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

            _viewModel = DataContext as MainViewModel;
            UpdatePaneToggleButtons();
            await Dispatcher.Yield(DispatcherPriority.Render);
            _viewModel?.StartInitialRefresh();
            _ = InitializeStartupInfrastructureAsync();
        }

        private async Task InitializeStartupInfrastructureAsync()
        {
            if (_startupInitializationStarted || _viewModel == null)
            {
                return;
            }

            _startupInitializationStarted = true;

            try
            {
                var env = await _viewModel.GetSharedWebViewEnvironmentAsync();
                await Dispatcher.Yield(DispatcherPriority.Background);

                await linkWebView.EnsureCoreWebView2Async(env);
                await Dispatcher.Yield(DispatcherPriority.Background);

                await articleWebView.EnsureCoreWebView2Async(env);
                _viewModel.SetWebView(linkWebView);
                _viewModel.SetArticleWebView(articleWebView);
                AttachWebViewShortcutHandlers();
                _viewModel.ClearBrowserOnStartup();
                _ = PrimeEmbeddedBrowserAfterStartupAsync();
            }
            catch
            {
                // Keep startup resilient even if WebView initialization is slow or fails.
            }
        }

        private async Task PrimeEmbeddedBrowserAfterStartupAsync()
        {
            if (_viewModel == null)
            {
                return;
            }

            try
            {
                await Task.Delay(750);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                await _viewModel.PrimeEmbeddedBrowserAsync();
            }
            catch
            {
                // Ignore warmup failures; first real navigation will still work.
            }
        }

        private void AttachWebViewShortcutHandlers()
        {
            if (_webViewShortcutHandlersAttached)
            {
                return;
            }

            if (linkWebView == null || articleWebView == null)
            {
                return;
            }

            linkWebView.PreviewKeyDown += WebView_PreviewKeyDown;
            articleWebView.PreviewKeyDown += WebView_PreviewKeyDown;
            _webViewShortcutHandlersAttached = true;
        }

        private void WebView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && e.Key == Key.A)
            {
                if (_viewModel?.OpenArchiveWindowCommand?.CanExecute(null) == true)
                {
                    _viewModel.OpenArchiveWindowCommand.Execute(null);
                    e.Handled = true;
                }

                return;
            }

            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && e.Key == Key.P)
            {
                if (_viewModel?.OpenLibraryWindowCommand?.CanExecute(null) == true)
                {
                    _viewModel.OpenLibraryWindowCommand.Execute(null);
                    e.Handled = true;
                }

                return;
            }

            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && e.Key == Key.L)
            {
                if (_viewModel?.OpenReadLaterWindowCommand?.CanExecute(null) == true)
                {
                    _viewModel.OpenReadLaterWindowCommand.Execute(null);
                    e.Handled = true;
                }

                return;
            }

            if (e.Key == Key.F11)
            {
                if (_viewModel?.ToggleContentFullscreenCommand?.CanExecute(null) == true)
                {
                    _viewModel.ToggleContentFullscreenCommand.Execute(null);
                    e.Handled = true;
                }

                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.PageUp)
            {
                if (_viewModel?.NavigateFeedCommand?.CanExecute(-1) == true)
                {
                    _viewModel.NavigateFeedCommand.Execute(-1);
                    e.Handled = true;
                }

                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.PageDown)
            {
                if (_viewModel?.NavigateFeedCommand?.CanExecute(1) == true)
                {
                    _viewModel.NavigateFeedCommand.Execute(1);
                    e.Handled = true;
                }

                return;
            }

            if (e.Key == Key.PageUp)
            {
                if (_viewModel?.NavigateSelectionCommand?.CanExecute(-5) == true)
                {
                    _viewModel.NavigateSelectionCommand.Execute(-5);
                    e.Handled = true;
                }

                return;
            }

            if (e.Key == Key.PageDown)
            {
                if (_viewModel?.NavigateSelectionCommand?.CanExecute(5) == true)
                {
                    _viewModel.NavigateSelectionCommand.Execute(5);
                    e.Handled = true;
                }

                return;
            }

            if (e.Key == Key.Escape && _viewModel?.IsContentFullscreen == true)
            {
                _viewModel.ExitContentFullscreen();
                e.Handled = true;
            }
        }

        private static bool IsDescendantOf(DependencyObject source, DependencyObject target)
        {
            var current = source;
            while (current != null)
            {
                if (ReferenceEquals(current, target))
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
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

        private void ShowAllSidebarButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || DataContext is not MainViewModel viewModel)
            {
                return;
            }

            var menu = BuildShowAllFeedsMenu(viewModel);
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Right;
            menu.IsOpen = true;
        }

        private void LibrarySidebarButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel viewModel)
            {
                return;
            }

            if (viewModel.OpenLibraryWindowCommand?.CanExecute(null) == true)
            {
                viewModel.OpenLibraryWindowCommand.Execute(null);
            }
        }

        private ContextMenu BuildShowAllFeedsMenu(MainViewModel viewModel)
        {
            var menu = new ContextMenu();
            if (TryFindResource("ArticleContextMenuStyle") is Style contextMenuStyle)
            {
                menu.Style = contextMenuStyle;
            }

            var menuItemStyle = TryFindResource("ArticleMenuItemStyle") as Style;
            var feeds = viewModel.GetEnabledFeedsForShowAll();
            if (feeds.Count == 0)
            {
                menu.Items.Add(new MenuItem
                {
                    Header = "No feeds available",
                    IsEnabled = false,
                    Style = menuItemStyle
                });
                return menu;
            }

            var groupedFeeds = feeds
                .GroupBy(feed => string.IsNullOrWhiteSpace(feed.Category) ? "Default" : feed.Category)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (int groupIndex = 0; groupIndex < groupedFeeds.Count; groupIndex++)
            {
                var group = groupedFeeds[groupIndex];
                foreach (var feed in group.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var feedItem = new MenuItem
                    {
                        Header = $"{group.Key}  •  {feed.Name}",
                        Tag = feed,
                        Style = menuItemStyle,
                        Icon = new PackIcon
                        {
                            Kind = PackIconKind.Rss,
                            Width = 16,
                            Height = 16
                        }
                    };
                    feedItem.Click += ShowAllFeedMenuItem_Click;
                    menu.Items.Add(feedItem);
                }

                if (groupIndex < groupedFeeds.Count - 1)
                {
                    menu.Items.Add(new Separator());
                }
            }

            return menu;
        }

        private void ShowAllFeedMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem ||
                menuItem.Tag is not Feed feed ||
                DataContext is not MainViewModel viewModel)
            {
                return;
            }

            if (viewModel.OpenFeedAllWindowForFeedCommand?.CanExecute(feed) == true)
            {
                viewModel.OpenFeedAllWindowForFeedCommand.Execute(feed);
            }
        }

        private void SectionTile_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not ArticleSectionViewModel section ||
                !section.OpensInWindow)
            {
                return;
            }

            if (DataContext is MainViewModel viewModel &&
                viewModel.OpenArchiveWindowCommand?.CanExecute(null) == true)
            {
                viewModel.OpenArchiveWindowCommand.Execute(null);
                e.Handled = true;
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

        private void ToggleFeedsPaneButton_Click(object sender, RoutedEventArgs e)
        {
            _isFeedExplorerCollapsed = !_isFeedExplorerCollapsed;
            ApplyPaneCollapseState();
        }

        private void ToggleArticlesPaneButton_Click(object sender, RoutedEventArgs e)
        {
            _isArticleListCollapsed = !_isArticleListCollapsed;
            ApplyPaneCollapseState();
        }

        private void ApplyPaneCollapseState()
        {
            const double collapsedPaneWidth = 60;

            if (_contentFullscreenApplied)
            {
                UpdatePaneToggleButtons();
                return;
            }

            if (_isFeedExplorerCollapsed)
            {
                if (ExplorerColumn.ActualWidth > collapsedPaneWidth + 4)
                {
                    _savedExpandedExplorerWidth = ExplorerColumn.ActualWidth;
                    _collapsedExplorerColumnWidth = new GridLength(_savedExpandedExplorerWidth);
                    _collapsedExplorerSplitterColumnWidth = ExplorerSplitterColumn.Width;
                    if (_viewModel != null && _savedExpandedExplorerWidth > 0)
                    {
                        _viewModel.PendingTreeWidth = _savedExpandedExplorerWidth;
                    }
                }

                FeedExplorerPane.Visibility = Visibility.Visible;
                FeedExplorerPane.Padding = new Thickness(2, 6, 2, 6);
                MainVerticalSplitter.Visibility = Visibility.Collapsed;
                if (FeedExplorerBodyScrollViewer != null)
                {
                    FeedExplorerBodyScrollViewer.Visibility = Visibility.Collapsed;
                }
                if (FeedExplorerHeaderContent != null)
                {
                    FeedExplorerHeaderContent.Visibility = Visibility.Collapsed;
                }
                ExplorerColumn.MinWidth = 0;
                ExplorerColumn.SetCurrentValue(ColumnDefinition.WidthProperty, new GridLength(collapsedPaneWidth));
                ExplorerSplitterColumn.SetCurrentValue(ColumnDefinition.WidthProperty, new GridLength(0));
            }
            else
            {
                FeedExplorerPane.Visibility = Visibility.Visible;
                FeedExplorerPane.Padding = new Thickness(6);
                MainVerticalSplitter.Visibility = Visibility.Visible;
                if (FeedExplorerBodyScrollViewer != null)
                {
                    FeedExplorerBodyScrollViewer.Visibility = Visibility.Visible;
                }
                if (FeedExplorerHeaderContent != null)
                {
                    FeedExplorerHeaderContent.Visibility = Visibility.Visible;
                }
                ExplorerColumn.MinWidth = _savedExplorerColumnMinWidth;
                ExplorerColumn.SetCurrentValue(
                    ColumnDefinition.WidthProperty,
                    _savedExpandedExplorerWidth > 0
                        ? new GridLength(_savedExpandedExplorerWidth)
                        : (_collapsedExplorerColumnWidth.Value > 0
                            ? _collapsedExplorerColumnWidth
                            : new GridLength(Math.Max(_viewModel?.PendingTreeWidth ?? 280, 260))));
                if (_viewModel != null && _savedExpandedExplorerWidth > 0)
                {
                    _viewModel.PendingTreeWidth = _savedExpandedExplorerWidth;
                }
                ExplorerSplitterColumn.SetCurrentValue(
                    ColumnDefinition.WidthProperty,
                    _collapsedExplorerSplitterColumnWidth.Value > 0
                        ? _collapsedExplorerSplitterColumnWidth
                        : new GridLength(5));
            }

            if (_isArticleListCollapsed)
            {
                if (ArticleListColumn.ActualWidth > collapsedPaneWidth + 4)
                {
                    _savedExpandedArticleListWidth = ArticleListColumn.ActualWidth;
                    _collapsedArticleListColumnWidth = new GridLength(_savedExpandedArticleListWidth);
                    _collapsedArticleListSplitterColumnWidth = ArticleListSplitterColumn.Width;
                    _savedArticleListColumnMinWidth = ArticleListColumn.MinWidth;
                }

                ArticleListPane.Visibility = Visibility.Visible;
                ArticleListPane.Padding = new Thickness(2, 6, 2, 6);
                ArticleListVerticalSplitter.Visibility = Visibility.Collapsed;
                MainArticleList.Visibility = Visibility.Collapsed;
                if (ArticleListHeaderContent != null)
                {
                    ArticleListHeaderContent.Visibility = Visibility.Collapsed;
                }
                if (ArticleListHeaderActions != null)
                {
                    ArticleListHeaderActions.Visibility = Visibility.Collapsed;
                }
                ArticleListColumn.MinWidth = 0;
                ArticleListColumn.SetCurrentValue(ColumnDefinition.WidthProperty, new GridLength(collapsedPaneWidth));
                ArticleListSplitterColumn.SetCurrentValue(ColumnDefinition.WidthProperty, new GridLength(0));
            }
            else
            {
                ArticleListPane.Visibility = Visibility.Visible;
                ArticleListPane.Padding = new Thickness(6);
                ArticleListVerticalSplitter.Visibility = Visibility.Visible;
                MainArticleList.Visibility = Visibility.Visible;
                if (ArticleListHeaderContent != null)
                {
                    ArticleListHeaderContent.Visibility = Visibility.Visible;
                }
                if (ArticleListHeaderActions != null)
                {
                    ArticleListHeaderActions.Visibility = Visibility.Visible;
                }
                ArticleListColumn.MinWidth = _savedArticleListColumnMinWidth > 0 ? _savedArticleListColumnMinWidth : 300;
                ArticleListColumn.SetCurrentValue(
                    ColumnDefinition.WidthProperty,
                    _savedExpandedArticleListWidth > 0
                        ? new GridLength(_savedExpandedArticleListWidth)
                        : (_collapsedArticleListColumnWidth.Value > 0
                            ? _collapsedArticleListColumnWidth
                            : new GridLength(380)));
                ArticleListSplitterColumn.SetCurrentValue(
                    ColumnDefinition.WidthProperty,
                    _collapsedArticleListSplitterColumnWidth.Value > 0
                        ? _collapsedArticleListSplitterColumnWidth
                        : new GridLength(5));
            }

            UpdatePaneToggleButtons();
        }

        private void UpdatePaneToggleButtons()
        {
            if (ToggleFeedsPaneIcon != null)
            {
                ToggleFeedsPaneIcon.Kind = _isFeedExplorerCollapsed ? PackIconKind.ChevronRight : PackIconKind.ChevronLeft;
            }

            if (ToggleArticlesPaneIcon != null)
            {
                ToggleArticlesPaneIcon.Kind = _isArticleListCollapsed ? PackIconKind.ChevronRight : PackIconKind.ChevronLeft;
            }

            if (ToggleFeedsPaneButton != null)
            {
                ToggleFeedsPaneButton.ToolTip = _isFeedExplorerCollapsed ? "Show feeds" : "Hide feeds";
            }

            if (ToggleArticlesPaneButton != null)
            {
                ToggleArticlesPaneButton.ToolTip = _isArticleListCollapsed ? "Show articles" : "Hide articles";
            }
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
                if (_viewModel?.IsContentFullscreen == true)
                {
                    return;
                }

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
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            }

            if (_webViewShortcutHandlersAttached)
            {
                if (linkWebView != null)
                {
                    linkWebView.PreviewKeyDown -= WebView_PreviewKeyDown;
                }

                if (articleWebView != null)
                {
                    articleWebView.PreviewKeyDown -= WebView_PreviewKeyDown;
                }
            }

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

        private void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.IsContentFullscreen))
            {
                Dispatcher.BeginInvoke(new Action(() => ApplyContentFullscreenState(_viewModel?.IsContentFullscreen == true)));
                return;
            }

            if (e.PropertyName == nameof(MainViewModel.IsLoading) && _viewModel?.IsLoading == true)
            {
                Dispatcher.BeginInvoke(new Action(CloseOpenContextMenus), DispatcherPriority.Input);
            }
        }

        private void CloseOpenContextMenus()
        {
            try
            {
                CloseContextMenusRecursive(this);
            }
            catch
            {
                // Ignore context-menu close failures during refresh.
            }
        }

        private static void CloseContextMenusRecursive(DependencyObject root)
        {
            if (root == null)
            {
                return;
            }

            if (root is FrameworkElement element && element.ContextMenu != null && element.ContextMenu.IsOpen)
            {
                element.ContextMenu.IsOpen = false;
            }

            if (root is FrameworkContentElement contentElement &&
                contentElement.ContextMenu != null &&
                contentElement.ContextMenu.IsOpen)
            {
                contentElement.ContextMenu.IsOpen = false;
            }

            var childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                CloseContextMenusRecursive(VisualTreeHelper.GetChild(root, i));
            }
        }

        private void ApplyContentFullscreenState(bool isFullscreen)
        {
            if (isFullscreen == _contentFullscreenApplied)
            {
                return;
            }

            if (isFullscreen)
            {
                _savedToolbarRowHeight = MainToolbarRow.Height;
                _savedSectionsColumnWidth = SectionsColumn.Width;
                _savedSectionsSplitterColumnWidth = SectionsSplitterColumn.Width;
                _savedExplorerColumnWidth = ExplorerColumn.Width;
                _savedExplorerSplitterColumnWidth = ExplorerSplitterColumn.Width;
                _savedArticleListColumnWidth = ArticleListColumn.Width;
                _savedArticleListSplitterColumnWidth = ArticleListSplitterColumn.Width;
                _savedArticleContentRowHeight = ArticleContentRow.Height;
                _savedArticleSplitterRowHeight = ArticleBrowserSplitterRow.Height;
                _savedBrowserContentRowHeight = BrowserContentRow.Height;
                _savedExplorerColumnMinWidth = ExplorerColumn.MinWidth;
                _savedArticleContentRowMinHeight = ArticleContentRow.MinHeight;
                _savedArticleBrowserGridColumn = Grid.GetColumn(ArticleBrowserGrid);
                _savedArticleBrowserGridColumnSpan = Grid.GetColumnSpan(ArticleBrowserGrid);
                _savedWindowStyle = WindowStyle;
                _savedResizeMode = ResizeMode;
                _savedWindowState = WindowState;

                MainToolbarRow.Height = new GridLength(0);
                MainToolbar.Visibility = Visibility.Collapsed;
                SectionsPane.Visibility = Visibility.Collapsed;
                FeedExplorerPane.Visibility = Visibility.Collapsed;
                MainVerticalSplitter.Visibility = Visibility.Collapsed;
                ArticleListPane.Visibility = Visibility.Collapsed;
                ArticleListVerticalSplitter.Visibility = Visibility.Collapsed;
                ArticleContentPane.Visibility = Visibility.Collapsed;
                ArticleHorizontalSplitter.Visibility = Visibility.Collapsed;

                SectionsColumn.SetCurrentValue(ColumnDefinition.WidthProperty, new GridLength(0));
                SectionsSplitterColumn.SetCurrentValue(ColumnDefinition.WidthProperty, new GridLength(0));
                ExplorerColumn.SetCurrentValue(ColumnDefinition.WidthProperty, new GridLength(0));
                ExplorerSplitterColumn.SetCurrentValue(ColumnDefinition.WidthProperty, new GridLength(0));
                ArticleListColumn.SetCurrentValue(ColumnDefinition.WidthProperty, new GridLength(0));
                ArticleListSplitterColumn.SetCurrentValue(ColumnDefinition.WidthProperty, new GridLength(0));
                ArticleBrowserColumn.SetCurrentValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
                ExplorerColumn.MinWidth = 0;

                ArticleContentRow.Height = new GridLength(0);
                ArticleBrowserSplitterRow.Height = new GridLength(0);
                BrowserContentRow.Height = new GridLength(1, GridUnitType.Star);
                ArticleContentRow.MinHeight = 0;
                Grid.SetColumn(ArticleBrowserGrid, 0);
                Grid.SetColumnSpan(ArticleBrowserGrid, MainContentGrid.ColumnDefinitions.Count);
                Panel.SetZIndex(ArticleBrowserGrid, 100);

                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;
            }
            else
            {
                MainToolbarRow.Height = _savedToolbarRowHeight;
                MainToolbar.Visibility = Visibility.Visible;
                SectionsPane.Visibility = Visibility.Visible;
                FeedExplorerPane.Visibility = Visibility.Visible;
                MainVerticalSplitter.Visibility = Visibility.Visible;
                ArticleListPane.Visibility = Visibility.Visible;
                ArticleListVerticalSplitter.Visibility = Visibility.Visible;
                ArticleContentPane.Visibility = Visibility.Visible;
                ArticleHorizontalSplitter.Visibility = Visibility.Visible;

                SectionsColumn.SetCurrentValue(ColumnDefinition.WidthProperty, _savedSectionsColumnWidth);
                SectionsSplitterColumn.SetCurrentValue(ColumnDefinition.WidthProperty, _savedSectionsSplitterColumnWidth);
                ExplorerColumn.SetCurrentValue(ColumnDefinition.WidthProperty, _savedExplorerColumnWidth);
                ExplorerSplitterColumn.SetCurrentValue(ColumnDefinition.WidthProperty, _savedExplorerSplitterColumnWidth);
                ArticleListColumn.SetCurrentValue(ColumnDefinition.WidthProperty, _savedArticleListColumnWidth);
                ArticleListSplitterColumn.SetCurrentValue(ColumnDefinition.WidthProperty, _savedArticleListSplitterColumnWidth);
                ArticleBrowserColumn.SetCurrentValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
                ExplorerColumn.MinWidth = _savedExplorerColumnMinWidth;

                ArticleContentRow.Height = _savedArticleContentRowHeight;
                ArticleBrowserSplitterRow.Height = _savedArticleSplitterRowHeight;
                BrowserContentRow.Height = _savedBrowserContentRowHeight;
                ArticleContentRow.MinHeight = _savedArticleContentRowMinHeight;
                Grid.SetColumn(ArticleBrowserGrid, _savedArticleBrowserGridColumn);
                Grid.SetColumnSpan(ArticleBrowserGrid, Math.Max(1, _savedArticleBrowserGridColumnSpan));
                Panel.SetZIndex(ArticleBrowserGrid, 0);

                WindowStyle = _savedWindowStyle;
                ResizeMode = _savedResizeMode;
                WindowState = _savedWindowState;
                ApplyPaneCollapseState();
            }

            _contentFullscreenApplied = isFullscreen;
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