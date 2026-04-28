using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MaterialDesignThemes.Wpf;
using System.Windows.Interop;
using System.Windows.Threading;
using MyNewsFeeder.Models;
using MyNewsFeeder.Services;
using MyNewsFeeder.ViewModels;

namespace MyNewsFeeder.Views
{
    public partial class FeedAllWindow : Window
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

        private static readonly StringComparer FeedComparer = StringComparer.OrdinalIgnoreCase;

        private readonly MainViewModel _viewModel;
        private readonly ObservableCollection<Feed> _availableFeeds = new ObservableCollection<Feed>();
        private readonly ObservableCollection<FeedItem> _items = new ObservableCollection<FeedItem>();
        private readonly FeedService _feedService = new FeedService();
        private readonly ArticleReaderService _articleReaderService = new ArticleReaderService();
        private readonly BrowserSession _browserSession;
        private ICollectionView _itemsView;
        private bool _isLoading;
        private bool _isReaderModeActive;
        private bool _isReaderModeLoading;
        private bool _showOriginalContent;
        private bool _isVideoPlaybackEnabled;
        private bool _previewWebViewInitialized;
        private bool _isPreviewPageLoading;
        private bool _isPreviewFullscreen;
        private bool _hasShownLocalWarmupHint;
        private bool _isApplyingWindowPlacement;
        private bool _hasInitialWindowPlacementApplied;
        private bool _hasRevealedInitialFrame;
        private GridLength _savedArticlesColumnWidth;
        private GridLength _savedSplitterColumnWidth;
        private WindowState _savedWindowState;
        private WindowState _lastNonMinimizedWindowState = WindowState.Normal;
        private string _currentListStatusText = string.Empty;
        private FeedItem _selectedItem;
        private ReaderArticleContent _readerContent;
        private Task<bool> _previewWebViewInitializationTask;
        private Feed _feed;
        private bool _isUpdatingFeedSelection;

        public FeedAllWindow(MainViewModel viewModel, Feed feed)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _feed = feed ?? throw new ArgumentNullException(nameof(feed));
            _browserSession = _viewModel.CreateBrowserSession();

            InitializeComponent();
            DataContext = _viewModel;

            _itemsView = CollectionViewSource.GetDefaultView(_items);
            _itemsView.Filter = ItemMatchesCurrentFilter;
            ArticlesListView.ItemsSource = _itemsView;
            ReadStateComboBox.ItemsSource = new[] { "All", "Unread", "Read" };
            ReadStateComboBox.SelectedIndex = 0;
            FeedSelectorComboBox.ItemsSource = _availableFeeds;

