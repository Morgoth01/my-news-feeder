using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Net;
using System.Windows;
using MyNewsFeeder.Models;
using MyNewsFeeder.Services;
using MyNewsFeeder.Views;
using MaterialDesignThemes.Wpf;

namespace MyNewsFeeder.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private readonly FeedService _feedService;
        private readonly SettingsService _settingsService;
        private readonly BrowserService _browserService;
        private List<Feed> _feeds;
        private AppSettings _settings;
        private string _selectedArticleText;
        private string _selectedArticleLink;
        private string _selectedArticleHtml;
        private string _keyword;
        private bool _isBrowserVisible = false;
        private double _browserHeight = 400;
        private double _articleWebViewHeight = 400;
        private Microsoft.Web.WebView2.Wpf.WebView2 _articleWebView;
        private Microsoft.Web.WebView2.Wpf.WebView2 _linkWebView;
        private bool _isShowContentAlwaysOn = false;
        private int _maxFeeds = 10;
        private int _autoRefreshIntervalMinutes = 10;
        private System.Windows.Threading.DispatcherTimer _autoRefreshTimer;
        private System.Windows.Threading.DispatcherTimer _cacheCleanupTimer;
        private string _copyLinkButtonText = "Copy Link";

        // Window height properties with persisten
        private double _articleWindowHeight = 350;
        private double _browserWindowHeight = 350;

        public int[] AvailableRefreshIntervals => AppSettings.AvailableRefreshIntervals;

        public ObservableCollection<CategoryGroupViewModel> CategoryGroups { get; set; }
        public ICommand CopyLinkCommand { get; }

        public string SelectedArticleText
        {
            get => _selectedArticleText;
            set
            {
                _selectedArticleText = value;
                OnPropertyChanged(nameof(SelectedArticleText));
            }
        }

        public string SelectedArticleLink
        {
            get => _selectedArticleLink;
            set
            {
                _selectedArticleLink = value;
                OnPropertyChanged(nameof(SelectedArticleLink));
            }
        }

        public string SelectedArticleHtml
        {
            get => _selectedArticleHtml;
            set
            {
                _selectedArticleHtml = value;
                OnPropertyChanged(nameof(SelectedArticleHtml));
                UpdateArticleWebView();
            }
        }

        public string Keyword
        {
            get => _keyword;
            set
            {
                _keyword = value;
                OnPropertyChanged(nameof(Keyword));
            }
        }

        public bool IsBrowserVisible
        {
            get => _isBrowserVisible;
            set
            {
                _isBrowserVisible = value;
                OnPropertyChanged(nameof(IsBrowserVisible));
                OnPropertyChanged(nameof(ShowContentButtonText));
            }
        }

        public double BrowserHeight
        {
            get => _browserHeight;
            set
            {
                _browserHeight = Math.Max(200, value);
                OnPropertyChanged(nameof(BrowserHeight));
            }
        }

        public double ArticleWebViewHeight
        {
            get => _articleWebViewHeight;
            set
            {
                _articleWebViewHeight = Math.Max(200, Math.Min(value, 800));
                OnPropertyChanged(nameof(ArticleWebViewHeight));
            }
        }

        public double ArticleWindowHeight
        {
            get => _articleWindowHeight;
            set
            {
                var newValue = Math.Max(200, value);
                if (_articleWindowHeight != newValue)
                {
                    _articleWindowHeight = newValue;
                    OnPropertyChanged(nameof(ArticleWindowHeight));

                    _settings.HasCustomHeights = true;
                    _settings.ArticleWindowHeight = newValue;
                    _settingsService.SaveSettings(_settings);

                }
            }
        }

        public double BrowserWindowHeight
        {
            get => _browserWindowHeight;
            set
            {
                var newValue = Math.Max(200, value);
                if (_browserWindowHeight != newValue)
                {
                    _browserWindowHeight = newValue;
                    OnPropertyChanged(nameof(BrowserWindowHeight));

                    _settings.HasCustomHeights = true;
                    _settings.BrowserWindowHeight = newValue;
                    _settingsService.SaveSettings(_settings);

                }
            }
        }

        public string ShowContentButtonText
        {
            get
            {
                if (IsShowContentAlwaysOn)
                {
                    return "Always-On";
                }
                return IsBrowserVisible ? "Hide Content" : "Show Content";
            }
        }

        public string CopyLinkButtonText
        {
            get => _copyLinkButtonText;
            set
            {
                if (_copyLinkButtonText != value)
                {
                    _copyLinkButtonText = value;
                    OnPropertyChanged(nameof(CopyLinkButtonText));
                }
            }
        }

        public bool DarkMode
        {
            get => _settings.DarkMode;
            set
            {
                _settings.DarkMode = value;
                OnPropertyChanged(nameof(DarkMode));
                _settingsService.SaveSettings(_settings);

                ApplyTheme();
                _browserService.SetDarkMode(value);
                ApplyArticleWebViewTheme();

                if (!string.IsNullOrEmpty(SelectedArticleHtml))
                {
                    var currentArticle = CategoryGroups.SelectMany(cg => cg.Feeds).SelectMany(fg => fg.Items)
                        .FirstOrDefault(item => item.Link == SelectedArticleLink);
                    if (currentArticle != null)
                    {
                        var htmlContent = CreateArticleHtml(currentArticle);
                        SelectedArticleHtml = htmlContent;
                    }
                }
            }
        }

        private void ApplyTheme()
        {
            try
            {
                var paletteHelper = new PaletteHelper();
                var theme = paletteHelper.GetTheme();
                if (_settings.DarkMode)
                {
                    theme.SetBaseTheme(BaseTheme.Dark);
                }
                else
                {
                    theme.SetBaseTheme(BaseTheme.Light);
                }
                paletteHelper.SetTheme(theme);
            }
            catch (Exception)
            {
                // Ignore palette update failures to keep the UI responsive.
            }
        }

        public bool AutoRefresh
        {
            get => _settings.AutoRefresh;
            set
            {
                _settings.AutoRefresh = value;
                OnPropertyChanged(nameof(AutoRefresh));
                _settingsService.SaveSettings(_settings);

                if (value)
                {
                    StartAutoRefreshTimer();
                }
                else
                {
                    StopAutoRefreshTimer();
                }

            }
        }

        public bool AdBlockerEnabled
        {
            get => _settings.AdBlockerEnabled;
            set
            {
                _settings.AdBlockerEnabled = value;
                OnPropertyChanged(nameof(AdBlockerEnabled));
                _settingsService.SaveSettings(_settings);

                _browserService.SetAdBlockerEnabled(value);
            }
        }

        public double TreeWidth
        {
            get => _settings.TreeWidth;
            set
            {
                if (Math.Abs(_settings.TreeWidth - value) > 0.1)
                {
                    _settings.TreeWidth = value;
                    OnPropertyChanged(nameof(TreeWidth));
                    _settingsService.SaveSettings(_settings);
                }
            }
        }

        public int MaxFeeds
        {
            get => _maxFeeds;
            set
            {
                var newValue = Math.Max(1, Math.Min(value, 50));
                if (_maxFeeds != newValue)
                {
                    _maxFeeds = newValue;
                    OnPropertyChanged(nameof(MaxFeeds));
                    OnPropertyChanged(nameof(CurrentFeedSettingsDisplay));

                    _settings.MaxFeeds = _maxFeeds;
                    _settingsService.SaveSettings(_settings);

                }
            }
        }

        public bool IsShowContentAlwaysOn
        {
            get => _isShowContentAlwaysOn;
            set
            {
                _isShowContentAlwaysOn = value;
                OnPropertyChanged(nameof(IsShowContentAlwaysOn));
                OnPropertyChanged(nameof(ShowContentButtonText));

                _settings.IsShowContentAlwaysOn = value;
                _settingsService.SaveSettings(_settings);

                _browserService.SetDarkMode(_settings.DarkMode);

                if (!string.IsNullOrEmpty(SelectedArticleHtml))
                {
                    UpdateArticleWebView();
                }

            }
        }

        public int AutoRefreshIntervalMinutes
        {
            get => _autoRefreshIntervalMinutes;
            set
            {
                if (_autoRefreshIntervalMinutes != value)
                {
                    _autoRefreshIntervalMinutes = value;
                    OnPropertyChanged(nameof(AutoRefreshIntervalMinutes));
                    OnPropertyChanged(nameof(AutoRefreshIntervalDisplay));

                    _settings.AutoRefreshIntervalMinutes = value;
                    _settingsService.SaveSettings(_settings);

                    if (AutoRefresh)
                    {
                        RestartAutoRefreshTimer();
                    }

                }
            }
        }

        public string AutoRefreshIntervalDisplay
        {
            get
            {
                if (AutoRefreshIntervalMinutes < 60)
                {
                    return $"Every {AutoRefreshIntervalMinutes} minutes";
                }
                else
                {
                    var hours = AutoRefreshIntervalMinutes / 60;
                    var minutes = AutoRefreshIntervalMinutes % 60;
                    if (minutes == 0)
                    {
                        return $"Every {hours} hour{(hours > 1 ? "s" : "")}";
                    }
                    else
                    {
                        return $"Every {hours}h {minutes}m";
                    }
                }
            }
        }

        public string CurrentFeedSettingsDisplay
        {
            get
            {
                var totalFeeds = _feeds?.Count(f => f.IsEnabled) ?? 0;
                var totalArticles = totalFeeds * MaxFeeds;
                return $"Currently loading {MaxFeeds} articles from {totalFeeds} feeds (≈{totalArticles} total articles)";
            }
        }

        // Commands
        public ICommand RefreshCommand { get; }
        public ICommand ManageFeedsCommand { get; }
        public ICommand ShowContentCommand { get; }
        public ICommand AdBlockerSettingsCommand { get; }
        public ICommand BrowserBackCommand { get; }
        public ICommand BrowserForwardCommand { get; }
        public ICommand BrowserReloadCommand { get; }
        public ICommand AutoAdjustHeightCommand { get; }
        public ICommand SaveSettingsCommand { get; }
        public ICommand ResetSettingsCommand { get; }
        public ICommand ResetWindowHeightsCommand { get; }
        public ICommand ClearCacheCommand { get; }
        public ICommand AboutCommand { get; }

        public MainViewModel(FeedService feedService,
            SettingsService settingsService,
            BrowserService browserService)
        {
            _feedService = feedService;
            _settingsService = settingsService;
            _browserService = browserService;

            try
            {
                _settings = _settingsService.LoadSettings();
                if (_settings == null)
                {
                    _settings = new AppSettings();
                }
            }
            catch (Exception)
            {
                _settings = new AppSettings();
            }
            TreeWidth = _settings.TreeWidth;
            try
            {
                _feeds = FeedService.NormalizeAndFilterFeeds(_settingsService.LoadFeeds());
            }
            catch (Exception)
            {
                _feeds = new List<Feed>();
            }

            CategoryGroups = new ObservableCollection<CategoryGroupViewModel>();
            Keyword = _settings.KeywordFilter ?? string.Empty;
            _maxFeeds = _settings.MaxFeeds > 0 ? _settings.MaxFeeds : 10;
            IsShowContentAlwaysOn = _settings.IsShowContentAlwaysOn;

            try
            {
                if (_settings.AutoRefreshIntervalMinutes > 0)
                {
                    _autoRefreshIntervalMinutes = _settings.AutoRefreshIntervalMinutes;
                }
                else
                {
                    _autoRefreshIntervalMinutes = 10;
                    _settings.AutoRefreshIntervalMinutes = 10;
                }
            }
            catch (Exception)
            {
                _autoRefreshIntervalMinutes = 10;
                _settings.AutoRefreshIntervalMinutes = 10;
            }

            try
            {
                _articleWindowHeight = _settings.ArticleWindowHeight > 0 ? _settings.ArticleWindowHeight : 350;
                _browserWindowHeight = _settings.BrowserWindowHeight > 0 ? _settings.BrowserWindowHeight : 350;
                ApplyDynamicWindowSizing();
            }
            catch (Exception)
            {
                _articleWindowHeight = 350;
                _browserWindowHeight = 350;
            }


            // Initialize commands
            RefreshCommand = new RelayCommand(async _ => await RefreshFeedsAsync());
            ManageFeedsCommand = new RelayCommand(_ => OpenFeedManager());
            ShowContentCommand = new RelayCommand(_ => ToggleBrowserContent());
            AdBlockerSettingsCommand = new RelayCommand(async _ => await ShowAdBlockerSettingsAsync());
            BrowserBackCommand = new RelayCommand(_ => _browserService.GoBack());
            BrowserForwardCommand = new RelayCommand(_ => _browserService.GoForward());
            BrowserReloadCommand = new RelayCommand(_ => _browserService.Reload());
            AutoAdjustHeightCommand = new RelayCommand(async _ => await AdjustArticleHeightAsync());
            SaveSettingsCommand = new RelayCommand(_ => SaveFeedSettings());
            ResetSettingsCommand = new RelayCommand(_ => ResetFeedSettings());
            ResetWindowHeightsCommand = new RelayCommand(_ => ResetWindowHeights());
            ClearCacheCommand = new RelayCommand(async _ => await ClearBrowserCacheAsync());
            AboutCommand = new RelayCommand(_ => ShowAboutWindow());

            // ** Initialize CopyLink command **
            CopyLinkCommand = new RelayCommand(async _ => await CopyLinkAsync(),
                                           _ => !string.IsNullOrEmpty(SelectedArticleLink));

            ApplyTheme();
            _ = RefreshFeedsAsync();

            _browserService.SetAdBlockerEnabled(_settings.AdBlockerEnabled);

            try
            {
                if (_settings.AutoRefresh)
                {
                    StartAutoRefreshTimer();
                }
            }
            catch (Exception)
            {
            }

            StartCacheCleanupTimer();

        }

        public void SetWebView(Microsoft.Web.WebView2.Wpf.WebView2 webView)
        {
            _linkWebView = webView;

            _browserService.SetWebView(webView);

            if (_linkWebView != null)
            {
                _linkWebView.CoreWebView2InitializationCompleted += (s, e) =>
                {
                    if (e.IsSuccess)
                    {
                        _browserService.SetAdBlockerEnabled(_settings.AdBlockerEnabled);
                    }
                };
            }
        }

        public void SetArticleWebView(Microsoft.Web.WebView2.Wpf.WebView2 webView)
        {
            _articleWebView = webView;

            if (_articleWebView?.CoreWebView2 != null)
            {
                _articleWebView.CoreWebView2.Settings.IsScriptEnabled = false;
                _articleWebView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
                _articleWebView.CoreWebView2.NavigationStarting += OnArticleNavigationStarting;
                _articleWebView.CoreWebView2.NewWindowRequested += OnArticleNewWindowRequested;
                ApplyArticleWebViewTheme();
            }
            else
            {
                _articleWebView.CoreWebView2InitializationCompleted += (s, e) =>
                {
                    if (e.IsSuccess)
                    {
                        _articleWebView.CoreWebView2.Settings.IsScriptEnabled = false;
                        _articleWebView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
                        _articleWebView.CoreWebView2.NavigationStarting += OnArticleNavigationStarting;
                        _articleWebView.CoreWebView2.NewWindowRequested += OnArticleNewWindowRequested;
                        ApplyArticleWebViewTheme();
                    }
                };
            }
        }

        private bool _hasOpenedExternalLink;
        private void OnArticleNewWindowRequested(object sender, Microsoft.Web.WebView2.Core.CoreWebView2NewWindowRequestedEventArgs e)
        {
            if (_hasOpenedExternalLink) return;
            _hasOpenedExternalLink = true;

            e.Handled = true;
            OpenInDefaultBrowser(e.Uri);
        }

        private void OnArticleNavigationStarting(object sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs e)
        {
            var targetUri = e.Uri;
            if (string.IsNullOrEmpty(targetUri))
            {
                return;
            }

            if (string.Equals(targetUri, "about:blank", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (Uri.TryCreate(targetUri, UriKind.Absolute, out var navigationUri) &&
                string.Equals(navigationUri.Scheme, "data", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            e.Cancel = true;

            if (_hasOpenedExternalLink)
            {
                return;
            }

            _hasOpenedExternalLink = true;

            var normalizedUrl = NormalizeExternalLink(targetUri);
            if (string.IsNullOrEmpty(normalizedUrl))
            {
                return;
            }

            if (!_browserService.TryOpenExternalLink(normalizedUrl))
            {
                System.Windows.MessageBox.Show(
                    $"Could not open URL: {normalizedUrl}",
                    "Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        private void OpenInDefaultBrowser(string url)
        {
            var normalizedUrl = NormalizeExternalLink(url);
            if (string.IsNullOrEmpty(normalizedUrl))
            {
                return;
            }

            if (!_browserService.TryOpenExternalLink(normalizedUrl))
            {
                System.Windows.MessageBox.Show(
                    $"Could not open URL: {normalizedUrl}",
                    "Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        public void OnArticleSelected(FeedItem feedItem)
        {
            _hasOpenedExternalLink = false;
            var htmlContent = CreateArticleHtml(feedItem);
            SelectedArticleHtml = htmlContent;
            SelectedArticleText = BuildArticlePlainText(feedItem.Title, feedItem.Description);
            SelectedArticleLink = NormalizeExternalLink(feedItem.Link);

            if (IsShowContentAlwaysOn)
            {
                IsBrowserVisible = true;

                try
                {
                    if (!string.IsNullOrEmpty(SelectedArticleLink))
                    {
                        _browserService.NavigateWithClear(SelectedArticleLink);
                    }
                }
                catch (Exception)
                {
                    // Ignore navigation failures; the browser fallback handles external links.
                }
            }
            else
            {
                ClearBrowserContent();
                IsBrowserVisible = false;
            }

        }

        private void ClearBrowserContent()
        {
            try
            {
                _browserService.NavigateToBlank();
                IsBrowserVisible = false;
            }
            catch (Exception)
            {
                // Ignore failures when clearing the embedded browser content.
            }
        }

        private string CreateArticleHtml(FeedItem feedItem)
        {
            var backgroundColor = _settings.DarkMode ? "#121212" : "#ffffff";
            var textColor = _settings.DarkMode ? "#e0e0e0" : "#333";
            var headerColor = _settings.DarkMode ? "#90caf9" : "#673ab7";
            var metaColor = _settings.DarkMode ? "#b0b0b0" : "#666";
            var readMoreBg = _settings.DarkMode ? "#1e1e1e" : "#f8f8f8";
            var borderColor = _settings.DarkMode ? "#90caf9" : "#673ab7";
            var linkColor = _settings.DarkMode ? "#81c784" : "#673ab7";

            var sanitizedDescription = SanitizeHtml(feedItem.Description);
            var normalizedLink = NormalizeExternalLink(feedItem.Link);
            var readMoreContent = BuildReadMoreContent(normalizedLink);

            var htmlTemplate = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1'>
    <meta name='color-scheme' content='{(_settings.DarkMode ? "dark light" : "light dark")}'>
    <meta http-equiv='Content-Security-Policy' content=""default-src 'none'; img-src https: http: data:; style-src 'unsafe-inline'; font-src data:; base-uri 'none'; form-action 'none'; child-src 'none'; frame-ancestors 'none'"">
    <style>
        :root {{
            color-scheme: {(_settings.DarkMode ? "dark" : "light")};
        }}
        
        body {{
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            margin: 15px;
            padding: 0;
            line-height: 1.5;
            color: {textColor};
            background-color: {backgroundColor};
            overflow-x: hidden;
        }}
        .article-header {{
            border-bottom: 2px solid {borderColor};
            padding-bottom: 10px;
            margin-bottom: 15px;
        }}
        .article-title {{
            font-size: 20px;
            font-weight: bold;
            color: {headerColor};
            margin-bottom: 8px;
            word-wrap: break-word;
        }}
        .article-meta {{
            font-size: 12px;
            color: {metaColor};
            margin-bottom: 10px;
        }}
        .article-content {{
            font-size: 14px;
            text-align: justify;
            word-wrap: break-word;
            line-height: 1.4;
            color: {textColor};
        }}
        .article-content img {{
            max-width: 250px;
            width: 100%;
            height: auto;
            margin: 8px 16;
            border-radius: 4px;
            box-shadow: 0 1px 4px rgba(0,0,0,0.3);
            display: block;
        }}
        .article-content a {{
            color: {linkColor};
            text-decoration: none;
        }}
        .article-content a:hover {{
            text-decoration: underline;
        }}
        .read-more {{
            margin-top: 15px;
            padding: 10px;
            background-color: {readMoreBg};
            border-left: 3px solid {borderColor};
            border-radius: 3px;
            font-size: 12px;
        }}
        .feed-source {{
            font-weight: bold;
            color: {headerColor};
        }}
        .external-link {{
            color: {linkColor};
            font-weight: bold;
            text-decoration: none;
            word-break: break-all;
            font-size: 11px;
        }}
        .external-link:hover {{
            color: {headerColor};
        }}
    </style>
</head>
<body>
    <div class='article-header'>
        <div class='article-title'>{System.Web.HttpUtility.HtmlEncode(feedItem.Title)}</div>
        <div class='article-meta'>
            <span class='feed-source'>{System.Web.HttpUtility.HtmlEncode(feedItem.FeedName)}</span> • 
            {feedItem.PublicationDate:yyyy-MM-dd HH:mm}
        </div>
    </div>
    <div class='article-content'>
        {sanitizedDescription}
    </div>
    <div class='read-more'>
        {readMoreContent}
    </div>
</body>
</html>";

            return htmlTemplate;
        }

        private void UpdateArticleWebView()
        {
            if (_articleWebView?.CoreWebView2 != null && !string.IsNullOrEmpty(SelectedArticleHtml))
            {
                try
                {
                    _articleWebView.NavigateToString(SelectedArticleHtml);
                    _articleWebView.CoreWebView2.NavigationCompleted += async (s, e) =>
                    {
                        if (e.IsSuccess)
                        {
                            await Task.Delay(500);
                            await AdjustArticleHeightAsync();
                        }
                    };
                }
                catch (Exception)
                {
                }
            }
        }

        private Task AdjustArticleHeightAsync()
        {
            if (_articleWebView?.CoreWebView2 != null)
            {
                try
                {
                    _ = _articleWebView.CoreWebView2.ExecuteScriptAsync(
                        "Math.max(document.body.scrollHeight, document.documentElement.scrollHeight).toString()")
                    .ContinueWith(task =>
                    {
                        if (task.IsCompletedSuccessfully)
                        {
                            var heightString = task.Result;
                            if (int.TryParse(heightString.Trim('"'), out int contentHeight))
                            {
                                var adjustedHeight = Math.Max(200, Math.Min(contentHeight + 40, 600));
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    ArticleWebViewHeight = adjustedHeight;
                                });
                            }
                        }
                    });
                }
                catch (Exception)
                {
                }
            }

            return Task.CompletedTask;
        }

        private void OpenFeedManager()
        {
            var feedManagerViewModel = new FeedManagerViewModel(_settingsService);
            var feedManagerWindow = new FeedManagerWindow
            {
                DataContext = feedManagerViewModel,
                Owner = Application.Current.MainWindow
            };
            feedManagerWindow.ShowDialog();

            // Reload settings to pick up new Categories and expanded states
            _settings = _settingsService.LoadSettings();

            // Reload feeds and refresh the tree
            _feeds = _settingsService.LoadFeeds();
            _ = RefreshFeedsAsync();
        }

        private void ToggleBrowserContent()
        {
            if (!IsBrowserVisible)
            {
                if (!string.IsNullOrEmpty(SelectedArticleLink))
                {
                    IsBrowserVisible = true;
                    BrowserHeight = 400;

                    try
                    {
                        _browserService.Navigate(SelectedArticleLink);
                    }
                    catch (Exception)
                    {
                    }
                }
                else
                {
                    System.Windows.MessageBox.Show("No article selected or no link available.",
                        "Information", System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
            }
            else
            {
                if (!IsShowContentAlwaysOn)
                {
                    IsBrowserVisible = false;
                    _browserService.NavigateToBlank();
                }
                else
                {
                }
            }
        }

        private async Task ShowAdBlockerSettingsAsync()
        {
            try
            {
                var stats = _browserService.GetAdBlockerStats();
                var detailedStats = _browserService.GetDetailedAdBlockerStats();

                var message = $"Enhanced AdBlocker Statistics:\n\n" +
                    $"Blocked Domains: {stats.domains:N0}\n" +
                    $"Blocked Patterns: {stats.patterns:N0}\n" +
                    $"Last Update: {detailedStats.lastUpdate:yyyy-MM-dd HH:mm}\n\n";

                if (detailedStats.availableLists.Count > 0)
                {
                    message += "Available Filter Lists:\n";
                    foreach (var list in detailedStats.availableLists)
                    {
                        message += $"• {list}\n";
                    }
                }

                if (detailedStats.failedLists.Count > 0)
                {
                    message += "\nFailed to Load:\n";
                    foreach (var list in detailedStats.failedLists)
                    {
                        message += $"• {list}\n";
                    }
                }

                message += $"\nAdBlocker is currently: {(AdBlockerEnabled ? "Enabled" : "Disabled")}\n" +
                    $"Dark Mode: Native website themes only (no CSS filters)\n\n" +
                    $"Filter lists update automatically every 24 hours.\n" +
                    $"You can also customize blocked domains in 'adblocker_hosts.txt'.";

                var result = System.Windows.MessageBox.Show(
                    message + "\n\nUpdate filter lists now?",
                    "Enhanced AdBlocker Settings",
                    System.Windows.MessageBoxButton.YesNoCancel,
                    System.Windows.MessageBoxImage.Information);

                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    try
                    {
                        Mouse.OverrideCursor = Cursors.Wait;
                        var updateResult = await _browserService.UpdateAdBlockerListsAsync();

                        var updateMessage = $"AdBlocker filter lists refreshed.\n\n" +
                            $"Downloaded lists: {updateResult.DownloadedLists}\n" +
                            $"Loaded from cache: {updateResult.CachedLists}";

                        if (updateResult.FailedLists.Count > 0)
                        {
                            var failedSummary = string.Join("\n - ", updateResult.FailedLists);
                            updateMessage += "\n\nThe following lists failed to update:\n - " + failedSummary;
                        }
                        else if (updateResult.DownloadedLists > 0)
                        {
                            updateMessage += "\n\nAll filter lists updated successfully.";
                        }
                        else
                        {
                            updateMessage += "\n\nNo new updates were available; cached lists remain current.";
                        }

                        System.Windows.MessageBox.Show(
                            updateMessage,
                            "AdBlocker Update",
                            System.Windows.MessageBoxButton.OK,
                            updateResult.FailedLists.Count > 0 ? System.Windows.MessageBoxImage.Warning : System.Windows.MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show(
                            $"Error updating filter lists: {ex.Message}",
                            "AdBlocker Update",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Error);
                    }
                    finally
                    {
                        Mouse.OverrideCursor = null;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error accessing AdBlocker settings: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void ClearBrowserOnStartup()
        {
            _browserService.NavigateToBlank();
        }

        public Dictionary<string, bool> SaveCategoryExpandedStates()
        {
            var expandedStates = new Dictionary<string, bool>();
            foreach (var category in CategoryGroups)
            {
                expandedStates[category.Name] = category.IsExpanded;
            }
            _settings.CategoryExpandedStates = expandedStates;
            _settingsService.SaveSettings(_settings);
            return expandedStates;
        }

        public Dictionary<string, bool> SaveFeedExpandedStates()
        {
            var expandedStates = new Dictionary<string, bool>();
            foreach (var category in CategoryGroups)
            {
                foreach (var feed in category.Feeds)
                {
                    expandedStates[feed.Name] = feed.IsExpanded;
                }
            }
            _settings.TreeViewExpandedStates = expandedStates;
            _settingsService.SaveSettings(_settings);
            return expandedStates;
        }

        private async Task RefreshFeedsAsync()
        {
            try
            {
                if (_feeds.Count == 0)
                {
                    CategoryGroups.Clear();
                    return;
                }

                var categoryExpandedStates = _settings.CategoryExpandedStates ?? new Dictionary<string, bool>();
                var feedExpandedStates = _settings.TreeViewExpandedStates ?? new Dictionary<string, bool>();

                var items = await _feedService.FetchArticlesAsync(_feeds, Keyword, MaxFeeds);

                // Group items by category
                var categorizedItems = items.GroupBy(item =>
                {
                    var feed = _feeds.FirstOrDefault(f => f.Name == item.FeedName);
                    return feed?.Category ?? "Default";
                }).ToDictionary(g => g.Key, g => g);

                CategoryGroups.Clear();

                // Create CategoryGroups in the order defined in Settings.Categories
                // This ensures the main window displays categories in user-defined order
                foreach (var categoryName in _settings.Categories)
                {
                    // Only process categories that have actual feed items
                    if (categorizedItems.TryGetValue(categoryName, out var categoryItems))
                    {
                        var categoryViewModel = new CategoryGroupViewModel
                        {
                            Name = categoryName,
                            IsExpanded = categoryExpandedStates.TryGetValue(categoryName, out var expanded) ? expanded : true
                        };

                        var feedGroups = categoryItems.GroupBy(item => item.FeedName);

                        foreach (var feedGroup in feedGroups)
                        {
                            var feedViewModel = new FeedGroupViewModel
                            {
                                Name = feedGroup.Key,
                                Category = categoryName,
                                Items = new ObservableCollection<FeedItem>(feedGroup.ToList()),
                                IsExpanded = feedExpandedStates.TryGetValue(feedGroup.Key, out var feedExpanded) ? feedExpanded : true
                            };

                            categoryViewModel.Feeds.Add(feedViewModel);
                        }

                        CategoryGroups.Add(categoryViewModel);
                    }
                }

                // Handle any categories that exist in feeds but not in settings (shouldn't normally happen)
                foreach (var categoryGroup in categorizedItems.Where(kvp => !_settings.Categories.Contains(kvp.Key)))
                {
                    var categoryViewModel = new CategoryGroupViewModel
                    {
                        Name = categoryGroup.Key,
                        IsExpanded = categoryExpandedStates.TryGetValue(categoryGroup.Key, out var expanded) ? expanded : true
                    };

                    var feedGroups = categoryGroup.Value.GroupBy(item => item.FeedName);

                    foreach (var feedGroup in feedGroups)
                    {
                        var feedViewModel = new FeedGroupViewModel
                        {
                            Name = feedGroup.Key,
                            Category = categoryGroup.Key,
                            Items = new ObservableCollection<FeedItem>(feedGroup.ToList()),
                            IsExpanded = feedExpandedStates.TryGetValue(feedGroup.Key, out var feedExpanded) ? feedExpanded : true
                        };

                        categoryViewModel.Feeds.Add(feedViewModel);
                    }

                    CategoryGroups.Add(categoryViewModel);
                }

                OnPropertyChanged(nameof(CategoryGroups));
                OnPropertyChanged(nameof(CurrentFeedSettingsDisplay));
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error loading feeds: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void SaveFeedSettings()
        {
            try
            {
                _settings.MaxFeeds = _maxFeeds;
                _settings.IsShowContentAlwaysOn = _isShowContentAlwaysOn;
                _settingsService.SaveSettings(_settings);

                OnPropertyChanged(nameof(CurrentFeedSettingsDisplay));
                OnPropertyChanged(nameof(MaxFeeds));

                _ = RefreshFeedsAsync();


                System.Windows.MessageBox.Show(
                    $"Feed settings saved successfully!\n\n" +
                    $"Max Articles per Feed: {_maxFeeds}\n" +
                    $"Always-On Content: {(IsShowContentAlwaysOn ? "Enabled" : "Disabled")}\n" +
                    $"Dark Mode: Native website themes only\n" +
                    $"Feeds are being refreshed with new settings...",
                    "Settings Saved",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Error saving feed settings: {ex.Message}",
                    "Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        private void ResetFeedSettings()
        {
            try
            {
                var result = System.Windows.MessageBox.Show(
                    "Reset feed settings to default values?\n\n" +
                    "This will set:\n" +
                    "• Max Articles per Feed: 10\n" +
                    "• Always-On Content: Disabled\n\n" +
                    "Continue?",
                    "Reset Feed Settings",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);

                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    MaxFeeds = 10;
                    IsShowContentAlwaysOn = false;

                    _settings.MaxFeeds = MaxFeeds;
                    _settings.IsShowContentAlwaysOn = IsShowContentAlwaysOn;
                    _settingsService.SaveSettings(_settings);

                    OnPropertyChanged(nameof(CurrentFeedSettingsDisplay));
                    _ = RefreshFeedsAsync();


                    System.Windows.MessageBox.Show(
                        "Feed settings have been reset to default values.",
                        "Settings Reset",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Error resetting feed settings: {ex.Message}",
                    "Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        private void ResetWindowHeights()
        {
            try
            {
                var result = System.Windows.MessageBox.Show(
                    "Reset window heights to dynamic screen-based sizing?\n\n" +
                    "This will:\n" +
                    "• Calculate equal heights based on your screen size\n" +
                    "• Re-enable automatic sizing for different screen resolutions\n" +
                    "• Browser window will extend to bottom of app\n\n" +
                    "Continue?",
                    "Reset Window Heights",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);

                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    _settings.HasCustomHeights = false;
                    _settingsService.SaveSettings(_settings);

                    ApplyDynamicWindowSizing();


                    System.Windows.MessageBox.Show(
                        $"Window heights have been reset to dynamic equal sizing.\n\n" +
                        $"Article Window: {ArticleWindowHeight}px\n" +
                        $"Browser Window: {BrowserWindowHeight}px\n\n" +
                        $"Both windows are equal and calculated based on your screen resolution.\n" +
                        $"Browser window extends to the bottom of the app.",
                        "Heights Reset",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Error resetting window heights: {ex.Message}",
                    "Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        private (double articleHeight, double browserHeight) CalculateInitialWindowSizes()
        {
            try
            {
                var workingArea = System.Windows.SystemParameters.WorkArea;
                var availableHeight = workingArea.Height;


                var usableHeight = availableHeight - 150;
                var halfHeight = usableHeight / 2;

                var articleHeight = Math.Max(200, Math.Min(halfHeight, 600));
                var browserHeight = articleHeight;


                return (Math.Floor(articleHeight), Math.Floor(browserHeight));
            }
            catch (Exception)
            {
                return (350, 350);
            }
        }

        private void ApplyDynamicWindowSizing()
        {
            if (!_settings.HasCustomHeights)
            {
                var (articleHeight, browserHeight) = CalculateInitialWindowSizes();

                _articleWindowHeight = articleHeight;
                _browserWindowHeight = browserHeight;

                _settings.ArticleWindowHeight = articleHeight;
                _settings.BrowserWindowHeight = browserHeight;
                _settings.HasCustomHeights = false;
                _settingsService.SaveSettings(_settings);

                OnPropertyChanged(nameof(ArticleWindowHeight));
                OnPropertyChanged(nameof(BrowserWindowHeight));

            }
            else
            {
            }
        }

        private void StartAutoRefreshTimer()
        {
            try
            {
                StopAutoRefreshTimer();

                _autoRefreshTimer = new System.Windows.Threading.DispatcherTimer();
                _autoRefreshTimer.Interval = TimeSpan.FromMinutes(AutoRefreshIntervalMinutes);
                _autoRefreshTimer.Tick += AutoRefreshTimer_Tick;
                _autoRefreshTimer.Start();

            }
            catch (Exception)
            {
                // Ignore errors when opening the About dialog.
            }
        }

        private void StopAutoRefreshTimer()
        {
            try
            {
                if (_autoRefreshTimer != null)
                {
                    _autoRefreshTimer.Stop();
                    _autoRefreshTimer.Tick -= AutoRefreshTimer_Tick;
                    _autoRefreshTimer = null;
                }
            }
            catch (Exception)
            {
                // Ignore clipboard errors and keep the previous button state.
            }
        }

        private void RestartAutoRefreshTimer()
        {
            if (AutoRefresh)
            {
                StartAutoRefreshTimer();
            }
        }

        private async void AutoRefreshTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                await RefreshFeedsAsync();
            }
            catch (Exception)
            {
            }
        }

        private void StartCacheCleanupTimer()
        {
            try
            {
                _cacheCleanupTimer = new System.Windows.Threading.DispatcherTimer();
                _cacheCleanupTimer.Interval = TimeSpan.FromHours(2);
                _cacheCleanupTimer.Tick += async (s, e) => await AutoCacheCleanup();
                _cacheCleanupTimer.Start();

            }
            catch (Exception)
            {
            }
        }

        private async Task AutoCacheCleanup()
        {
            try
            {
                await _browserService.ClearOldCacheAsync(6);
            }
            catch (Exception)
            {
                // Ignore background cache cleanup failures.
            }
        }

        private async Task ClearBrowserCacheAsync()
        {
            try
            {
                await _browserService.ClearSelectiveCacheAsync();

                System.Windows.MessageBox.Show(
                    "Browser cache has been cleared successfully!\n\n" +
                    "This should reduce the app folder size and free up disk space.",
                    "Cache Cleared",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Error clearing cache: {ex.Message}",
                    "Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        public (int domains, int patterns) GetAdBlockerStats()
        {
            return _browserService.GetAdBlockerStats();
        }

        public void DiagnoseAdBlocker()
        {
            try
            {
                var stats = _browserService.GetAdBlockerStats();

                var detailedStats = _browserService.GetDetailedAdBlockerStats();

                var message = $"Blocked domains: {stats.domains}\nBlocked patterns: {stats.patterns}";

                if (detailedStats.lastUpdate != DateTime.MinValue)
                {
                    message += $"\nLast list update: {detailedStats.lastUpdate:G}";
                }

                if (detailedStats.availableLists.Count > 0)
                {
                    message += "\n\nAvailable lists:\n - " + string.Join("\n - ", detailedStats.availableLists);
                }

                if (detailedStats.failedLists.Count > 0)
                {
                    message += "\n\nFailed lists:\n - " + string.Join("\n - ", detailedStats.failedLists);
                }

                System.Windows.MessageBox.Show(
                    message,
                    "AdBlocker Diagnostics",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            catch (Exception)
            {
                System.Windows.MessageBox.Show(
                    "Unable to gather AdBlocker diagnostics.",
                    "AdBlocker Diagnostics",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }

        }

        public void Cleanup()
        {
            try
            {
                StopAutoRefreshTimer();

                if (_cacheCleanupTimer != null)
                {
                    _cacheCleanupTimer.Stop();
                    _cacheCleanupTimer = null;
                }

            }
            catch (Exception)
            {
                // Ignore cleanup failures during shutdown.
            }
        }

        private void ShowAboutWindow()
        {
            try
            {
                var aboutWindow = new AboutWindow();
                aboutWindow.ShowDialog();
            }
            catch (Exception)
            {
                // Ignore errors when opening the About dialog.
            }
        }
        private async Task CopyLinkAsync()
        {
            try
            {
                // Copy link to clipboard
                Clipboard.SetText(SelectedArticleLink);

                // Set button text to indicate success
                CopyLinkButtonText = "Copied!";

                // Wait for 2 seconds before reverting text
                await Task.Delay(2000);

                // Revert button text back to original
                CopyLinkButtonText = "Copy Link";
            }
            catch (Exception)
            {
                // Ignore clipboard errors and keep the previous button state.
            }
        }

        private string BuildArticlePlainText(string title, string description)
        {
            var normalizedText = NormalizeHtmlToText(description);
            var collapsed = CollapseWhitespace(normalizedText);

            if (string.IsNullOrWhiteSpace(collapsed))
            {
                return title ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                return collapsed;
            }

            return $"{title}\n\n{collapsed}";
        }

        private string SanitizeHtml(string html)
        {
            var normalizedText = NormalizeHtmlToText(html);
            var collapsed = CollapseWhitespace(normalizedText);
            return ConvertPlainTextToHtml(collapsed);
        }

        private static string NormalizeHtmlToText(string html)
        {
            if (string.IsNullOrEmpty(html))
            {
                return string.Empty;
            }

            var textBuilder = new System.Text.StringBuilder();
            var input = html;
            int index = 0;

            while (index < input.Length)
            {
                var current = input[index];
                if (current == '<')
                {
                    var closeIndex = input.IndexOf('>', index);
                    if (closeIndex == -1)
                    {
                        break;
                    }

                    var tagContent = input.Substring(index + 1, closeIndex - index - 1).Trim();
                    var lowerTag = tagContent.ToLowerInvariant();

                    if (lowerTag.StartsWith("!--"))
                    {
                        var commentEnd = input.IndexOf("-->", closeIndex + 1, StringComparison.Ordinal);
                        if (commentEnd == -1)
                        {
                            break;
                        }

                        index = commentEnd + 3;
                        continue;
                    }

                    if (lowerTag.StartsWith("script"))
                    {
                        var scriptEnd = input.IndexOf("</script", closeIndex + 1, StringComparison.OrdinalIgnoreCase);
                        if (scriptEnd == -1)
                        {
                            break;
                        }

                        var scriptClose = input.IndexOf('>', scriptEnd);
                        if (scriptClose == -1)
                        {
                            break;
                        }

                        index = scriptClose + 1;
                        continue;
                    }

                    if (lowerTag.StartsWith("style"))
                    {
                        var styleEnd = input.IndexOf("</style", closeIndex + 1, StringComparison.OrdinalIgnoreCase);
                        if (styleEnd == -1)
                        {
                            break;
                        }

                        var styleClose = input.IndexOf('>', styleEnd);
                        if (styleClose == -1)
                        {
                            break;
                        }

                        index = styleClose + 1;
                        continue;
                    }

                    if (lowerTag.StartsWith("br"))
                    {
                        textBuilder.Append('\n');
                    }
                    else if (lowerTag.StartsWith("li"))
                    {
                        textBuilder.Append("\n• ");
                    }
                    else if (lowerTag.StartsWith("/li"))
                    {
                        textBuilder.Append('\n');
                    }
                    else if (lowerTag.StartsWith("/p") || lowerTag.StartsWith("/div"))
                    {
                        textBuilder.Append("\n\n");
                    }
                    else if (lowerTag.StartsWith("p") || lowerTag.StartsWith("div"))
                    {
                        textBuilder.Append("\n\n");
                    }

                    index = closeIndex + 1;
                    continue;
                }

                if (current == '\r' || current == '\n')
                {
                    textBuilder.Append('\n');
                    index++;
                    continue;
                }

                textBuilder.Append(current);
                index++;
            }

            var decoded = WebUtility.HtmlDecode(textBuilder.ToString());
            return decoded ?? string.Empty;
        }

        private static string CollapseWhitespace(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = value.Replace("\r\n", "\n").Replace('\r', '\n');
            var lines = normalized.Split('\n');
            var builder = new System.Text.StringBuilder();
            bool previousWasEmpty = true;

            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd();
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (!previousWasEmpty)
                    {
                        builder.AppendLine();
                        builder.AppendLine();
                    }
                    previousWasEmpty = true;
                }
                else
                {
                    if (builder.Length > 0 && previousWasEmpty)
                    {
                        builder.AppendLine();
                    }

                    if (line.StartsWith("• "))
                    {
                        builder.Append(line.Trim());
                    }
                    else
                    {
                        builder.Append(line);
                    }
                    builder.AppendLine();
                    previousWasEmpty = false;
                }
            }

            var result = builder.ToString().Trim();
            return result;
        }

        private static string ConvertPlainTextToHtml(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "<p></p>";
            }

            var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            var paragraphs = normalized.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
            var builder = new System.Text.StringBuilder();

            foreach (var paragraph in paragraphs)
            {
                var trimmed = paragraph.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                builder.Append("<p>");
                var encoded = System.Web.HttpUtility.HtmlEncode(trimmed);
                encoded = encoded.Replace("\n", "<br/>");
                builder.Append(encoded);
                builder.Append("</p>");
            }

            if (builder.Length == 0)
            {
                builder.Append("<p></p>");
            }

            return builder.ToString();
        }

        private void ApplyArticleWebViewTheme()
        {
            if (_articleWebView?.CoreWebView2 == null)
            {
                return;
            }

            var color = _settings.DarkMode
                ? System.Drawing.Color.FromArgb(255, 18, 18, 18)
                : System.Drawing.Color.FromArgb(255, 245, 245, 245);

            _articleWebView.DefaultBackgroundColor = color;
        }

        private string NormalizeExternalLink(string link)
        {
            if (string.IsNullOrWhiteSpace(link))
            {
                return string.Empty;
            }

            if (Uri.TryCreate(link.Trim(), UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                return uri.AbsoluteUri;
            }

            return string.Empty;
        }

        private string BuildReadMoreContent(string normalizedLink)
        {
            if (string.IsNullOrEmpty(normalizedLink))
            {
                return "<strong>Full Article:</strong> <span class='external-link unavailable'>Link not available</span>";
            }

            var encodedLink = System.Web.HttpUtility.HtmlEncode(normalizedLink);
            return $"<strong>Full Article (Use 'Show Content' for integrated browser):</strong> <a href=\"{encodedLink}\" class='external-link'>{encodedLink}</a>";
        }
    }
}
