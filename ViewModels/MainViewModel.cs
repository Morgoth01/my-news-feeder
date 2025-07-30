using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
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

        // Window height properties with persisten
        private double _articleWindowHeight = 350;
        private double _browserWindowHeight = 350;

        public int[] AvailableRefreshIntervals => AppSettings.AvailableRefreshIntervals;

        public ObservableCollection<CategoryGroupViewModel> CategoryGroups { get; set; }

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
                System.Diagnostics.Debug.WriteLine($"🔧 IsBrowserVisible changed to: {value}");
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

                    System.Diagnostics.Debug.WriteLine($"Article window height changed to: {_articleWindowHeight} (marked as custom)");
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

                    System.Diagnostics.Debug.WriteLine($"Browser window height changed to: {_browserWindowHeight} (marked as custom)");
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
                System.Diagnostics.Debug.WriteLine($"🌙 Native dark mode setting changed to: {value}");

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
                    System.Diagnostics.Debug.WriteLine("Applied Dark Theme to UI");
                }
                else
                {
                    theme.SetBaseTheme(BaseTheme.Light);
                    System.Diagnostics.Debug.WriteLine("Applied Light Theme to UI");
                }
                paletteHelper.SetTheme(theme);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error applying theme: {ex.Message}");
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

                System.Diagnostics.Debug.WriteLine($"Auto-refresh {(value ? "enabled" : "disabled")} with {AutoRefreshIntervalMinutes} minute interval");
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
                System.Diagnostics.Debug.WriteLine($"🔧 AdBlocker enabled set to: {value}");
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

                    System.Diagnostics.Debug.WriteLine($"MaxFeeds changed and saved: {_maxFeeds}");
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

                System.Diagnostics.Debug.WriteLine($"Show Content Always-On: {value}");
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

                    System.Diagnostics.Debug.WriteLine($"Auto-refresh interval changed to: {value} minutes");
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
                    System.Diagnostics.Debug.WriteLine("❌ Settings is null, creating default settings");
                    _settings = new AppSettings();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error loading settings: {ex.Message}");
                _settings = new AppSettings();
            }
            TreeWidth = _settings.TreeWidth;
            try
            {
                _feeds = _settingsService.LoadFeeds();
                if (_feeds == null)
                {
                    _feeds = new List<Feed>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error loading feeds: {ex.Message}");
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error loading auto-refresh interval: {ex.Message}");
                _autoRefreshIntervalMinutes = 10;
                _settings.AutoRefreshIntervalMinutes = 10;
            }

            try
            {
                _articleWindowHeight = _settings.ArticleWindowHeight > 0 ? _settings.ArticleWindowHeight : 350;
                _browserWindowHeight = _settings.BrowserWindowHeight > 0 ? _settings.BrowserWindowHeight : 350;

                ApplyDynamicWindowSizing();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error loading window heights: {ex.Message}");
                _articleWindowHeight = 350;
                _browserWindowHeight = 350;
            }

            System.Diagnostics.Debug.WriteLine($"Loaded settings - MaxFeeds: {_maxFeeds}, AutoRefresh: {_settings.AutoRefresh}, Interval: {_autoRefreshIntervalMinutes} minutes");

            // Initialize commands
            RefreshCommand = new RelayCommand(async _ => await RefreshFeedsAsync());
            ManageFeedsCommand = new RelayCommand(_ => OpenFeedManager());
            ShowContentCommand = new RelayCommand(_ => ToggleBrowserContent());
            AdBlockerSettingsCommand = new RelayCommand(_ => ShowAdBlockerSettings());
            BrowserBackCommand = new RelayCommand(_ => _browserService.GoBack());
            BrowserForwardCommand = new RelayCommand(_ => _browserService.GoForward());
            BrowserReloadCommand = new RelayCommand(_ => _browserService.Reload());
            AutoAdjustHeightCommand = new RelayCommand(async _ => await AdjustArticleHeightAsync());
            SaveSettingsCommand = new RelayCommand(_ => SaveFeedSettings());
            ResetSettingsCommand = new RelayCommand(_ => ResetFeedSettings());
            ResetWindowHeightsCommand = new RelayCommand(_ => ResetWindowHeights());
            ClearCacheCommand = new RelayCommand(async _ => await ClearBrowserCacheAsync());
            AboutCommand = new RelayCommand(_ => ShowAboutWindow());

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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error starting auto-refresh timer: {ex.Message}");
            }

            StartCacheCleanupTimer();

            System.Diagnostics.Debug.WriteLine($"✅ MainViewModel initialization completed");
        }

        public void SetWebView(Microsoft.Web.WebView2.Wpf.WebView2 webView)
        {
            _linkWebView = webView;
            System.Diagnostics.Debug.WriteLine("🔧 MainViewModel.SetWebView called");

            _browserService.SetWebView(webView);

            if (_linkWebView != null)
            {
                _linkWebView.CoreWebView2InitializationCompleted += (s, e) =>
                {
                    if (e.IsSuccess)
                    {
                        System.Diagnostics.Debug.WriteLine("✅ Link WebView2 initialized in MainViewModel");
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
                _articleWebView.CoreWebView2.WebMessageReceived += OnArticleWebMessageReceived;
                _articleWebView.CoreWebView2.NewWindowRequested += OnArticleNewWindowRequested;
            }
            else
            {
                _articleWebView.CoreWebView2InitializationCompleted += (s, e) =>
                {
                    if (e.IsSuccess)
                    {
                        _articleWebView.CoreWebView2.WebMessageReceived += OnArticleWebMessageReceived;
                        _articleWebView.CoreWebView2.NewWindowRequested += OnArticleNewWindowRequested;
                    }
                };
            }
        }

        private void OnArticleNewWindowRequested(object sender, Microsoft.Web.WebView2.Core.CoreWebView2NewWindowRequestedEventArgs e)
        {
            try
            {
                var url = e.Uri;
                e.Handled = true;
                OpenInDefaultBrowser(url);
                System.Diagnostics.Debug.WriteLine($"Article link opened in default browser: {url}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling article new window: {ex.Message}");
            }
        }

        private void OnArticleWebMessageReceived(object sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var message = e.TryGetWebMessageAsString();
                var messageData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(message);

                if (messageData.ContainsKey("action") && messageData["action"].ToString() == "openInDefaultBrowser")
                {
                    var url = messageData["url"].ToString();
                    OpenInDefaultBrowser(url);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling article web message: {ex.Message}");
            }
        }

        private void OpenInDefaultBrowser(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                System.Diagnostics.Debug.WriteLine($"Opened in default browser: {url}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to open URL in default browser: {ex.Message}");
                System.Windows.MessageBox.Show($"Could not open URL: {url}\nError: {ex.Message}", "Error");
            }
        }

        public void OnArticleSelected(FeedItem feedItem)
        {
            System.Diagnostics.Debug.WriteLine($"🔗 Article selected: {feedItem.Title}");
            System.Diagnostics.Debug.WriteLine($"🔧 Always-On enabled: {IsShowContentAlwaysOn}");

            var htmlContent = CreateArticleHtml(feedItem);
            SelectedArticleHtml = htmlContent;
            SelectedArticleText = $"{feedItem.Title}\n\n{feedItem.Description}";
            SelectedArticleLink = feedItem.Link;

            if (IsShowContentAlwaysOn)
            {
                IsBrowserVisible = true;

                try
                {
                    _browserService.NavigateWithClear(SelectedArticleLink);
                    System.Diagnostics.Debug.WriteLine($"✅ Auto-navigating with clear to: {SelectedArticleLink}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Error in NavigateWithClear: {ex.Message}");
                }
            }
            else
            {
                ClearBrowserContent();
                IsBrowserVisible = false;
                System.Diagnostics.Debug.WriteLine("🔧 Browser hidden (Always-On disabled)");
            }

            System.Diagnostics.Debug.WriteLine($"Selected article: {feedItem.Title}");
            System.Diagnostics.Debug.WriteLine($"Article link: {feedItem.Link}");
            System.Diagnostics.Debug.WriteLine($"Browser visible: {IsBrowserVisible}");
        }

        private void ClearBrowserContent()
        {
            try
            {
                _browserService.NavigateToBlank();
                IsBrowserVisible = false;
                System.Diagnostics.Debug.WriteLine("Browser content cleared.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing browser content: {ex.Message}");
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

            var htmlTemplate = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1'>
    <meta name='color-scheme' content='{(_settings.DarkMode ? "dark light" : "light dark")}'>
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
    <script>
        document.addEventListener('DOMContentLoaded', function() {{
            document.addEventListener('click', function(e) {{
                if (e.target.tagName === 'A' && e.target.href) {{
                    e.preventDefault();
                    try {{
                        if (window.chrome && window.chrome.webview) {{
                            window.chrome.webview.postMessage(JSON.stringify({{
                                action: 'openInDefaultBrowser',
                                url: e.target.href
                            }}));
                        }} else {{
                            window.open(e.target.href, '_blank');
                        }}
                    }} catch (error) {{
                        window.open(e.target.href, '_blank');
                    }}
                }}
            }});
        }});
    </script>
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
        {feedItem.Description}
    </div>
    <div class='read-more'>
        <strong>Full Article (Use 'Show Content' for integrated browser):</strong> 
        <a href='{feedItem.Link}' class='external-link'>{feedItem.Link}</a>
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
                    System.Diagnostics.Debug.WriteLine("Article HTML content updated in WebView.");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error updating article WebView: {ex.Message}");
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
                                    System.Diagnostics.Debug.WriteLine($"Article height adjusted to: {adjustedHeight}px (content: {contentHeight}px)");
                                });
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error adjusting article height: {ex.Message}");
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
                        System.Diagnostics.Debug.WriteLine($"Showing external content in integrated browser: {SelectedArticleLink}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error navigating: {ex.Message}");
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
                    System.Diagnostics.Debug.WriteLine("Hiding integrated browser content.");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Browser stays visible due to Always-On setting.");
                }
            }
        }

        private void ShowAdBlockerSettings()
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
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Information);

                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    _ = _browserService.UpdateAdBlockerListsAsync();
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
            System.Diagnostics.Debug.WriteLine("Browser cleared on startup.");
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
            System.Diagnostics.Debug.WriteLine($"Saved category expanded states for {expandedStates.Count} categories.");
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
            System.Diagnostics.Debug.WriteLine($"Saved feed expanded states for {expandedStates.Count} feeds.");
            return expandedStates;
        }

        private async Task RefreshFeedsAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Refreshing feeds with category support. Found {_feeds.Count} feeds, using MaxFeeds: {MaxFeeds}.");
                if (_feeds.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("No feeds configured. Please add feeds first.");
                    CategoryGroups.Clear();
                    return;
                }

                var categoryExpandedStates = _settings.CategoryExpandedStates ?? new Dictionary<string, bool>();
                var feedExpandedStates = _settings.TreeViewExpandedStates ?? new Dictionary<string, bool>();

                var items = await _feedService.FetchArticlesAsync(_feeds, Keyword, MaxFeeds);
                System.Diagnostics.Debug.WriteLine($"Fetched {items.Count} articles total (limited to {MaxFeeds} per feed).");

                var categorizedItems = items.GroupBy(item =>
                {
                    var feed = _feeds.FirstOrDefault(f => f.Name == item.FeedName);
                    return feed?.Category ?? "Default";
                });

                CategoryGroups.Clear();

                foreach (var categoryGroup in categorizedItems)
                {
                    var categoryViewModel = new CategoryGroupViewModel
                    {
                        Name = categoryGroup.Key,
                        IsExpanded = categoryExpandedStates.TryGetValue(categoryGroup.Key, out var expanded) ? expanded : true
                    };

                    var feedGroups = categoryGroup.GroupBy(item => item.FeedName);

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
                    System.Diagnostics.Debug.WriteLine($"Added category '{categoryGroup.Key}' with {categoryViewModel.Feeds.Count} feeds.");
                }

                OnPropertyChanged(nameof(CategoryGroups));
                OnPropertyChanged(nameof(CurrentFeedSettingsDisplay));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error refreshing feeds: {ex.Message}");
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

                System.Diagnostics.Debug.WriteLine($"🔄 Forcing feed refresh with MaxFeeds: {_maxFeeds}");
                _ = RefreshFeedsAsync();

                System.Diagnostics.Debug.WriteLine($"Feed settings saved. MaxFeeds: {_maxFeeds}, Always-On: {_isShowContentAlwaysOn}");

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
                System.Diagnostics.Debug.WriteLine($"Error saving feed settings: {ex.Message}");
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

                    System.Diagnostics.Debug.WriteLine("Feed settings reset to defaults");

                    System.Windows.MessageBox.Show(
                        "Feed settings have been reset to default values.",
                        "Settings Reset",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error resetting feed settings: {ex.Message}");
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

                    System.Diagnostics.Debug.WriteLine("Window heights reset to dynamic screen-based equal sizing");

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
                System.Diagnostics.Debug.WriteLine($"Error resetting window heights: {ex.Message}");
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

                System.Diagnostics.Debug.WriteLine($"Screen work area height: {availableHeight}px");

                var usableHeight = availableHeight - 150;
                var halfHeight = usableHeight / 2;

                var articleHeight = Math.Max(200, Math.Min(halfHeight, 600));
                var browserHeight = articleHeight;

                System.Diagnostics.Debug.WriteLine($"Calculated equal window heights - Article: {articleHeight}px, Browser: {browserHeight}px");

                return (Math.Floor(articleHeight), Math.Floor(browserHeight));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error calculating window sizes: {ex.Message}");
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

                System.Diagnostics.Debug.WriteLine($"Applied dynamic equal window sizing - Article: {articleHeight}px, Browser: {browserHeight}px");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("User has custom heights, skipping dynamic sizing");
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

                System.Diagnostics.Debug.WriteLine($"✅ Auto-refresh timer started: {AutoRefreshIntervalMinutes} minutes");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error starting auto-refresh timer: {ex.Message}");
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
                    System.Diagnostics.Debug.WriteLine("🛑 Auto-refresh timer stopped");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error stopping auto-refresh timer: {ex.Message}");
            }
        }

        private void RestartAutoRefreshTimer()
        {
            if (AutoRefresh)
            {
                StartAutoRefreshTimer();
                System.Diagnostics.Debug.WriteLine($"🔄 Auto-refresh timer restarted with new interval: {AutoRefreshIntervalMinutes} minutes");
            }
        }

        private async void AutoRefreshTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"🔄 Auto-refresh triggered (every {AutoRefreshIntervalMinutes} minutes)");
                await RefreshFeedsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error during auto-refresh: {ex.Message}");
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

                System.Diagnostics.Debug.WriteLine("✅ Cache cleanup timer started (every 2 hours)");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error starting cache cleanup timer: {ex.Message}");
            }
        }

        private async Task AutoCacheCleanup()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("🧹 Auto cache cleanup started");
                await _browserService.ClearOldCacheAsync(6);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error in auto cache cleanup: {ex.Message}");
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
                System.Diagnostics.Debug.WriteLine($"❌ Error clearing cache: {ex.Message}");
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
                System.Diagnostics.Debug.WriteLine("=== ADBLOCKER DIAGNOSTIC ===");

                System.Diagnostics.Debug.WriteLine($"AdBlocker Enabled Setting: {AdBlockerEnabled}");

                var stats = _browserService.GetAdBlockerStats();
                System.Diagnostics.Debug.WriteLine($"Blocked Domains Count: {stats.domains}");
                System.Diagnostics.Debug.WriteLine($"Blocked Patterns Count: {stats.patterns}");

                var detailedStats = _browserService.GetDetailedAdBlockerStats();
                System.Diagnostics.Debug.WriteLine($"Last Update: {detailedStats.lastUpdate}");
                System.Diagnostics.Debug.WriteLine($"Available Lists: {detailedStats.availableLists.Count}");
                System.Diagnostics.Debug.WriteLine($"Failed Lists: {detailedStats.failedLists.Count}");

                if (stats.domains == 0)
                {
                    System.Diagnostics.Debug.WriteLine("❌ CRITICAL: No domains loaded in AdBlocker!");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"✅ AdBlocker has {stats.domains} domains loaded");
                }

                System.Diagnostics.Debug.WriteLine("=== DIAGNOSTIC COMPLETE ===");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error diagnosing AdBlocker: {ex.Message}");
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

                System.Diagnostics.Debug.WriteLine("✅ MainViewModel cleanup completed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error during cleanup: {ex.Message}");
            }
        }

        private void ShowAboutWindow()
        {
            try
            {
                var aboutWindow = new AboutWindow();
                aboutWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing About window: {ex.Message}");
            }
        }
    }
}