            SourceInitialized += (_, __) => EnableDarkTitleBar();
            Loaded += FeedAllWindow_Loaded;
            ContentRendered += FeedAllWindow_ContentRendered;
            Closed += FeedAllWindow_Closed;
            LocationChanged += FeedAllWindow_PlacementChanged;
            SizeChanged += FeedAllWindow_PlacementChanged;
            StateChanged += FeedAllWindow_StateChanged;
            PreviewKeyDown += FeedAllWindow_PreviewKeyDown;
            PreviewWebView.PreviewKeyDown += PreviewWebView_PreviewKeyDown;
            PopulateFeedSelector();
            UpdateFeedHeader();
            UpdatePreviewState();
            UpdateButtons();
        }

        private void EnableDarkTitleBar()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int useDark = 1;
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, Marshal.SizeOf<int>()) != 0)
            {
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref useDark, Marshal.SizeOf<int>());
            }
        }

        private void FeedAllWindow_Loaded(object sender, RoutedEventArgs e)
        {
            PopulateFeedSelector();
            Dispatcher.BeginInvoke(new Action(() => _ = LoadFeedAsync()), DispatcherPriority.Background);
        }

        private void FeedAllWindow_ContentRendered(object sender, EventArgs e)
        {
            if (_hasRevealedInitialFrame)
            {
                return;
            }

            _hasRevealedInitialFrame = true;

            var fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(90),
                FillBehavior = FillBehavior.Stop
            };

            fadeIn.Completed += (_, __) => Opacity = 1;
            BeginAnimation(OpacityProperty, fadeIn, HandoffBehavior.SnapshotAndReplace);
        }

        private void FeedAllWindow_Closed(object sender, EventArgs e)
        {
            SaveWindowPreferences();

            try
            {
                _browserSession?.Dispose();
            }
            catch
            {
            }

            try
            {
                _feedService.Dispose();
            }
            catch
            {
            }

            try
            {
                _articleReaderService.Dispose();
            }
            catch
            {
            }
        }

        public void PrepareInitialWindowPlacement(FeedAllWindowPreferences preferences)
        {
            preferences ??= new FeedAllWindowPreferences();
            if (!IsLoaded)
            {
                Opacity = 0;
            }

            ApplyWindowPlacement(preferences);
            _hasInitialWindowPlacementApplied = true;
        }

        private void FeedAllWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            HandleFullscreenShortcut(e);
        }

        private void PreviewWebView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            HandleFullscreenShortcut(e);
        }

        private void HandleFullscreenShortcut(KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.PageUp)
            {
                SwitchFeedSelectionByDelta(-1);
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.PageDown)
            {
                SwitchFeedSelectionByDelta(1);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F11)
            {
                TogglePreviewFullscreen();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape && _isPreviewFullscreen)
            {
                SetPreviewFullscreen(false);
                e.Handled = true;
            }
        }

        private void SwitchFeedSelectionByDelta(int delta)
        {
            if (delta == 0 || _isLoading || _availableFeeds.Count <= 1)
            {
                return;
            }

            var currentIndex = _availableFeeds
                .Select((feed, index) => new { feed, index })
                .FirstOrDefault(x => FeedMatchesCurrentFeed(x.feed))
                ?.index ?? -1;

            if (currentIndex < 0)
            {
                currentIndex = 0;
            }

            var targetIndex = currentIndex + delta;
            if (targetIndex < 0)
            {
                targetIndex = 0;
            }
            else if (targetIndex >= _availableFeeds.Count)
            {
                targetIndex = _availableFeeds.Count - 1;
            }

            if (targetIndex == currentIndex)
            {
                return;
            }

            var targetFeed = _availableFeeds[targetIndex];
            if (targetFeed == null)
            {
                return;
            }

            FeedSelectorComboBox.SelectedItem = targetFeed;
        }

        private void FeedAllWindow_PlacementChanged(object sender, EventArgs e)
        {
            if (!IsLoaded || _isApplyingWindowPlacement || _isPreviewFullscreen)
            {
                return;
            }

            SaveWindowPreferences();
        }

        private void FeedAllWindow_StateChanged(object sender, EventArgs e)
        {
            if (WindowState != WindowState.Minimized)
            {
                _lastNonMinimizedWindowState = WindowState;
            }

            FeedAllWindow_PlacementChanged(sender, e);
        }

        private void ApplyWindowPlacement(FeedAllWindowPreferences preferences)
        {
            preferences ??= new FeedAllWindowPreferences();
            _isApplyingWindowPlacement = true;

            try
            {
                if (preferences.WindowWidth.HasValue && preferences.WindowWidth.Value > 0)
                {
                    Width = Math.Max(MinWidth, preferences.WindowWidth.Value);
                }

                if (preferences.WindowHeight.HasValue && preferences.WindowHeight.Value > 0)
                {
                    Height = Math.Max(MinHeight, preferences.WindowHeight.Value);
                }

                if (preferences.WindowLeft.HasValue)
                {
                    Left = preferences.WindowLeft.Value;
                }

                if (preferences.WindowTop.HasValue)
                {
                    Top = preferences.WindowTop.Value;
                }

                var targetState = NormalizePreferredWindowState(preferences.WindowState);
                WindowState = targetState;
                if (targetState != WindowState.Minimized)
                {
                    _lastNonMinimizedWindowState = targetState;
                }
            }
            finally
            {
                _isApplyingWindowPlacement = false;
            }
        }

        private FeedAllWindowPreferences CaptureCurrentWindowPreferences()
        {
            var effectiveState = _isPreviewFullscreen
                ? (_savedWindowState == WindowState.Minimized ? WindowState.Normal : _savedWindowState)
                : (WindowState == WindowState.Minimized ? _lastNonMinimizedWindowState : WindowState);

            var placementBounds = (_isPreviewFullscreen || effectiveState != WindowState.Normal)
                ? RestoreBounds
                : new Rect(Left, Top, Width, Height);

            return new FeedAllWindowPreferences
            {
                WindowState = effectiveState == WindowState.Maximized ? "maximized" : "normal",
                WindowWidth = placementBounds.Width > 0 ? placementBounds.Width : null,
                WindowHeight = placementBounds.Height > 0 ? placementBounds.Height : null,
                WindowLeft = placementBounds.Width > 0 ? placementBounds.Left : null,
                WindowTop = placementBounds.Height > 0 ? placementBounds.Top : null
            };
        }

        private void SaveWindowPreferences()
        {
            if (_viewModel == null || _isApplyingWindowPlacement || !_hasInitialWindowPlacementApplied)
            {
                return;
            }

            _viewModel.SaveFeedAllWindowPreferences(CaptureCurrentWindowPreferences());
        }

        private static WindowState NormalizePreferredWindowState(string value)
        {
            return string.Equals(value, "maximized", StringComparison.OrdinalIgnoreCase)
                ? WindowState.Maximized
                : WindowState.Normal;
        }

        private async Task LoadFeedAsync()
        {
            if (_isLoading)
            {
                return;
            }

            _isLoading = true;
            FeedSelectorComboBox.IsEnabled = false;
            SetLoadedCountText("Loading items...");
            LoadingProgressBar.Visibility = Visibility.Visible;
            UpdateButtons();

            try
            {
                var advertisementKeywords = _viewModel.AdvertisementFilterEnabled
                    ? _viewModel.GetAdvertisementKeywordsSnapshot()
                    : new System.Collections.Generic.List<string>();

                var items = await _feedService.FetchArticlesForFeedAsync(
                    _feed,
                    keywordFilter: null,
                    maxItems: 500,
                    advertisementKeywords: advertisementKeywords.Count > 0 ? advertisementKeywords : null);

                var orderedItems = items
                    .Where(item => item != null)
                    .OrderByDescending(item => item.PublicationDate)
                    .ToList();

                _items.Clear();
                foreach (var item in orderedItems)
                {
                    _viewModel.ApplyStoredStateToExternalViewItem(item);
                    item.Description = SanitizeFeedSummary(item.Description, 180);
                    _items.Add(item);
                }

                SetLoadedCountText($"{_items.Count} item{(_items.Count == 1 ? string.Empty : "s")} loaded");
                _itemsView.Refresh();
                if (_itemsView.Cast<FeedItem>().FirstOrDefault() is FeedItem first)
                {
                    ArticlesListView.SelectedItem = first;
                }
                else
                {
                    UpdatePreviewState();
                }
            }
            catch (Exception ex)
            {
                SetLoadedCountText("Could not load this feed.");
                MessageBox.Show(
                    this,
                    $"Could not load all articles for this feed.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                    "Show All",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                _isLoading = false;
                LoadingProgressBar.Visibility = Visibility.Collapsed;
                FeedSelectorComboBox.IsEnabled = _availableFeeds.Count > 1;
                UpdateButtons();
            }
        }

        private void PopulateFeedSelector()
        {
            var feeds = _viewModel.GetEnabledFeedsForShowAll();
            var selectedFeed = feeds.FirstOrDefault(FeedMatchesCurrentFeed);

            _isUpdatingFeedSelection = true;
            _availableFeeds.Clear();
            foreach (var feed in feeds)
            {
                _availableFeeds.Add(feed);
            }

            if (selectedFeed == null && _feed != null && !string.IsNullOrWhiteSpace(_feed.Url))
            {
                selectedFeed = CloneFeed(_feed);
                _availableFeeds.Insert(0, selectedFeed);
            }

            FeedSelectorComboBox.SelectedItem = selectedFeed;
            FeedSelectorComboBox.IsEnabled = !_isLoading && _availableFeeds.Count > 1;
            _isUpdatingFeedSelection = false;
        }

        private bool FeedMatchesCurrentFeed(Feed feed)
        {
            if (feed == null || _feed == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(feed.Url) && !string.IsNullOrWhiteSpace(_feed.Url))
            {
                return FeedComparer.Equals(feed.Url.Trim(), _feed.Url.Trim());
            }

            return FeedComparer.Equals(feed.Name?.Trim(), _feed.Name?.Trim());
        }

        private static Feed CloneFeed(Feed feed)
        {
            if (feed == null)
            {
                return null;
            }

            return new Feed
            {
                Name = feed.Name,
                Url = feed.Url,
                Category = feed.Category,
                IsEnabled = feed.IsEnabled,
                IsImportant = feed.IsImportant
            };
        }

        private void UpdateFeedHeader()
        {
            var feedName = string.IsNullOrWhiteSpace(_feed?.Name) ? "Feed" : _feed.Name.Trim();
            Title = $"{feedName} - Show All";
            FeedTitleTextBlock.Text = feedName;
            FeedSelectorComboBox.ToolTip = string.IsNullOrWhiteSpace(_feed?.Category)
                ? feedName
                : $"{feedName} ({_feed.Category})";
        }

        private async void FeedSelectorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingFeedSelection || !IsLoaded || FeedSelectorComboBox.SelectedItem is not Feed selectedFeed)
            {
                return;
            }

            if (FeedMatchesCurrentFeed(selectedFeed))
            {
                return;
            }

            _feed = CloneFeed(selectedFeed);
            UpdateFeedHeader();

            _selectedItem = null;
            _readerContent = null;
            _isReaderModeActive = false;
            _isReaderModeLoading = false;
            _showOriginalContent = false;
            ResetVideoPlaybackState();
            _items.Clear();
            ArticlesListView.SelectedItem = null;

            if (_previewWebViewInitialized)
            {
                _browserSession.NavigateToPlaceholder();
            }

            SetLoadedCountText("Loading items...");
            UpdatePreviewState();
            UpdateButtons();
            await LoadFeedAsync();
        }

        private bool ItemMatchesCurrentFilter(object obj)
        {
            if (obj is not FeedItem item)
            {
                return false;
            }

            var search = SearchTextBox?.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(search))
            {
                if ((item.Title?.IndexOf(search, StringComparison.OrdinalIgnoreCase) ?? -1) < 0 &&
                    (item.Description?.IndexOf(search, StringComparison.OrdinalIgnoreCase) ?? -1) < 0 &&
                    (item.Link?.IndexOf(search, StringComparison.OrdinalIgnoreCase) ?? -1) < 0)
                {
                    return false;
                }
            }

            var readState = ReadStateComboBox?.SelectedItem as string ?? "All";
            if (string.Equals(readState, "Unread", StringComparison.OrdinalIgnoreCase) && item.IsRead)
            {
                return false;
            }

            if (string.Equals(readState, "Read", StringComparison.OrdinalIgnoreCase) && !item.IsRead)
            {
                return false;
            }

            return true;
        }

        private void FilterControl_Changed(object sender, EventArgs e)
        {
            _itemsView?.Refresh();
            SetLoadedCountText($"{_itemsView?.Cast<FeedItem>().Count() ?? 0} matching item{((_itemsView?.Cast<FeedItem>().Count() ?? 0) == 1 ? string.Empty : "s")}");
        }

        private void ArticlesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedItem = ArticlesListView.SelectedItem as FeedItem;
            _readerContent = null;
            _isReaderModeActive = false;
            _showOriginalContent = false;
            ResetVideoPlaybackState();

            if (_previewWebViewInitialized)
            {
                _browserSession.NavigateToPlaceholder();
            }

            if (_selectedItem != null)
            {
                _viewModel.MarkArticleAsReadFromExternalView(_selectedItem);
                if (string.Equals(ReadStateComboBox.SelectedItem as string, "Unread", StringComparison.OrdinalIgnoreCase))
                {
                    _itemsView.Refresh();
                }
            }

            ShowSummaryPreview();
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            var hasSelection = _selectedItem != null;
            var hasLink = hasSelection && !string.IsNullOrWhiteSpace(_selectedItem?.Link);
            var hasContentPreview = hasSelection && (_showOriginalContent || _isReaderModeActive);
            var canEnableVideo = hasLink && _showOriginalContent && !_isReaderModeActive && !_isLoading;
            ShowContentButton.IsEnabled = hasSelection && !_isLoading;
            ReaderModeButton.IsEnabled = hasLink && !_isLoading && !_isReaderModeLoading;
            OpenArticleButton.IsEnabled = hasSelection && !_isLoading;
            RefreshPageButton.IsEnabled = hasLink && !_isLoading;
            FullscreenButton.IsEnabled = hasContentPreview && !_isLoading;
            FullscreenButton.Visibility = hasContentPreview && !_isPreviewFullscreen ? Visibility.Visible : Visibility.Collapsed;
            FullscreenButton.Content = _isPreviewFullscreen ? "Exit Fullscreen" : "Fullscreen";
            EnableVideoButton.Visibility = canEnableVideo ? Visibility.Visible : Visibility.Collapsed;
            EnableVideoButton.IsEnabled = canEnableVideo && !_isVideoPlaybackEnabled;
            EnableVideoButton.Content = _isVideoPlaybackEnabled ? "Media Allowed" : "Allow Media";

            if (PreviewFullscreenOverlayButton != null)
            {
                PreviewFullscreenOverlayButton.IsEnabled = true;
            }

            if (PreviewFullscreenToolbar != null)
            {
                PreviewFullscreenToolbar.Visibility = _isPreviewFullscreen && hasContentPreview ? Visibility.Visible : Visibility.Collapsed;
            }

            if (PreviewFullscreenRefreshButton != null)
            {
                PreviewFullscreenRefreshButton.IsEnabled = hasLink && !_isLoading;
            }

            if (PreviewFullscreenEnableVideoButton != null)
            {
                PreviewFullscreenEnableVideoButton.Visibility = canEnableVideo ? Visibility.Visible : Visibility.Collapsed;
                PreviewFullscreenEnableVideoButton.IsEnabled = canEnableVideo && !_isVideoPlaybackEnabled;
                PreviewFullscreenEnableVideoButton.Content = _isVideoPlaybackEnabled ? "Media Allowed" : "Allow Media";
            }

            if (PreviewFullscreenVideoSeparator != null)
            {
                PreviewFullscreenVideoSeparator.Visibility = canEnableVideo ? Visibility.Visible : Visibility.Collapsed;
            }

            if (PreviewFullscreenLinkTextBlock != null)
            {
                PreviewFullscreenLinkTextBlock.Text = hasLink ? _selectedItem.Link : string.Empty;
            }

            if (_isReaderModeLoading)
            {
                ReaderModeButton.Content = "Loading Reader...";
            }
            else
            {
                ReaderModeButton.Content = _isReaderModeActive ? "Feed Summary" : "Reader Mode";
            }
        }

        private void UpdatePreviewState()
        {
            var hasSelection = _selectedItem != null;
            PreviewTitleTextBlock.Text = hasSelection ? _selectedItem.Title : "Select an article";
            PreviewMetaTextBlock.Text = hasSelection
                ? $"{_selectedItem.PublicationDate:yyyy-MM-dd HH:mm}  •  {_feed.Name}"
                : "The selected article will appear here.";

            if (!hasSelection)
            {
                PreviewEmptyStateGrid.Visibility = Visibility.Visible;
                PreviewBrowserContainer.Visibility = Visibility.Collapsed;
                PreviewEmptyIcon.Kind = PackIconKind.Rss;
                PreviewEmptyTitleTextBlock.Text = "Select an article";
                PreviewEmptyBodyTextBlock.Text = "Use Show Content or Reader Mode to inspect the selected feed item.";
                return;
            }

            if (_showOriginalContent || _isReaderModeActive)
            {
                PreviewEmptyStateGrid.Visibility = Visibility.Collapsed;
                PreviewBrowserContainer.Visibility = Visibility.Visible;
                return;
            }

            PreviewEmptyStateGrid.Visibility = Visibility.Visible;
            PreviewBrowserContainer.Visibility = Visibility.Collapsed;
            PreviewEmptyIcon.Kind = PackIconKind.TextBoxSearchOutline;
            PreviewEmptyTitleTextBlock.Text = "Preview";
            var summary = SanitizeFeedSummary(_selectedItem.Description, 320);
            PreviewEmptyBodyTextBlock.Text = string.IsNullOrWhiteSpace(summary)
                ? "Use Show Content or Reader Mode to inspect this article."
                : summary;
        }

        private void ShowSummaryPreview()
        {
            ResetVideoPlaybackState();
            UpdatePreviewState();
        }

        private async Task RefreshCurrentPageAsync()
        {
            if (_selectedItem == null || string.IsNullOrWhiteSpace(_selectedItem.Link))
            {
                return;
            }

            if (_isReaderModeActive)
            {
                _readerContent = null;
                await LoadReaderModeAsync(forceReaderRefresh: true);
                return;
            }

            await ShowOriginalContentAsync();
        }

        private async void ReaderModeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null || _isReaderModeLoading)
            {
                return;
            }

            if (_isReaderModeActive)
            {
                _isReaderModeActive = false;
                _showOriginalContent = false;
                ResetVideoPlaybackState();
                ShowSummaryPreview();
                UpdateButtons();
                return;
            }

            if (string.IsNullOrWhiteSpace(_selectedItem.Link))
            {
                return;
            }

            await LoadReaderModeAsync(forceReaderRefresh: false);
        }

        private async void EnableVideoButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null || string.IsNullOrWhiteSpace(_selectedItem.Link) || _isVideoPlaybackEnabled)
            {
                return;
            }

            if (!await EnsurePreviewWebViewAsync())
            {
                return;
            }

            _isVideoPlaybackEnabled = true;
            _browserSession.SetMediaPlaybackEnabled(true);
            await ShowOriginalContentAsync();
        }

        private async Task LoadReaderModeAsync(bool forceReaderRefresh)
        {
            _isReaderModeLoading = true;
            UpdateButtons();

            try
            {
                if (!await EnsurePreviewWebViewAsync())
                {
                    MessageBox.Show(
                        this,
                        "The embedded preview browser could not be initialized.",
                        "Reader Mode",
                        MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                    return;
                }

                _browserSession.SetDarkMode(_viewModel.DarkMode);
                _browserSession.SetAdBlockerEnabled(_viewModel.AdBlockerEnabled);
                ResetVideoPlaybackState();
                if (forceReaderRefresh)
                {
                    _readerContent = null;
                }

                _readerContent ??= await _articleReaderService.ExtractAsync(_selectedItem.Link);
                if (_readerContent == null || string.IsNullOrWhiteSpace(_readerContent.HtmlContent))
                {
                    MessageBox.Show(
                        this,
                        "Reader Mode could not extract full text for this article.",
                        "Reader Mode",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                PreviewWebView.NavigateToString(BuildReaderModeHtml(_readerContent));
                _isReaderModeActive = true;
                _showOriginalContent = false;
                UpdatePreviewState();
            }
            catch
            {
                MessageBox.Show(
                    this,
                    "Reader Mode failed to load article content.",
                    "Reader Mode",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                _isReaderModeLoading = false;
                UpdateButtons();
            }
        }

        private void ShowContentButton_Click(object sender, RoutedEventArgs e)
        {
            _ = ShowOriginalContentAsync();
        }

        private void RefreshPageButton_Click(object sender, RoutedEventArgs e)
        {
            _ = RefreshCurrentPageAsync();
        }

        private void FullscreenButton_Click(object sender, RoutedEventArgs e)
        {
            TogglePreviewFullscreen();
        }

        private void PreviewFullscreenRefreshButton_Click(object sender, RoutedEventArgs e)
        {
            _ = RefreshCurrentPageAsync();
        }

        private async Task ShowOriginalContentAsync()
        {
            if (_selectedItem == null || string.IsNullOrWhiteSpace(_selectedItem.Link))
            {
                return;
            }

            if (!await EnsurePreviewWebViewAsync())
            {
                _viewModel.PromptOpenExternalLink(_selectedItem.Link);
                return;
            }

            _browserSession.SetDarkMode(_viewModel.DarkMode);
            _browserSession.SetAdBlockerEnabled(_viewModel.AdBlockerEnabled);
            _browserSession.SetMediaPlaybackEnabled(_isVideoPlaybackEnabled);
            SetPreviewPageLoading(true);
            _showOriginalContent = true;
            _isReaderModeActive = false;
            UpdatePreviewState();
            if (!_hasShownLocalWarmupHint)
            {
                _hasShownLocalWarmupHint = true;
                _ = ShowWarmupHintAsync();
            }
            _viewModel.NotifyEmbeddedBrowserWarmupHintOnce();
            await Dispatcher.Yield(DispatcherPriority.Render);
            var navigated = await _browserSession.NavigateFastAsync(_selectedItem.Link);
            if (!navigated)
            {
                SetPreviewPageLoading(false);
                _viewModel.PromptOpenExternalLink(_selectedItem.Link);
            }
            UpdateButtons();
        }

        private void ResetVideoPlaybackState()
        {
            if (!_isVideoPlaybackEnabled && !_browserSession.IsMediaPlaybackEnabled)
            {
                return;
            }

            _isVideoPlaybackEnabled = false;
            _browserSession.SetMediaPlaybackEnabled(false);
        }

        private void SetLoadedCountText(string text)
        {
            _currentListStatusText = text ?? string.Empty;
            LoadedCountTextBlock.Text = _currentListStatusText;
        }

        private async Task ShowWarmupHintAsync()
        {
            var restoreText = _currentListStatusText;
            LoadedCountTextBlock.Text = "The first Show Content load after app start can take longer. After that, pages usually open faster.";

            try
            {
                await Task.Delay(5000);
            }
            catch
            {
            }

            if (LoadedCountTextBlock != null &&
                string.Equals(LoadedCountTextBlock.Text, "The first Show Content load after app start can take longer. After that, pages usually open faster.", StringComparison.Ordinal))
            {
                LoadedCountTextBlock.Text = restoreText;
            }
        }

        private static string SanitizeFeedSummary(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var text = value;

            // Strip common HTML blocks that look especially bad in feed summaries.
            text = Regex.Replace(text, "<script\\b[^>]*>.*?</script>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, "<style\\b[^>]*>.*?</style>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, "<img\\b[^>]*>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, "<br\\s*/?>", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<[^>]+>", " ", RegexOptions.Singleline);
            text = WebUtility.HtmlDecode(text) ?? string.Empty;
            text = Regex.Replace(text, "\\s+", " ").Trim();

            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            if (maxLength > 0 && text.Length > maxLength)
            {
                text = text.Substring(0, maxLength).TrimEnd();
                text += "...";
            }

            return text;
        }

        private void OpenArticleButton_Click(object sender, RoutedEventArgs e)
        {
            OpenSelectedArticle();
        }

        private void OpenSelectedArticle()
        {
            if (_selectedItem == null)
            {
                return;
            }

            if (_viewModel.OpenArticleInWindowCommand?.CanExecute(_selectedItem) == true)
            {
                _viewModel.OpenArticleInWindowCommand.Execute(_selectedItem);
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            _ = LoadFeedAsync();
        }

        private void TogglePreviewFullscreen()
        {
            SetPreviewFullscreen(!_isPreviewFullscreen);
        }

        private void SetPreviewFullscreen(bool isFullscreen)
        {
            if (_isPreviewFullscreen == isFullscreen)
            {
                return;
            }

            if (isFullscreen && !(_showOriginalContent || _isReaderModeActive))
            {
                return;
            }

            if (isFullscreen)
            {
                _savedArticlesColumnWidth = ArticlesColumn.Width;
                _savedSplitterColumnWidth = SplitterColumn.Width;
                _savedWindowState = WindowState;

                HeaderPanel.Visibility = Visibility.Collapsed;
                FooterActionsPanel.Visibility = Visibility.Collapsed;
                ArticlesListPane.Visibility = Visibility.Collapsed;
                ContentSplitter.Visibility = Visibility.Collapsed;
                ArticlesColumn.Width = new GridLength(0);
                SplitterColumn.Width = new GridLength(0);
                Grid.SetColumn(PreviewPane, 0);
                Grid.SetColumnSpan(PreviewPane, 3);
                WindowState = WindowState.Maximized;
            }
            else
            {
                HeaderPanel.Visibility = Visibility.Visible;
                FooterActionsPanel.Visibility = Visibility.Visible;
                ArticlesListPane.Visibility = Visibility.Visible;
                ContentSplitter.Visibility = Visibility.Visible;
                ArticlesColumn.Width = _savedArticlesColumnWidth;
                SplitterColumn.Width = _savedSplitterColumnWidth;
                Grid.SetColumn(PreviewPane, 2);
                Grid.SetColumnSpan(PreviewPane, 1);
                WindowState = _savedWindowState;
            }

            _isPreviewFullscreen = isFullscreen;
            UpdateButtons();
        }

        private void ArticlesListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            OpenSelectedArticle();
        }

        private void ArticlesListView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var scrollViewer = FindScrollViewer(ArticlesListView);
            if (scrollViewer == null)
            {
                return;
            }

            const int lineStepsPerWheelTick = 3;
            for (int i = 0; i < lineStepsPerWheelTick; i++)
            {
                if (e.Delta > 0)
                {
                    scrollViewer.LineUp();
                }
                else
                {
                    scrollViewer.LineDown();
                }
            }

            e.Handled = true;
        }

        private void ContextOpenArticle_Click(object sender, RoutedEventArgs e)
        {
            if (GetContextItem(sender) is FeedItem item)
            {
                ArticlesListView.SelectedItem = item;
                OpenSelectedArticle();
            }
        }

        private void ContextShowContent_Click(object sender, RoutedEventArgs e)
        {
            if (GetContextItem(sender) is FeedItem item)
            {
                ArticlesListView.SelectedItem = item;
                _ = ShowOriginalContentAsync();
            }
        }

        private async void ContextReaderMode_Click(object sender, RoutedEventArgs e)
        {
            if (GetContextItem(sender) is FeedItem item)
            {
                ArticlesListView.SelectedItem = item;
                await Dispatcher.InvokeAsync(() => ReaderModeButton_Click(sender, e));
            }
        }

        private void ArticleContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is not ContextMenu contextMenu)
            {
                return;
            }

            var item = (contextMenu.PlacementTarget as FrameworkElement)?.DataContext as FeedItem;
            foreach (var menuItem in contextMenu.Items.OfType<MenuItem>())
            {
                menuItem.IsEnabled = item != null;
            }

            if (item == null)
            {
                return;
            }

            foreach (var menuItem in contextMenu.Items.OfType<MenuItem>())
            {
                switch (menuItem.Tag as string)
                {
                    case "pin":
                        menuItem.Header = item.IsPinned ? "Unpin" : "Pin";
                        menuItem.IsChecked = item.IsPinned;
                        break;
                    case "readlater":
                        menuItem.Header = item.IsReadLater ? "Unsave" : "Read Later";
                        menuItem.IsChecked = item.IsReadLater;
                        break;
                    case "archive":
                        menuItem.Header = item.IsArchived ? "Unarchive" : "Archive";
                        menuItem.IsChecked = item.IsArchived;
                        break;
                    case "unread":
                        menuItem.IsEnabled = !item.IsUnread;
                        break;
                }
            }
        }

        private static FeedItem GetContextItem(object sender)
        {
            if (sender is FrameworkElement element && element.DataContext is FeedItem directItem)
            {
                return directItem;
            }

            if (sender is MenuItem menuItem &&
                menuItem.Parent is ContextMenu contextMenu &&
                contextMenu.PlacementTarget is FrameworkElement placementTarget &&
                placementTarget.DataContext is FeedItem placementItem)
            {
                return placementItem;
            }

            return null;
        }

        private void ExecuteArticleCommand(ICommand command, FeedItem item)
        {
            if (command == null || item == null)
            {
                return;
            }

            ArticlesListView.SelectedItem = item;
            if (command.CanExecute(item))
            {
                command.Execute(item);
            }
        }

        private void ContextPin_Click(object sender, RoutedEventArgs e)
        {
            ExecuteArticleCommand(_viewModel.PinArticleCommand, GetContextItem(sender));
        }

        private void ContextReadLater_Click(object sender, RoutedEventArgs e)
        {
            ExecuteArticleCommand(_viewModel.ReadLaterArticleCommand, GetContextItem(sender));
        }

        private void ContextArchive_Click(object sender, RoutedEventArgs e)
        {
            ExecuteArticleCommand(_viewModel.ArchiveArticleCommand, GetContextItem(sender));
        }

        private void ContextMarkUnread_Click(object sender, RoutedEventArgs e)
        {
            ExecuteArticleCommand(_viewModel.MarkUnreadCommand, GetContextItem(sender));
        }

        private void ContextCopyLink_Click(object sender, RoutedEventArgs e)
        {
            ExecuteArticleCommand(_viewModel.CopyArticleLinkCommand, GetContextItem(sender));
        }

        private Task<bool> EnsurePreviewWebViewAsync()
        {
            if (_previewWebViewInitialized)
            {
                return Task.FromResult(true);
            }

            _previewWebViewInitializationTask ??= EnsurePreviewWebViewCoreAsync();
            return _previewWebViewInitializationTask;
        }

        private async Task<bool> EnsurePreviewWebViewCoreAsync()
        {
            try
            {
                _browserSession.SetDarkMode(_viewModel.DarkMode);
                _browserSession.SetAdBlockerEnabled(_viewModel.AdBlockerEnabled);
                _browserSession.SetWebView(PreviewWebView);

                if (!await _browserSession.EnsureInitializedAsync())
                {
                    _previewWebViewInitializationTask = null;
                    return false;
                }

                if (PreviewWebView.CoreWebView2 != null)
                {
                    PreviewWebView.CoreWebView2.DOMContentLoaded -= PreviewWebView_DOMContentLoaded;
                    PreviewWebView.CoreWebView2.ContentLoading -= PreviewWebView_ContentLoading;
                    PreviewWebView.CoreWebView2.NavigationStarting -= PreviewWebView_NavigationStarting;
                    PreviewWebView.CoreWebView2.NavigationCompleted -= PreviewWebView_NavigationCompleted;
                    PreviewWebView.CoreWebView2.DOMContentLoaded += PreviewWebView_DOMContentLoaded;
                    PreviewWebView.CoreWebView2.ContentLoading += PreviewWebView_ContentLoading;
                    PreviewWebView.CoreWebView2.NavigationStarting += PreviewWebView_NavigationStarting;
                    PreviewWebView.CoreWebView2.NavigationCompleted += PreviewWebView_NavigationCompleted;
                }

                _previewWebViewInitialized = true;
                return true;
            }
            catch
            {
                _previewWebViewInitializationTask = null;
                return false;
            }
        }

        private void PreviewWebView_NavigationStarting(object sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Uri) ||
                e.Uri.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            SetPreviewPageLoading(true);
        }

        private void PreviewWebView_ContentLoading(object sender, Microsoft.Web.WebView2.Core.CoreWebView2ContentLoadingEventArgs e)
        {
        }

        private void PreviewWebView_DOMContentLoaded(object sender, Microsoft.Web.WebView2.Core.CoreWebView2DOMContentLoadedEventArgs e)
        {
            SetPreviewPageLoading(false);
        }

        private void PreviewWebView_NavigationCompleted(object sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                return;
            }

            SetPreviewPageLoading(false);
        }

        private void SetPreviewPageLoading(bool isLoading)
        {
            _isPreviewPageLoading = isLoading;
            PreviewLoadingOverlay.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        }

        private string BuildReaderModeHtml(ReaderArticleContent content)
        {
            var backgroundColor = _viewModel.DarkMode ? "#121212" : "#ffffff";
            var textColor = _viewModel.DarkMode ? "#e0e0e0" : "#333333";
            var headerColor = _viewModel.DarkMode ? "#90caf9" : "#673ab7";
            var metaColor = _viewModel.DarkMode ? "#b0b0b0" : "#666666";
            var borderColor = _viewModel.DarkMode ? "#90caf9" : "#673ab7";
            var linkColor = _viewModel.DarkMode ? "#81c784" : "#673ab7";
            var title = string.IsNullOrWhiteSpace(content.Title) ? _selectedItem?.Title : content.Title;
            var encodedTitle = System.Web.HttpUtility.HtmlEncode(title ?? "Article");
            var encodedLink = System.Web.HttpUtility.HtmlEncode(_selectedItem?.Link ?? string.Empty);
            var encodedLinkAttribute = System.Web.HttpUtility.HtmlAttributeEncode(_selectedItem?.Link ?? string.Empty);

            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1'>
    <meta name='color-scheme' content='{(_viewModel.DarkMode ? "dark light" : "light dark")}'>
    <meta http-equiv='Content-Security-Policy' content=""default-src 'none'; img-src https: http: data:; style-src 'unsafe-inline'; font-src data:; base-uri 'none'; form-action 'none'; child-src 'none'; frame-ancestors 'none'"">
    <style>
        body {{
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            margin: 15px;
            line-height: 1.55;
            color: {textColor};
            background-color: {backgroundColor};
        }}
        .article-header {{
            border-bottom: 2px solid {borderColor};
            padding-bottom: 10px;
            margin-bottom: 15px;
        }}
        .article-title {{
            font-size: 22px;
            font-weight: bold;
            color: {headerColor};
            margin-bottom: 8px;
        }}
        .article-meta {{
            font-size: 12px;
            color: {metaColor};
            margin-bottom: 10px;
            word-break: break-all;
        }}
        .article-meta a {{
            color: {linkColor};
            text-decoration: none;
        }}
        .article-content p {{
            margin: 0 0 14px 0;
        }}
        .article-content img {{
            max-width: min(100%, 520px) !important;
            width: auto !important;
            height: auto !important;
            max-height: 320px !important;
            margin: 12px auto;
            display: block;
            border-radius: 6px;
        }}
    </style>
</head>
<body>
    <div class='article-header'>
        <div class='article-title'>{encodedTitle}</div>
        <div class='article-meta'><a href='{encodedLinkAttribute}'>{encodedLink}</a></div>
    </div>
    <div class='article-content'>
        {content.HtmlContent}
    </div>
</body>
</html>";
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private static ScrollViewer FindScrollViewer(DependencyObject root)
        {
            if (root == null)
            {
                return null;
            }

            if (root is ScrollViewer viewer)
            {
                return viewer;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var result = FindScrollViewer(child);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }
    }
}