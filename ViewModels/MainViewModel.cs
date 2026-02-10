using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Net;
using System.Windows;
using System.Windows.Threading;
using System.Diagnostics;
using HtmlAgilityPack;
using MyNewsFeeder.Models;
using MyNewsFeeder.Services;
using MyNewsFeeder.Views;
using MaterialDesignThemes.Wpf;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MyNewsFeeder.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public event Action<string> SelectionRestoreRequested;
        public Func<double> RequestTreeScrollOffset;
        public event Action<double> ScrollOffsetRestoreRequested;

        private readonly FeedService _feedService;
        private readonly SettingsService _settingsService;
        private readonly BrowserService _browserService;
        private readonly List<CategoryGroupViewModel> _allCategoryGroups = new List<CategoryGroupViewModel>();
        private List<FeedGroupViewModel> _pinnedSourceFeeds = new List<FeedGroupViewModel>();
        private List<FeedGroupViewModel> _readLaterSourceFeeds = new List<FeedGroupViewModel>();
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
        private int _pendingAutoRefreshIntervalMinutes = 10;
        private bool _pendingAutoRefresh;
        private double _pendingTreeWidth;
        private HashSet<string> _readArticleLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _pinnedArticleLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _readLaterArticleLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _suppressSelectionDuringRefresh;
        private double _lastTreeScrollOffset;
        private System.Windows.Threading.DispatcherTimer _autoRefreshTimer;
        private System.Windows.Threading.DispatcherTimer _cacheCleanupTimer;
        private string _copyLinkButtonText = "Copy Link";
        private ArticleSectionViewModel _pinnedSection;
        private ArticleSectionViewModel _readLaterSection;
        private ArticleSectionViewModel _myFeedsSection;
        private bool _sectionRefreshPending;
        private const int MaxExtraPerFeed = 50;
        private bool _refreshQueued;
        private double _lastInlineMoveOffset;
        private FeedItem _currentSelectedItem;
        private string _lastMyFeedsSelectedLink;
        private bool _isLoading;
        private bool _suppressAutoScroll;
        private DateTime _nextAutoRefreshTime;
        private System.Windows.Threading.DispatcherTimer _autoRefreshCountdownTimer;
        private string _nextAutoRefreshDisplay;
        private bool _useCompactArticleCards;
        private bool _pendingDarkMode;
        private bool _pendingAutoUpdateCheck;
        private bool _autoUpdatePromptedThisSession;
        private ArticleSectionViewModel _selectedSection;
        private string _treeFilterText;
        private bool _autoUpdateEnabledCached;
        private CoreWebView2Environment _sharedEnvironment;
        public ObservableCollection<SelectableFilterItem> FilterCategories { get; }
        public ObservableCollection<SelectableFilterItem> FilterFeeds { get; }
        private readonly List<FeedGroupViewModel> _sectionFilteredFeedsBuffer = new List<FeedGroupViewModel>();
        private readonly DispatcherTimer _filterDebounceTimer = new DispatcherTimer();
        private static readonly HashSet<string> AllowedHtmlTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "p", "br", "strong", "em", "b", "i", "ul", "ol", "li", "blockquote", "a", "img", "span", "div", "code", "pre"
        };

        private readonly Dictionary<string, HashSet<string>> _savedCategoryFiltersBySection = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HashSet<string>> _savedFeedFiltersBySection = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _savedTextFiltersBySection = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HashSet<string>> _savedKnownCategoriesBySection = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HashSet<string>> _savedKnownFeedsBySection = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private bool _suppressFilterStateSave;
        private readonly Dictionary<ArticleSectionViewModel, bool> _sectionNeedsFilterApply = new Dictionary<ArticleSectionViewModel, bool>();
        private bool _externalPromptInProgress;
        private bool _externalPromptHandled;

        private static readonly HashSet<string> SelfClosingHtmlTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "br", "img", "hr"
        };

        private static readonly Dictionary<string, HashSet<string>> AllowedTagAttributes = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "href", "title", "rel" },
            ["img"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "src", "alt", "title", "width", "height" },
            ["span"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "class" },
            ["div"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "class" }
        };

        private static readonly string[] BlockedElementNames =
        {
            "script", "style", "iframe", "object", "embed", "form", "input", "button"
        };
        public SnackbarMessageQueue SnackbarMessageQueue { get; }

        // Window height properties with persisten
        private double _articleWindowHeight = 350;
        private double _browserWindowHeight = 350;

        public int[] AvailableRefreshIntervals => AppSettings.AvailableRefreshIntervals;

        public ObservableCollection<CategoryGroupViewModel> CategoryGroups { get; set; }
        public ObservableCollection<ArticleSectionViewModel> ArticleSections { get; set; }
        public ArticleSectionViewModel SelectedSection
        {
            get => _selectedSection;
            set
            {
                if (_selectedSection != value)
                {
                    // If leaving My Feeds, remember the current article.
                    if (_selectedSection == _myFeedsSection && _currentSelectedItem != null && !string.IsNullOrWhiteSpace(_currentSelectedItem.Link))
                    {
                        _lastMyFeedsSelectedLink = _currentSelectedItem.Link.Trim();
                    }

                    _selectedSection = value;
                    OnPropertyChanged(nameof(SelectedSection));
                    OnPropertyChanged(nameof(VisibleSections));
                    RestoreSectionFilterText(_selectedSection);
                    if (FilterCategories != null && FilterFeeds != null)
                    {
                        RebuildFilterListsForSection(_selectedSection, resetSelections: false);
                        ApplyFilterIfNeeded();
                        if (_selectedSection == _myFeedsSection)
                        {
                            TryRestoreMyFeedsSelection();
                        }
                    }
                }
            }
        }
        public IEnumerable<ArticleSectionViewModel> VisibleSections
        {
            get
            {
                if (_selectedSection != null)
                {
                    return new[] { _selectedSection };
                }
                return ArticleSections;
            }
        }
        public ICommand CopyLinkCommand { get; }
        public ICommand CopyArticleLinkCommand { get; }
        public ICommand MarkUnreadCommand { get; }
        public ICommand PinArticleCommand { get; }
        public ICommand ReadLaterArticleCommand { get; }
        public ICommand ArticleClickCommand { get; }
        public ICommand NavigateSelectionCommand { get; }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged(nameof(IsLoading));
                    OnPropertyChanged(nameof(IsNotLoading));
                }
            }
        }

        public bool IsNotLoading => !_isLoading;

        public bool SuppressAutoScroll
        {
            get => _suppressAutoScroll;
            set
            {
                if (_suppressAutoScroll != value)
                {
                    _suppressAutoScroll = value;
                    OnPropertyChanged(nameof(SuppressAutoScroll));
                }
            }
        }

        public bool UseCompactArticleCards
        {
            get => _useCompactArticleCards;
            set
            {
                if (_useCompactArticleCards != value)
                {
                    _useCompactArticleCards = value;
                    _settings.UseCompactArticleCards = value;
                    OnPropertyChanged(nameof(UseCompactArticleCards));
                    _settingsService.SaveSettings(_settings);
                }
            }
        }

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

        // Used by the view to ignore selection changes while a refresh rebuilds the tree.
        public bool SuppressSelectionDuringRefresh => _suppressSelectionDuringRefresh;

        private void PersistReadState()
        {
            try
            {
                _settings.ReadArticleLinks = new HashSet<string>(_readArticleLinks, StringComparer.OrdinalIgnoreCase);
                _settingsService.SaveSettings(_settings);
            }
            catch (Exception)
            {
                // Ignore persistence failures; read state will be rebuilt next successful save.
            }
        }

        private void PersistPinnedAndSavedLists()
        {
            try
            {
                _settings.PinnedArticleLinks = new HashSet<string>(_pinnedArticleLinks, StringComparer.OrdinalIgnoreCase);
                _settings.ReadLaterArticleLinks = new HashSet<string>(_readLaterArticleLinks, StringComparer.OrdinalIgnoreCase);
                _settings.PinnedArticleSnapshots = new List<FeedItem>(_settings.PinnedArticleSnapshots ?? new List<FeedItem>());
                _settings.ReadLaterArticleSnapshots = new List<FeedItem>(_settings.ReadLaterArticleSnapshots ?? new List<FeedItem>());
                _settingsService.SaveSettings(_settings);
            }
            catch (Exception)
            {
                // Ignore persistence failures; lists will be refreshed on next save.
            }
        }

        public bool DarkMode => _settings.DarkMode;

        public bool PendingDarkMode
        {
            get => _pendingDarkMode;
            set
            {
                if (_pendingDarkMode != value)
                {
                    _pendingDarkMode = value;
                    OnPropertyChanged(nameof(PendingDarkMode));
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

        private void ApplyActiveDarkMode()
        {
            ApplyTheme();
            _browserService.SetDarkMode(_settings.DarkMode);
            ApplyArticleWebViewTheme();
            RefreshCurrentArticleHtml();
        }

        private void NavigateCurrentArticleInBrowser()
        {
            if (string.IsNullOrWhiteSpace(SelectedArticleLink))
            {
                return;
            }

            _browserService.NavigateWithClear(SelectedArticleLink);
        }

        public void SetAutoUpdatePreference(bool enabled)
        {
            _settings.AutoUpdateCheckEnabled = enabled;
            _pendingAutoUpdateCheck = enabled;
            _autoUpdateEnabledCached = enabled;
            _settingsService.SaveSettings(_settings);
        }

        public async Task<CoreWebView2Environment> GetSharedWebViewEnvironmentAsync()
        {
            if (_sharedEnvironment != null) return _sharedEnvironment;

            var userDataFolder = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MyNewsFeeder",
                "WebView2Cache");

            System.IO.Directory.CreateDirectory(userDataFolder);

            _sharedEnvironment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            return _sharedEnvironment;
        }

        public bool AutoRefresh
        {
            get => _settings.AutoRefresh;
        }
        public bool PendingAutoRefresh
        {
            get => _pendingAutoRefresh;
            set
            {
                if (_pendingAutoRefresh != value)
                {
                    _pendingAutoRefresh = value;
                    OnPropertyChanged(nameof(PendingAutoRefresh));
                }
            }
        }
        public string NextAutoRefreshDisplay
        {
            get => _nextAutoRefreshDisplay;
            private set
            {
                if (_nextAutoRefreshDisplay != value)
                {
                    _nextAutoRefreshDisplay = value;
                    OnPropertyChanged(nameof(NextAutoRefreshDisplay));
                }
            }
        }

        public bool AutoUpdateCheckEnabled => _settings.AutoUpdateCheckEnabled;

        public bool PendingAutoUpdateCheck
        {
            get => _pendingAutoUpdateCheck;
            set
            {
                if (_pendingAutoUpdateCheck != value)
                {
                    _pendingAutoUpdateCheck = value;
                    OnPropertyChanged(nameof(PendingAutoUpdateCheck));
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
        public bool AdvertisementFilterEnabled
        {
            get => _settings.AdvertisementFilterEnabled;
            set
            {
                if (_settings.AdvertisementFilterEnabled == value)
                {
                    return;
                }

                _settings.AdvertisementFilterEnabled = value;
                OnPropertyChanged(nameof(AdvertisementFilterEnabled));
                _settingsService.SaveSettings(_settings);
            }
        }

        public string AdvertisementKeywordsText
        {
            get
            {
                var keywords = _settings.AdvertisementKeywords ?? new List<string>();
                return string.Join(Environment.NewLine, keywords);
            }
            set
            {
                var parsedKeywords = ParseAdvertisementKeywords(value);
                _settings.AdvertisementKeywords = parsedKeywords;
                OnPropertyChanged(nameof(AdvertisementKeywordsText));
                _settingsService.SaveSettings(_settings);
            }
        }

        public double PendingTreeWidth
        {
            get => _pendingTreeWidth;
            set
            {
                if (Math.Abs(_pendingTreeWidth - value) > 0.1)
                {
                    _pendingTreeWidth = value;
                    OnPropertyChanged(nameof(PendingTreeWidth));
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

                _browserService.SetDarkMode(_settings.DarkMode);

                if (!string.IsNullOrEmpty(SelectedArticleHtml))
                {
                    UpdateArticleWebView();
                }

            }
        }

        public int PendingAutoRefreshIntervalMinutes
        {
            get => _pendingAutoRefreshIntervalMinutes;
            set
            {
                if (_pendingAutoRefreshIntervalMinutes != value)
                {
                    _pendingAutoRefreshIntervalMinutes = value;
                    OnPropertyChanged(nameof(PendingAutoRefreshIntervalMinutes));
                    OnPropertyChanged(nameof(AutoRefreshIntervalDisplay));
                }
            }
        }

        public string AutoRefreshIntervalDisplay
        {
            get
            {
                if (PendingAutoRefreshIntervalMinutes < 60)
                {
                    return $"Every {PendingAutoRefreshIntervalMinutes} minutes";
                }
                else
                {
                    var hours = PendingAutoRefreshIntervalMinutes / 60;
                    var minutes = PendingAutoRefreshIntervalMinutes % 60;
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

        public string TreeFilterText
        {
            get => _treeFilterText;
            set
            {
                if (_treeFilterText != value)
                {
                    _treeFilterText = value;
                    OnPropertyChanged(nameof(TreeFilterText));
                    if (!_suppressFilterStateSave)
                    {
                        SaveCurrentSectionFilterState();
                    }
                    MarkSectionDirty(_selectedSection ?? _myFeedsSection);
                    ScheduleFilterApply();
                }
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
        public ICommand SelectSectionCommand { get; }
        public ICommand SelectAllCategoriesCommand { get; }
        public ICommand ClearCategoriesCommand { get; }
        public ICommand SelectAllFeedsCommand { get; }
        public ICommand ClearFeedsCommand { get; }

        public MainViewModel(FeedService feedService,
            SettingsService settingsService,
            BrowserService browserService)
        {
            _feedService = feedService;
            _settingsService = settingsService;
            _browserService = browserService;
            SnackbarMessageQueue = new SnackbarMessageQueue(TimeSpan.FromSeconds(4));

            try
            {
                _settings = _settingsService.LoadSettings();
                if (_settings == null)
                {
                    _settings = new AppSettings();
                }
                _autoUpdateEnabledCached = _settings.AutoUpdateCheckEnabled;
                _pendingDarkMode = _settings.DarkMode;
                _pendingAutoRefresh = _settings.AutoRefresh;
                _pendingAutoRefreshIntervalMinutes = _settings.AutoRefreshIntervalMinutes > 0 ? _settings.AutoRefreshIntervalMinutes : 10;
                _pendingTreeWidth = _settings.TreeWidth;
                _pendingAutoUpdateCheck = _settings.AutoUpdateCheckEnabled;
                _readArticleLinks = new HashSet<string>(_settings.ReadArticleLinks ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                _pinnedArticleLinks = new HashSet<string>(_settings.PinnedArticleLinks ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                _readLaterArticleLinks = new HashSet<string>(_settings.ReadLaterArticleLinks ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                _settings = new AppSettings();
                _pendingDarkMode = _settings.DarkMode;
                _pendingAutoRefresh = _settings.AutoRefresh;
                _pendingAutoRefreshIntervalMinutes = _settings.AutoRefreshIntervalMinutes > 0 ? _settings.AutoRefreshIntervalMinutes : 10;
                _pendingTreeWidth = _settings.TreeWidth;
                _pendingAutoUpdateCheck = _settings.AutoUpdateCheckEnabled;
                _autoUpdateEnabledCached = _settings.AutoUpdateCheckEnabled;
            }

            _filterDebounceTimer.Interval = TimeSpan.FromMilliseconds(120);
            _filterDebounceTimer.Tick += (_, __) =>
            {
                _filterDebounceTimer.Stop();
                ApplyTreeFilter();
            };

            if (_settings.AdvertisementKeywords == null || _settings.AdvertisementKeywords.Count == 0)
            {
                _settings.AdvertisementKeywords = new List<string>(AppSettings.DefaultAdvertisementKeywords);
            }

            FilterCategories = new ObservableCollection<SelectableFilterItem>();
            FilterFeeds = new ObservableCollection<SelectableFilterItem>();
            FilterCategories.CollectionChanged += (_, __) => ScheduleFilterApply();
            FilterFeeds.CollectionChanged += (_, __) => ScheduleFilterApply();

            if (_settings.SectionExpandedStates == null)
            {
                _settings.SectionExpandedStates = new Dictionary<string, bool>();
            }

            PendingTreeWidth = _settings.TreeWidth;
            try
            {
                _feeds = FeedService.NormalizeAndFilterFeeds(_settingsService.LoadFeeds());
            }
            catch (Exception)
            {
                _feeds = new List<Feed>();
            }

            CategoryGroups = new ObservableCollection<CategoryGroupViewModel>();
            ArticleSections = new ObservableCollection<ArticleSectionViewModel>();
            _pinnedSection = new ArticleSectionViewModel
            {
                Name = "Pinned",
                IconKind = "PinOutline",
                IsExpanded = _settings.SectionExpandedStates.TryGetValue("Pinned", out var pinnedExpanded) ? pinnedExpanded : true,
                HideUnreadIndicators = true
            };
            _readLaterSection = new ArticleSectionViewModel
            {
                Name = "Read Later",
                IconKind = "ClockOutline",
                IsExpanded = _settings.SectionExpandedStates.TryGetValue("Read Later", out var readLaterExpanded) ? readLaterExpanded : true,
                HideUnreadIndicators = true
            };
            _myFeedsSection = new ArticleSectionViewModel
            {
                Name = "My Feeds",
                IconKind = "Rss",
                IsExpanded = _settings.SectionExpandedStates.TryGetValue("My Feeds", out var myFeedsExpanded) ? myFeedsExpanded : true,
                HideUnreadIndicators = false
            };
            ArticleSections.Add(_pinnedSection);
            ArticleSections.Add(_readLaterSection);
            ArticleSections.Add(_myFeedsSection);
            _pinnedSection.PropertyChanged += SectionOnPropertyChanged;
            _readLaterSection.PropertyChanged += SectionOnPropertyChanged;
            _myFeedsSection.PropertyChanged += SectionOnPropertyChanged;
            _sectionNeedsFilterApply[_pinnedSection] = true;
            _sectionNeedsFilterApply[_readLaterSection] = true;
            _sectionNeedsFilterApply[_myFeedsSection] = true;
            SelectedSection = _myFeedsSection;
            Keyword = _settings.KeywordFilter ?? string.Empty;
            _maxFeeds = _settings.MaxFeeds > 0 ? _settings.MaxFeeds : 10;
            IsShowContentAlwaysOn = _settings.IsShowContentAlwaysOn;
            _useCompactArticleCards = _settings.UseCompactArticleCards;
            OnPropertyChanged(nameof(UseCompactArticleCards));

            try
            {
                if (_settings.AutoRefreshIntervalMinutes <= 0)
                {
                    _settings.AutoRefreshIntervalMinutes = 10;
                }
                _pendingAutoRefreshIntervalMinutes = _settings.AutoRefreshIntervalMinutes;
            }
            catch (Exception)
            {
                _settings.AutoRefreshIntervalMinutes = 10;
                _pendingAutoRefreshIntervalMinutes = 10;
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
            BrowserReloadCommand = new RelayCommand(_ => NavigateCurrentArticleInBrowser(),
                                                    _ => !string.IsNullOrWhiteSpace(SelectedArticleLink));
            AutoAdjustHeightCommand = new RelayCommand(async _ => await AdjustArticleHeightAsync());
            SaveSettingsCommand = new RelayCommand(_ => SaveFeedSettings());
            ResetSettingsCommand = new RelayCommand(_ => ResetFeedSettings());
            ResetWindowHeightsCommand = new RelayCommand(_ => ResetWindowHeights());
            ClearCacheCommand = new RelayCommand(async _ => await ClearBrowserCacheAsync());
            AboutCommand = new RelayCommand(_ => ShowAboutWindow());
            SelectSectionCommand = new RelayCommand(param =>
            {
                if (param is ArticleSectionViewModel section)
                {
                    SelectedSection = section;
                }
            });
            SelectAllCategoriesCommand = new RelayCommand(_ => SetFilterSelection(FilterCategories, true));
            ClearCategoriesCommand = new RelayCommand(_ => SetFilterSelection(FilterCategories, false));
            SelectAllFeedsCommand = new RelayCommand(_ => SetFilterSelection(FilterFeeds, true));
            ClearFeedsCommand = new RelayCommand(_ => SetFilterSelection(FilterFeeds, false));

            // ** Initialize CopyLink command **
            CopyLinkCommand = new RelayCommand(async _ => await CopyLinkAsync(),
                                           _ => !string.IsNullOrEmpty(SelectedArticleLink));
            CopyArticleLinkCommand = new RelayCommand(param => CopyLinkFromItem(param as FeedItem));
            MarkUnreadCommand = new RelayCommand(param => MarkAsUnread(param as FeedItem));
            PinArticleCommand = new RelayCommand(param => PinArticle(param as FeedItem));
            ReadLaterArticleCommand = new RelayCommand(param => AddToReadLater(param as FeedItem));
            ArticleClickCommand = new RelayCommand(param => OnArticleSelected(param as FeedItem), param => param is FeedItem);
            NavigateSelectionCommand = new RelayCommand(param => MoveSelection(param), _ => true);

            ApplyActiveDarkMode();
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

            PromptAutoUpdateIfNeeded();
            _ = CheckForUpdatesAsync();

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

        private void OnArticleNewWindowRequested(object sender, Microsoft.Web.WebView2.Core.CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            if (_externalPromptHandled)
            {
                return;
            }
            _externalPromptHandled = true;
            TryPromptOpenExternal(e.Uri);
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
            if (_externalPromptHandled)
            {
                _externalPromptHandled = false;
                return;
            }

            _externalPromptHandled = true;
            TryPromptOpenExternal(targetUri);
        }

        private void OpenInDefaultBrowser(string url)
        {
            TryPromptOpenExternal(url);
        }

        public void OnArticleSelected(FeedItem feedItem, bool enableAutoScroll = true)
        {
            // Selecting explicitly re-enables auto-scroll unless explicitly suppressed.
            if (enableAutoScroll)
            {
                SuppressAutoScroll = false;
            }
            if (!ReferenceEquals(_currentSelectedItem, feedItem))
            {
                if (_currentSelectedItem != null)
                {
                    _currentSelectedItem.IsSelected = false;
                }
                _currentSelectedItem = feedItem;
                if (_currentSelectedItem != null)
                {
                    _currentSelectedItem.IsSelected = true;
                }
            }

            var htmlContent = CreateArticleHtml(feedItem);
            SelectedArticleHtml = htmlContent;
            SelectedArticleText = BuildArticlePlainText(feedItem.Title, feedItem.Description);
            SelectedArticleLink = NormalizeExternalLink(feedItem.Link);
            var sectionForItem = FindSectionForItem(feedItem);
            if (sectionForItem == _myFeedsSection && !string.IsNullOrWhiteSpace(feedItem.Link))
            {
                _lastMyFeedsSelectedLink = feedItem.Link.Trim();
            }

            if (feedItem != null && !feedItem.IsRead)
            {
                feedItem.IsRead = true;
                var key = feedItem.Link?.Trim();
                if (!string.IsNullOrWhiteSpace(key))
                {
                    _readArticleLinks.Add(key);
                    PersistReadState();
                }
            }

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

        private List<FeedItem> BuildVisibleArticleList(ArticleSectionViewModel section)
        {
            if (section == null)
            {
                return new List<FeedItem>();
            }

            var result = new List<FeedItem>();

            foreach (var child in section.Items)
            {
                if (child is CategoryGroupViewModel category)
                {
                    if (!category.IsExpanded)
                    {
                        category.IsExpanded = true;
                    }

                    foreach (var feed in category.Feeds)
                    {
                        if (!feed.IsExpanded)
                        {
                            feed.IsExpanded = true;
                        }

                        foreach (var obj in feed.PagedItems)
                        {
                            if (obj is FeedItem item)
                            {
                                result.Add(item);
                            }
                        }
                    }
                }
                else if (child is FeedGroupViewModel feedGroup)
                {
                    if (!feedGroup.IsExpanded)
                    {
                        feedGroup.IsExpanded = true;
                    }

                    foreach (var obj in feedGroup.PagedItems)
                    {
                        if (obj is FeedItem item)
                        {
                            result.Add(item);
                        }
                    }
                }
            }

            return result;
        }

        private void ClearArticleSelection()
        {
            if (_currentSelectedItem != null)
            {
                _currentSelectedItem.IsSelected = false;
                _currentSelectedItem = null;
            }

            SelectedArticleHtml = string.Empty;
            SelectedArticleText = string.Empty;
            SelectedArticleLink = string.Empty;
            ClearBrowserContent();
            IsBrowserVisible = false;
        }

        private ArticleSectionViewModel FindSectionForItem(FeedItem item)
        {
            if (item == null)
            {
                return null;
            }

            foreach (var section in ArticleSections)
            {
                foreach (var child in section.Items)
                {
                    if (child is CategoryGroupViewModel category)
                    {
                        foreach (var feed in category.Feeds)
                        {
                            if (feed.Items.Contains(item))
                            {
                                return section;
                            }
                        }
                    }
                    else if (child is FeedGroupViewModel feedGroup)
                    {
                        if (feedGroup.Items.Contains(item))
                        {
                            return section;
                        }
                    }
                }
            }

            return null;
        }

        private FeedItem GetNeighborForSelection(FeedItem item, ArticleSectionViewModel sourceSection)
        {
            if (item == null || sourceSection == null)
            {
                return null;
            }

            var list = BuildVisibleArticleList(sourceSection);
            var index = list.FindIndex(f => ReferenceEquals(f, item));
            if (index < 0)
            {
                return null;
            }

            if (index + 1 < list.Count)
            {
                return list[index + 1];
            }

            if (index - 1 >= 0)
            {
                return list[index - 1];
            }

            return null;
        }

        private void MoveSelection(object param)
        {
            int delta = 0;
            if (param is int i)
            {
                delta = i;
            }
            else if (param is string s && int.TryParse(s, out var parsed))
            {
                delta = parsed;
            }

            if (delta == 0)
            {
                return;
            }

            // Determine the section to navigate within
            var targetSection = FindSectionForItem(_currentSelectedItem);
            if (targetSection == null)
            {
                targetSection = ArticleSections.FirstOrDefault(s =>
                    s.Items.OfType<CategoryGroupViewModel>().Any(c => c.Feeds.Any(f => f.Items.Count > 0)) ||
                    s.Items.OfType<FeedGroupViewModel>().Any(f => f.Items.Count > 0));
            }

            var visibleItems = BuildVisibleArticleList(targetSection);
            if (visibleItems.Count == 0)
            {
                return;
            }

            var currentIndex = _currentSelectedItem != null
                ? visibleItems.FindIndex(f => ReferenceEquals(f, _currentSelectedItem))
                : -1;

            var targetIndex = currentIndex + delta;
            if (targetIndex < 0) targetIndex = 0;
            if (targetIndex >= visibleItems.Count) targetIndex = visibleItems.Count - 1;

            if (targetIndex == currentIndex || targetIndex < 0 || targetIndex >= visibleItems.Count)
            {
                return;
            }

            OnArticleSelected(visibleItems[targetIndex]);
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
            if (_articleWebView == null)
            {
                return;
            }

            try
            {
                if (_articleWebView.CoreWebView2 == null)
                {
                    _articleWebView.CoreWebView2InitializationCompleted -= ArticleWebView_CoreWebView2InitializationCompleted;
                    _articleWebView.CoreWebView2InitializationCompleted += ArticleWebView_CoreWebView2InitializationCompleted;
                    _ = _articleWebView.EnsureCoreWebView2Async();
                    return;
                }

                if (string.IsNullOrEmpty(SelectedArticleHtml))
                {
                    _articleWebView.NavigateToString("<html><body></body></html>");
                    return;
                }

                NavigateToCurrentArticleHtml();
            }
            catch (Exception)
            {
                // Swallow to avoid UI disruption; navigation failures are non-critical.
            }
        }

        private void ArticleWebView_CoreWebView2InitializationCompleted(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            _articleWebView.CoreWebView2InitializationCompleted -= ArticleWebView_CoreWebView2InitializationCompleted;
            if (e.IsSuccess)
            {
                NavigateToCurrentArticleHtml();
            }
        }

        private void NavigateToCurrentArticleHtml()
        {
            if (_articleWebView?.CoreWebView2 == null || string.IsNullOrEmpty(SelectedArticleHtml))
            {
                return;
            }

            _articleWebView.NavigateToString(SelectedArticleHtml);
            _articleWebView.CoreWebView2.NavigationCompleted -= ArticleWebView_NavigationCompleted;
            _articleWebView.CoreWebView2.NavigationCompleted += ArticleWebView_NavigationCompleted;
        }

        private async void ArticleWebView_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                await Task.Delay(500);
                await AdjustArticleHeightAsync();
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
            var feedManagerViewModel = new FeedManagerViewModel(_settingsService, _feedService);
            var feedManagerWindow = new FeedManagerWindow
            {
                DataContext = feedManagerViewModel,
                Owner = Application.Current.MainWindow
            };
            feedManagerWindow.ShowDialog();

            if (feedManagerViewModel.WasClosedBySave)
            {
                // Reload settings to pick up new Categories and expanded states
                _settings = _settingsService.LoadSettings();
                _readArticleLinks = new HashSet<string>(_settings.ReadArticleLinks ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                _pendingDarkMode = _settings.DarkMode;
                _pendingAutoRefresh = _settings.AutoRefresh;
                _pendingAutoRefreshIntervalMinutes = _settings.AutoRefreshIntervalMinutes > 0 ? _settings.AutoRefreshIntervalMinutes : 10;
                _pendingTreeWidth = _settings.TreeWidth;
                _isShowContentAlwaysOn = _settings.IsShowContentAlwaysOn;
                OnPropertyChanged(nameof(DarkMode));
                OnPropertyChanged(nameof(PendingDarkMode));
                OnPropertyChanged(nameof(PendingAutoRefresh));
                OnPropertyChanged(nameof(PendingAutoRefreshIntervalMinutes));
                OnPropertyChanged(nameof(PendingTreeWidth));
                OnPropertyChanged(nameof(IsShowContentAlwaysOn));
                if (_settings.AdvertisementKeywords == null || _settings.AdvertisementKeywords.Count == 0)
                {
                    _settings.AdvertisementKeywords = new List<string>(AppSettings.DefaultAdvertisementKeywords);
                }

                OnPropertyChanged(nameof(AdvertisementFilterEnabled));
                OnPropertyChanged(nameof(AdvertisementKeywordsText));

                // Reload feeds and refresh the tree
                _feeds = _settingsService.LoadFeeds();
                _ = RefreshFeedsAsync();
            }
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

        private void SectionOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ArticleSectionViewModel.IsExpanded))
            {
                SaveSectionExpandedStates();
            }
        }

        public Dictionary<string, bool> SaveSectionExpandedStates()
        {
            var expandedStates = new Dictionary<string, bool>();
            foreach (var section in ArticleSections)
            {
                expandedStates[section.Name] = section.IsExpanded;
            }

            _settings.SectionExpandedStates = expandedStates;
            _settingsService.SaveSettings(_settings);
            return expandedStates;
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

        private static List<string> ParseAdvertisementKeywords(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new List<string>();
            }

            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var segments = value.Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var segment in segments)
            {
                var trimmed = segment.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (seen.Add(trimmed))
                {
                    result.Add(trimmed);
                }
            }

            return result;
        }

        private List<string> GetAdvertisementKeywordsForFiltering()
        {
            var keywords = _settings.AdvertisementKeywords;
            if (keywords == null || keywords.Count == 0)
            {
                return new List<string>();
            }

            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var keyword in keywords)
            {
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    continue;
                }

                var trimmed = keyword.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (seen.Add(trimmed))
                {
                    result.Add(trimmed);
                }
            }

            return result;
        }

        private void SyncMyFeedsSection()
        {
            if (_myFeedsSection == null)
            {
                return;
            }

            _myFeedsSection.Items.Clear();
            foreach (var category in CategoryGroups)
            {
                _myFeedsSection.Items.Add(category);
            }
            _myFeedsSection.UpdateUnreadCount();
        }

        private void ScheduleSectionSync()
        {
            if (_sectionRefreshPending)
            {
                return;
            }

            _sectionRefreshPending = true;
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    SyncMyFeedsSection();
                    _pinnedSection?.UpdateUnreadCount();
                    _readLaterSection?.UpdateUnreadCount();
                }
                finally
                {
                    _sectionRefreshPending = false;
                }
            }, DispatcherPriority.Background);
        }

        private static int GetSectionMaxPerFeed(ArticleSectionViewModel section)
        {
            if (section == null)
            {
                return 0;
            }

            var feeds = section.Items
                .OfType<CategoryGroupViewModel>()
                .SelectMany(c => c.Feeds)
                .Concat(section.Items.OfType<FeedGroupViewModel>());

            return feeds
                .Select(f => f.Items?.Count ?? 0)
                .DefaultIfEmpty(0)
                .Max();
        }

        private void UpdateSectionsImmediate()
        {
            SyncMyFeedsSection();
            _pinnedSection?.UpdateUnreadCount();
            _readLaterSection?.UpdateUnreadCount();
        }

        private void RefreshSectionsDeferred(double offsetToRestore)
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                UpdateSectionsImmediate();
                RestoreTreeOffset(offsetToRestore);
            }, DispatcherPriority.ContextIdle);
        }

        private void RestoreTreeOffset(double offset)
        {
            if (offset < 0)
            {
                return;
            }

            void Apply()
            {
                ScrollOffsetRestoreRequested?.Invoke(offset);
            }

            Apply();
            Application.Current.Dispatcher.InvokeAsync(Apply, DispatcherPriority.ContextIdle);
            Application.Current.Dispatcher.InvokeAsync(Apply, DispatcherPriority.Render);
            Application.Current.Dispatcher.InvokeAsync(Apply, DispatcherPriority.ApplicationIdle);
        }

        private void QueueSmoothRefresh()
        {
            if (_refreshQueued)
            {
                return;
            }

            _refreshQueued = true;
            Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await RefreshFeedsAsync();
                }
                finally
                {
                    _refreshQueued = false;
                }
            }, DispatcherPriority.Background);
        }

        private static void AddToLookup(Dictionary<string, List<FeedItem>> lookup, FeedItem item)
        {
            if (item == null)
            {
                return;
            }

            var key = item.Link?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            var feedName = string.IsNullOrWhiteSpace(item.FeedName) ? "Feed" : item.FeedName;
            if (!lookup.TryGetValue(feedName, out var list))
            {
                list = new List<FeedItem>();
                lookup[feedName] = list;
            }

            // Skip duplicates (same link) to avoid duplicate-key errors downstream.
            if (list.Any(x => string.Equals(x.Link?.Trim(), key, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            list.Add(item);
        }

        private void AddArticleBackToCategories(FeedItem item)
        {
            if (item == null)
            {
                return;
            }

            var key = item.Link?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            var feedName = string.IsNullOrWhiteSpace(item.FeedName) ? "Feed" : item.FeedName;
            var feedModel = _feeds.FirstOrDefault(f => string.Equals(f.Name, feedName, StringComparison.OrdinalIgnoreCase));
            var categoryName = feedModel?.Category ?? "Default";

            var categoryVm = CategoryGroups.FirstOrDefault(c => string.Equals(c.Name, categoryName, StringComparison.OrdinalIgnoreCase));
            if (categoryVm == null)
            {
                var isExpanded = _settings.CategoryExpandedStates.TryGetValue(categoryName, out var expandedState) ? expandedState : true;
                categoryVm = new CategoryGroupViewModel
                {
                    Name = categoryName,
                    IsExpanded = isExpanded,
                    HideUnreadIndicators = false
                };
                CategoryGroups.Add(categoryVm);
            }

            var feedVm = categoryVm.Feeds.FirstOrDefault(f => string.Equals(f.Name, feedName, StringComparison.OrdinalIgnoreCase));
            if (feedVm == null)
            {
                var isExpanded = _settings.TreeViewExpandedStates.TryGetValue(feedName, out var feedExpanded) ? feedExpanded : true;
                feedVm = new FeedGroupViewModel
                {
                    Name = feedName,
                    Category = categoryName,
                    IsExpanded = isExpanded,
                    HideUnreadIndicators = false
                };
                categoryVm.Feeds.Add(feedVm);
            }

            if (!feedVm.Items.Any(i =>
                    !string.IsNullOrWhiteSpace(i.Link) &&
                    string.Equals(i.Link.Trim(), key, StringComparison.OrdinalIgnoreCase)))
            {
                feedVm.Items.Add(item);
            }

            _myFeedsSection?.UpdateUnreadCount();
        }

        private void UpdateFeedSection(ArticleSectionViewModel section, Dictionary<string, List<FeedItem>> groupedItems)
        {
            if (section == null)
            {
                return;
            }

            var categoryStates = section.Items
                .OfType<CategoryGroupViewModel>()
                .ToDictionary(c => c.Name, c => c.IsExpanded, StringComparer.OrdinalIgnoreCase);
            var feedStates = section.Items
                .OfType<CategoryGroupViewModel>()
                .SelectMany(c => c.Feeds)
                .ToDictionary(f => f.Name, f => f.IsExpanded, StringComparer.OrdinalIgnoreCase);

            section.Items.Clear();

            var orderedFeeds = groupedItems.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            var groupedByCategory = orderedFeeds.GroupBy(ResolveCategoryNameForFeed);

            foreach (var categoryGroup in groupedByCategory.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                var categoryName = categoryGroup.Key;
                var categoryVm = new CategoryGroupViewModel
                {
                    Name = categoryName,
                    IconKind = "FolderMultipleOutline",
                    IsExpanded = categoryStates.TryGetValue(categoryName, out var catExpanded) ? catExpanded : true,
                    HideUnreadIndicators = section.HideUnreadIndicators
                };

                foreach (var feedName in categoryGroup)
                {
                    var feedVm = new FeedGroupViewModel
                    {
                        Name = feedName,
                        Category = categoryName,
                        IsExpanded = feedStates.TryGetValue(feedName, out var feedExpanded) ? feedExpanded : true,
                        HideUnreadIndicators = section.HideUnreadIndicators
                    };

                    UpdateItemsInPlace(feedVm.Items, groupedItems[feedName], _readArticleLinks);
                    categoryVm.Feeds.Add(feedVm);
                }

                section.Items.Add(categoryVm);
            }

            section.UpdateUnreadCount();
            if (section == _pinnedSection)
            {
                _pinnedSourceFeeds = groupedItems
                    .Select(kvp => new FeedGroupViewModel
                    {
                        Name = kvp.Key,
                        Category = ResolveCategoryNameForFeed(kvp.Key),
                        IsExpanded = feedStates.TryGetValue(kvp.Key, out var feedExpanded) ? feedExpanded : true,
                        HideUnreadIndicators = section.HideUnreadIndicators,
                        Items = new ObservableCollection<FeedItem>(kvp.Value)
                    })
                    .ToList();
            }
            else if (section == _readLaterSection)
            {
                _readLaterSourceFeeds = groupedItems
                    .Select(kvp => new FeedGroupViewModel
                    {
                        Name = kvp.Key,
                        Category = ResolveCategoryNameForFeed(kvp.Key),
                        IsExpanded = feedStates.TryGetValue(kvp.Key, out var feedExpanded) ? feedExpanded : true,
                        HideUnreadIndicators = section.HideUnreadIndicators,
                        Items = new ObservableCollection<FeedItem>(kvp.Value)
                    })
                    .ToList();
            }
        }

        private string ResolveCategoryNameForFeed(string feedName)
        {
            if (string.IsNullOrWhiteSpace(feedName))
            {
                return "Default";
            }

            var match = _feeds?.FirstOrDefault(f => string.Equals(f.Name, feedName, StringComparison.OrdinalIgnoreCase));
            var category = match?.Category;
            if (string.IsNullOrWhiteSpace(category))
            {
                return "Default";
            }

            return category;
        }

        private void RemoveArticleFromSectionFeeds(ArticleSectionViewModel section, string key)
        {
            if (section == null || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            foreach (var category in section.Items.OfType<CategoryGroupViewModel>().ToList())
            {
                foreach (var feed in category.Feeds.ToList())
                {
                    var toRemove = feed.Items.FirstOrDefault(i =>
                        !string.IsNullOrWhiteSpace(i.Link) &&
                        string.Equals(i.Link.Trim(), key.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (toRemove != null)
                    {
                        feed.Items.Remove(toRemove);
                    }

                    if (feed.Items.Count == 0)
                    {
                        category.Feeds.Remove(feed);
                    }
                }

                if (category.Feeds.Count == 0)
                {
                    section.Items.Remove(category);
                }
            }

            section.UpdateUnreadCount();

            RemoveItemFromCache(section, key);
        }

        private void RemoveArticleFromAllCollections(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            foreach (var category in CategoryGroups)
            {
                foreach (var feed in category.Feeds.ToList())
                {
                    var match = feed.Items.FirstOrDefault(i =>
                        !string.IsNullOrWhiteSpace(i.Link) &&
                        string.Equals(i.Link.Trim(), key.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        feed.Items.Remove(match);
                        break;
                    }
                }
            }

            RemoveArticleFromSectionFeeds(_pinnedSection, key);
            RemoveArticleFromSectionFeeds(_readLaterSection, key);
        }

        private void AddItemToCache(ArticleSectionViewModel section, string feedName, string categoryName, FeedItem item)
        {
            if (section != _pinnedSection && section != _readLaterSection)
            {
                return;
            }

            var cache = section == _pinnedSection ? _pinnedSourceFeeds : _readLaterSourceFeeds;
            var feedVm = cache.FirstOrDefault(f => string.Equals(f.Name, feedName, StringComparison.OrdinalIgnoreCase));
            if (feedVm == null)
            {
                feedVm = new FeedGroupViewModel
                {
                    Name = feedName,
                    Category = categoryName,
                    IsExpanded = true,
                    HideUnreadIndicators = section.HideUnreadIndicators
                };
                cache.Add(feedVm);
            }
            else
            {
                feedVm.Category = categoryName;
                feedVm.HideUnreadIndicators = section.HideUnreadIndicators;
            }

            if (!feedVm.Items.Any(i =>
                    !string.IsNullOrWhiteSpace(i.Link) &&
                    string.Equals(i.Link.Trim(), item.Link?.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                feedVm.Items.Add(item);
            }
        }

        private void RemoveItemFromCache(ArticleSectionViewModel section, string key)
        {
            if (section != _pinnedSection && section != _readLaterSection)
            {
                return;
            }

            var cache = section == _pinnedSection ? _pinnedSourceFeeds : _readLaterSourceFeeds;
            foreach (var feed in cache.ToList())
            {
                var match = feed.Items.FirstOrDefault(i =>
                    !string.IsNullOrWhiteSpace(i.Link) &&
                    string.Equals(i.Link.Trim(), key.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    feed.Items.Remove(match);
                }

                if (feed.Items.Count == 0)
                {
                    cache.Remove(feed);
                }
            }
        }

        private void AddArticleToSection(ArticleSectionViewModel section, FeedItem item)
        {
            if (section == null || item == null)
            {
                return;
            }

            var key = item.Link?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            var feedName = string.IsNullOrWhiteSpace(item.FeedName) ? "Feed" : item.FeedName;
            var categoryName = ResolveCategoryNameForFeed(feedName);

            var categoryVm = section.Items.OfType<CategoryGroupViewModel>().FirstOrDefault(c =>
                string.Equals(c.Name, categoryName, StringComparison.OrdinalIgnoreCase));
            if (categoryVm == null)
            {
                categoryVm = new CategoryGroupViewModel
                {
                    Name = categoryName,
                    IconKind = "FolderMultipleOutline",
                    IsExpanded = true,
                    HideUnreadIndicators = section.HideUnreadIndicators
                };
                section.Items.Add(categoryVm);
            }
            else
            {
                categoryVm.HideUnreadIndicators = section.HideUnreadIndicators;
            }

            var feedGroup = categoryVm.Feeds.FirstOrDefault(f =>
                string.Equals(f.Name, feedName, StringComparison.OrdinalIgnoreCase));

            if (feedGroup == null)
            {
                feedGroup = new FeedGroupViewModel
                {
                    Name = feedName,
                    Category = categoryName,
                    IsExpanded = true,
                    HideUnreadIndicators = section.HideUnreadIndicators
                };
                categoryVm.Feeds.Add(feedGroup);
            }
            else
            {
                feedGroup.HideUnreadIndicators = section.HideUnreadIndicators;
            }

            var alreadyExists = feedGroup.Items.Any(i =>
                !string.IsNullOrWhiteSpace(i.Link) &&
                string.Equals(i.Link.Trim(), key, StringComparison.OrdinalIgnoreCase));

            if (!alreadyExists)
            {
                feedGroup.Items.Add(item);
            }

            section.UpdateUnreadCount();

            AddItemToCache(section, feedName, categoryName, item);
        }

        private static FeedItem CloneSnapshot(FeedItem item)
        {
            return new FeedItem
            {
                FeedName = item.FeedName,
                Title = item.Title,
                Description = item.Description,
                Link = item.Link,
                PublicationDate = item.PublicationDate,
                IsRead = item.IsRead,
                IsPinned = item.IsPinned,
                IsReadLater = item.IsReadLater
            };
        }

        private void CopyLinkFromItem(FeedItem item)
        {
            var normalized = NormalizeExternalLink(item?.Link);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            try
            {
                Clipboard.SetText(normalized);
            }
            catch (Exception)
            {
                // Ignore clipboard errors.
            }
        }

        private void MarkAsUnread(FeedItem item)
        {
            if (item == null)
            {
                return;
            }

            item.IsRead = false;
            var key = item.Link?.Trim();
            if (!string.IsNullOrWhiteSpace(key))
            {
                _readArticleLinks.Remove(key);
                PersistReadState();
            }
        }

        private void PinArticle(FeedItem item)
        {
            if (item == null)
            {
                return;
            }

            var key = item.Link?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            SuppressAutoScroll = true;
            var sourceSection = FindSectionForItem(item);
            var neighbor = GetNeighborForSelection(item, sourceSection);
            _lastInlineMoveOffset = RequestTreeScrollOffset?.Invoke() ?? -1;

            if (_pinnedArticleLinks.Contains(key))
            {
                _pinnedArticleLinks.Remove(key);
                _settings.PinnedArticleSnapshots.RemoveAll(f => string.Equals(f.Link?.Trim(), key, StringComparison.OrdinalIgnoreCase));
                PersistPinnedAndSavedLists();

                item.IsPinned = false;
                item.IsReadLater = false;
                RemoveArticleFromSectionFeeds(_pinnedSection, key);
                AddArticleBackToCategories(item);
                RefreshSectionsDeferred(_lastInlineMoveOffset);
                if (neighbor != null)
                {
                    OnArticleSelected(neighbor, enableAutoScroll: false);
                }
                SuppressAutoScroll = false;
                return;
            }

            _pinnedArticleLinks.Add(key);
            _readLaterArticleLinks.Remove(key);
            _settings.ReadLaterArticleSnapshots.RemoveAll(f => string.Equals(f.Link?.Trim(), key, StringComparison.OrdinalIgnoreCase));
            _settings.PinnedArticleSnapshots.RemoveAll(f => string.Equals(f.Link?.Trim(), key, StringComparison.OrdinalIgnoreCase));
            _settings.PinnedArticleSnapshots.Add(CloneSnapshot(item));
            PersistPinnedAndSavedLists();

            item.IsPinned = true;
            item.IsReadLater = false;
            RemoveArticleFromAllCollections(key);
            AddArticleToSection(_pinnedSection, item);
            RefreshSectionsDeferred(_lastInlineMoveOffset);
            item.IsSelected = false;
            if (neighbor != null)
            {
                OnArticleSelected(neighbor, enableAutoScroll: false);
            }
            SuppressAutoScroll = false;
        }

        private void AddToReadLater(FeedItem item)
        {
            if (item == null)
            {
                return;
            }

            var key = item.Link?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            SuppressAutoScroll = true;
            var sourceSection = FindSectionForItem(item);
            var neighbor = GetNeighborForSelection(item, sourceSection);
            _lastInlineMoveOffset = RequestTreeScrollOffset?.Invoke() ?? -1;
            if (_readLaterArticleLinks.Contains(key))
            {
                _readLaterArticleLinks.Remove(key);
                _settings.ReadLaterArticleSnapshots.RemoveAll(f => string.Equals(f.Link?.Trim(), key, StringComparison.OrdinalIgnoreCase));
                PersistPinnedAndSavedLists();

                item.IsReadLater = false;
                RemoveArticleFromSectionFeeds(_readLaterSection, key);
                AddArticleBackToCategories(item);
                RefreshSectionsDeferred(_lastInlineMoveOffset);
                if (neighbor != null)
                {
                    OnArticleSelected(neighbor, enableAutoScroll: false);
                }
                SuppressAutoScroll = false;
                return;
            }

            _readLaterArticleLinks.Add(key);
            _pinnedArticleLinks.Remove(key);
            _settings.PinnedArticleSnapshots.RemoveAll(f => string.Equals(f.Link?.Trim(), key, StringComparison.OrdinalIgnoreCase));
            _settings.ReadLaterArticleSnapshots.RemoveAll(f => string.Equals(f.Link?.Trim(), key, StringComparison.OrdinalIgnoreCase));
            _settings.ReadLaterArticleSnapshots.Add(CloneSnapshot(item));
            PersistPinnedAndSavedLists();

            item.IsReadLater = true;
            item.IsPinned = false;
            RemoveArticleFromAllCollections(key);
            AddArticleToSection(_readLaterSection, item);
            RefreshSectionsDeferred(_lastInlineMoveOffset);
            item.IsSelected = false;
            if (neighbor != null)
            {
                OnArticleSelected(neighbor, enableAutoScroll: false);
            }
            SuppressAutoScroll = false;
        }
        private async Task RefreshFeedsAsync()
        {
            IsLoading = true;
            string linkToRestore = null;
            string refreshSummaryMessage = null;
            var refreshTimer = Stopwatch.StartNew();
            try
            {
                // Capture current scroll offset (view will provide it)
                _lastTreeScrollOffset = RequestTreeScrollOffset?.Invoke() ?? 0;

                if (_feeds.Count == 0)
                {
                    _allCategoryGroups.Clear();
            CategoryGroups.Clear();
            UpdateFeedSection(_pinnedSection, new Dictionary<string, List<FeedItem>>(StringComparer.OrdinalIgnoreCase));
            UpdateFeedSection(_readLaterSection, new Dictionary<string, List<FeedItem>>(StringComparer.OrdinalIgnoreCase));
            SyncMyFeedsSection();
            _allCategoryGroups.Clear();
            FilterCategories.Clear();
            FilterFeeds.Clear();
            refreshTimer.Stop();
            refreshSummaryMessage = "No feeds to refresh";
            return;
        }

                var categoryExpandedStates = _settings.CategoryExpandedStates ?? new Dictionary<string, bool>();
                var feedExpandedStates = _settings.TreeViewExpandedStates ?? new Dictionary<string, bool>();

                // Preserve read state across refreshes (keyed by article link)
                var previousReadStates = _readArticleLinks;

                var advertisementKeywords = _settings.AdvertisementFilterEnabled ? GetAdvertisementKeywordsForFiltering() : new List<string>();
                var extraPerFeed = Math.Min(MaxExtraPerFeed, GetSectionMaxPerFeed(_pinnedSection) + GetSectionMaxPerFeed(_readLaterSection));
                var advertisementBuffer = (_settings.AdvertisementFilterEnabled && advertisementKeywords.Count > 0)
                    ? Math.Min(MaxExtraPerFeed, MaxFeeds) // over-fetch to replace filtered ads
                    : 0;
                var targetPerFeed = Math.Max(
                    MaxFeeds,
                    Math.Min(MaxFeeds + extraPerFeed + advertisementBuffer, MaxFeeds + MaxExtraPerFeed));

                var items = await _feedService.FetchArticlesAsync(
                    _feeds,
                    Keyword,
                    targetPerFeed,
                    advertisementKeywords.Count > 0 ? advertisementKeywords : null);
                var totalEnabledFeeds = _feeds.Count(f => f.IsEnabled);
                var totalFetchedItems = items.Count;
                var failedFeeds = items
                    .Where(it =>
                        it?.Title != null &&
                        (it.Title.StartsWith("[ERROR]", StringComparison.OrdinalIgnoreCase) ||
                         it.Title.StartsWith("[BLOCKED]", StringComparison.OrdinalIgnoreCase)))
                    .Select(it => it.FeedName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();

                if (_feedService.LastBlockedFeeds.Count > 0)
                {
                    var blockedList = string.Join(", ", _feedService.LastBlockedFeeds.Distinct(StringComparer.OrdinalIgnoreCase));
                    System.Windows.MessageBox.Show(
                        $"The following feeds were blocked by security rules (local/private addresses are not allowed):\n{blockedList}",
                        "Feed blocked",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                }

                if (_settings.AdvertisementFilterEnabled && advertisementKeywords.Count > 0)
                {
                    items = items.Where(item => !item.IsAdvertisement).ToList();
                }

                // Reapply read states to freshly loaded items
                if (previousReadStates.Count > 0)
                {
                    foreach (var item in items)
                    {
                        var key = item.Link?.Trim();
                        if (!string.IsNullOrWhiteSpace(key) &&
                            previousReadStates.Contains(key))
                        {
                            item.IsRead = true;
                        }
                    }
                }

                var pinnedLookup = new Dictionary<string, List<FeedItem>>(StringComparer.OrdinalIgnoreCase);
                var readLaterLookup = new Dictionary<string, List<FeedItem>>(StringComparer.OrdinalIgnoreCase);

                // Rehydrate saved pinned/read-later snapshots that may no longer be in the feed responses.
                void AddSnapshotIfMissing(List<FeedItem> source, Dictionary<string, List<FeedItem>> targetLookup)
                {
                    if (source == null)
                    {
                        return;
                    }

                    foreach (var snap in source)
                    {
                        var keySnap = snap?.Link?.Trim();
                        if (string.IsNullOrWhiteSpace(keySnap))
                        {
                            continue;
                        }

                        if (items.Any(i => string.Equals(i.Link?.Trim(), keySnap, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        snap.IsPinned = targetLookup == pinnedLookup;
                        snap.IsReadLater = targetLookup == readLaterLookup;
                        AddToLookup(targetLookup, snap);
                    }
                }

                var regularItems = new List<FeedItem>();

                foreach (var item in items)
                {
                    var key = item.Link?.Trim();
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        if (_pinnedArticleLinks.Contains(key))
                        {
                            item.IsPinned = true;
                            item.IsReadLater = false;
                            AddToLookup(pinnedLookup, item);
                            continue;
                        }

                        if (_readLaterArticleLinks.Contains(key))
                        {
                            item.IsReadLater = true;
                            item.IsPinned = false;
                            AddToLookup(readLaterLookup, item);
                            continue;
                        }
                    }

                    item.IsPinned = false;
                    item.IsReadLater = false;
                    regularItems.Add(item);
                }

                AddSnapshotIfMissing(_settings.PinnedArticleSnapshots, pinnedLookup);
                AddSnapshotIfMissing(_settings.ReadLaterArticleSnapshots, readLaterLookup);

                items = regularItems;

                // Clamp items per feed to the configured MaxFeeds for My Feeds display
                items = items
                    .GroupBy(i => string.IsNullOrWhiteSpace(i.FeedName) ? "Feed" : i.FeedName)
                    .SelectMany(g => g
                        .OrderByDescending(it => it.PublicationDate)
                        .Take(MaxFeeds))
                    .ToList();

                // Group items by category
                var categorizedItems = items
                    .Where(it =>
                    {
                        var link = it.Link?.Trim();
                        if (string.IsNullOrWhiteSpace(link))
                        {
                            return true;
                        }
                        return !_pinnedArticleLinks.Contains(link) && !_readLaterArticleLinks.Contains(link);
                    })
                    .GroupBy(item =>
                {
                    var feed = _feeds.FirstOrDefault(f => f.Name == item.FeedName);
                    return feed?.Category ?? "Default";
                }).ToDictionary(g => g.Key, g => g);

                var desiredCategoryOrder = new List<string>();
                desiredCategoryOrder.AddRange(_settings.Categories.Where(c => categorizedItems.ContainsKey(c)));
                desiredCategoryOrder.AddRange(categorizedItems.Keys.Where(k => !_settings.Categories.Contains(k)));

                var previousSelectedLink = SelectedArticleLink?.Trim();
                _suppressSelectionDuringRefresh = true;

                // Update categories in-place
                foreach (var categoryName in desiredCategoryOrder)
                {
                    if (!categorizedItems.TryGetValue(categoryName, out var categoryItems))
                    {
                        continue;
                    }

                    var categoryVm = CategoryGroups.FirstOrDefault(c => string.Equals(c.Name, categoryName, StringComparison.OrdinalIgnoreCase));
                    if (categoryVm == null)
                    {
                        categoryVm = new CategoryGroupViewModel
                        {
                            Name = categoryName,
                            IsExpanded = categoryExpandedStates.TryGetValue(categoryName, out var expanded) ? expanded : true,
                            HideUnreadIndicators = false
                        };
                        CategoryGroups.Add(categoryVm);
                    }

                    var feedGroups = categoryItems.GroupBy(item => item.FeedName).ToList();
                    var desiredFeeds = feedGroups.Select(fg => fg.Key).ToList();

                    // Remove feeds that no longer exist
                    foreach (var feedVm in categoryVm.Feeds.ToList())
                    {
                        if (!desiredFeeds.Any(df => string.Equals(df, feedVm.Name, StringComparison.OrdinalIgnoreCase)))
                        {
                            categoryVm.Feeds.Remove(feedVm);
                        }
                    }

                    // Ensure feeds exist and in order
                    for (int feedIndex = 0; feedIndex < desiredFeeds.Count; feedIndex++)
                    {
                        var feedName = desiredFeeds[feedIndex];
                        var feedGroup = feedGroups.First(fg => string.Equals(fg.Key, feedName, StringComparison.OrdinalIgnoreCase));
                        var feedVm = categoryVm.Feeds.FirstOrDefault(f => string.Equals(f.Name, feedName, StringComparison.OrdinalIgnoreCase));

                        if (feedVm == null)
                        {
                            feedVm = new FeedGroupViewModel
                            {
                                Name = feedName,
                                Category = categoryName,
                                IsExpanded = feedExpandedStates.TryGetValue(feedName, out var feedExpanded) ? feedExpanded : true,
                                HideUnreadIndicators = false
                            };
                            categoryVm.Feeds.Insert(feedIndex, feedVm);
                        }
                        else
                        {
                            if (categoryVm.Feeds.IndexOf(feedVm) != feedIndex)
                            {
                                categoryVm.Feeds.Move(categoryVm.Feeds.IndexOf(feedVm), feedIndex);
                            }
                        }

                        UpdateItemsInPlace(feedVm.Items, feedGroup.ToList(), previousReadStates);
                    }
                }

                // Remove categories that no longer have items
                foreach (var cat in CategoryGroups.ToList())
                {
                    if (!desiredCategoryOrder.Any(dc => string.Equals(dc, cat.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        CategoryGroups.Remove(cat);
                    }
                }

                // Reorder categories to desired order
                for (int i = 0; i < desiredCategoryOrder.Count; i++)
                {
                    var name = desiredCategoryOrder[i];
                    var cat = CategoryGroups.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (cat != null && CategoryGroups.IndexOf(cat) != i)
                    {
                        CategoryGroups.Move(CategoryGroups.IndexOf(cat), i);
                    }
                }

                OnPropertyChanged(nameof(CategoryGroups));
                OnPropertyChanged(nameof(CurrentFeedSettingsDisplay));
                UpdateFeedSection(_pinnedSection, pinnedLookup);
                UpdateFeedSection(_readLaterSection, readLaterLookup);
                SyncMyFeedsSection();
                SelectedSection ??= _myFeedsSection;
                OnPropertyChanged(nameof(VisibleSections));

                _allCategoryGroups.Clear();
                foreach (var cat in CategoryGroups)
                {
                    _allCategoryGroups.Add(cat);
                }
                RebuildFilterListsForSection(_selectedSection ?? _myFeedsSection);
                ApplyTreeFilter();

                refreshTimer.Stop();
                refreshSummaryMessage =
                    $"Refreshed {totalEnabledFeeds} feed{(totalEnabledFeeds == 1 ? "" : "s")} in {refreshTimer.Elapsed.TotalSeconds:F1}s - " +
                    $"{totalFetchedItems} item{(totalFetchedItems == 1 ? "" : "s")}" +
                    (failedFeeds > 0 ? $", {failedFeeds} failed" : string.Empty);

                // Attempt to restore selection to the previously opened article
                if (!string.IsNullOrWhiteSpace(previousSelectedLink))
                {
                    var match = CategoryGroups
                        .SelectMany(cg => cg.Feeds ?? Enumerable.Empty<FeedGroupViewModel>())
                        .SelectMany(fg => fg.Items ?? Enumerable.Empty<FeedItem>())
                        .FirstOrDefault(item =>
                            !string.IsNullOrWhiteSpace(item.Link) &&
                            string.Equals(item.Link.Trim(), previousSelectedLink.Trim(), StringComparison.OrdinalIgnoreCase));

                    if (match == null && _pinnedSection != null)
                    {
                        match = _pinnedSection.Items
                            .OfType<CategoryGroupViewModel>()
                            .SelectMany(cg => cg.Feeds ?? Enumerable.Empty<FeedGroupViewModel>())
                            .SelectMany(fg => fg.Items ?? Enumerable.Empty<FeedItem>())
                            .FirstOrDefault(item =>
                                !string.IsNullOrWhiteSpace(item.Link) &&
                                string.Equals(item.Link.Trim(), previousSelectedLink.Trim(), StringComparison.OrdinalIgnoreCase));
                    }

                    if (match == null && _readLaterSection != null)
                    {
                        match = _readLaterSection.Items
                            .OfType<CategoryGroupViewModel>()
                            .SelectMany(cg => cg.Feeds ?? Enumerable.Empty<FeedGroupViewModel>())
                            .SelectMany(fg => fg.Items ?? Enumerable.Empty<FeedItem>())
                            .FirstOrDefault(item =>
                                !string.IsNullOrWhiteSpace(item.Link) &&
                                string.Equals(item.Link.Trim(), previousSelectedLink.Trim(), StringComparison.OrdinalIgnoreCase));
                    }

                    if (match != null)
                    {
                        SelectedArticleLink = NormalizeExternalLink(match.Link);
                        linkToRestore = match.Link?.Trim();
                        // Keep showing the previous content without triggering a selection change.
                    }
                    else
                    {
                        // If we cannot find the previous selection, at least restore scroll.
                        ScrollOffsetRestoreRequested?.Invoke(_lastTreeScrollOffset);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error loading feeds: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                refreshTimer.Stop();
                IsLoading = false;
                _suppressSelectionDuringRefresh = false;
                if (!string.IsNullOrWhiteSpace(linkToRestore))
                {
                    SelectionRestoreRequested?.Invoke(linkToRestore);
                }
                // Always attempt to restore previous scroll position
                ScrollOffsetRestoreRequested?.Invoke(_lastTreeScrollOffset);
                if (!string.IsNullOrWhiteSpace(refreshSummaryMessage))
                {
                    SnackbarMessageQueue?.Enqueue(refreshSummaryMessage);
                }
            }
        }

        private void SaveFeedSettings()
        {
            try
            {
                _settings.MaxFeeds = _maxFeeds;
                _settings.IsShowContentAlwaysOn = _isShowContentAlwaysOn;
                _settings.AutoRefresh = _pendingAutoRefresh;
                _settings.AutoRefreshIntervalMinutes = _pendingAutoRefreshIntervalMinutes;
                _settings.TreeWidth = _pendingTreeWidth;
                _settings.DarkMode = _pendingDarkMode;
                _settings.AutoUpdateCheckEnabled = _autoUpdateEnabledCached;
                _settingsService.SaveSettings(_settings);

                OnPropertyChanged(nameof(CurrentFeedSettingsDisplay));
                OnPropertyChanged(nameof(MaxFeeds));
                OnPropertyChanged(nameof(DarkMode));
                OnPropertyChanged(nameof(AutoRefresh));
                OnPropertyChanged(nameof(PendingTreeWidth));
                _pendingDarkMode = _settings.DarkMode;
                OnPropertyChanged(nameof(PendingDarkMode));

                if (_settings.AutoRefresh)
                {
                    RestartAutoRefreshTimer();
                }
                else
                {
                    StopAutoRefreshTimer();
                    NextAutoRefreshDisplay = string.Empty;
                }

                ApplyActiveDarkMode();

                _ = RefreshFeedsAsync();


                System.Windows.MessageBox.Show(
                    $"Feed settings saved successfully!\n\n" +
                    $"Max Articles per Feed: {_maxFeeds}\n" +
                    $"Always-On Content: {(IsShowContentAlwaysOn ? "Enabled" : "Disabled")}\n" +
                    $"Dark Mode: {(_settings.DarkMode ? "Enabled" : "Disabled")} (applies to open article and browser)\n" +
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

        private void PromptAutoUpdateIfNeeded()
        {
            if (_settings.AutoUpdatePromptShown)
            {
                return;
            }

            var result = System.Windows.MessageBox.Show(
                "Enable automatic update checks?\n" +
                "Manage automatic update checks in About → Automatic update check",
                "Automatic Updates",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            _settings.AutoUpdateCheckEnabled = result == System.Windows.MessageBoxResult.Yes;
            _settings.AutoUpdatePromptShown = true;
            _pendingAutoUpdateCheck = _settings.AutoUpdateCheckEnabled;

            _settingsService.SaveSettings(_settings);

            OnPropertyChanged(nameof(PendingAutoUpdateCheck));
            OnPropertyChanged(nameof(AutoUpdateCheckEnabled));
        }

        private async Task CheckForUpdatesAsync()
        {
            if (!_settings.AutoUpdateCheckEnabled)
            {
                return;
            }

            if (_autoUpdatePromptedThisSession)
            {
                return;
            }

            try
            {
                _autoUpdatePromptedThisSession = true;
                await UpdateChecker.RunInteractiveCheckAsync(
                    Application.Current?.MainWindow,
                    showUpToDateMessage: false,
                    showFailureMessage: false);
                // sync cached value in case user changed setting via About dialog during prompt
                _autoUpdateEnabledCached = _settings.AutoUpdateCheckEnabled;
            }
            catch
            {
                // Silently ignore update check errors to avoid bothering the user.
                _autoUpdatePromptedThisSession = false; // allow retry on next app start
            }
        }

        private void RebuildFilterLists(IEnumerable<CategoryGroupViewModel> sourceCategories,
            IEnumerable<string> feedNamesOverride = null,
            bool includeCategories = true,
            bool resetSelections = false,
            HashSet<string> persistedCategories = null,
            HashSet<string> persistedFeeds = null,
            HashSet<string> knownCategories = null,
            HashSet<string> knownFeeds = null)
        {
            var previouslySelectedCategories = new HashSet<string>(
                FilterCategories.Where(c => c.IsSelected).Select(c => c.Name),
                StringComparer.OrdinalIgnoreCase);
            var previouslySelectedFeeds = new HashSet<string>(
                FilterFeeds.Where(f => f.IsSelected).Select(f => f.Name),
                StringComparer.OrdinalIgnoreCase);

            var categorySelectionSource = (!resetSelections && persistedCategories != null)
                ? persistedCategories
                : (!resetSelections ? previouslySelectedCategories : null);
            var feedSelectionSource = (!resetSelections && persistedFeeds != null)
                ? persistedFeeds
                : (!resetSelections ? previouslySelectedFeeds : null);
            var categoryKnownSource = knownCategories ?? categorySelectionSource ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var feedKnownSource = knownFeeds ?? feedSelectionSource ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in FilterCategories)
            {
                item.PropertyChanged -= FilterItem_PropertyChanged;
            }
            foreach (var item in FilterFeeds)
            {
                item.PropertyChanged -= FilterItem_PropertyChanged;
            }

            FilterCategories.Clear();
            FilterFeeds.Clear();

            var categoryNames = includeCategories
                ? sourceCategories
                    .Select(c => c.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n)
                    .ToList()
                : new List<string>();

            var feedNames = sourceCategories
                .SelectMany(c => c.Feeds ?? Enumerable.Empty<FeedGroupViewModel>())
                .Select(f => f.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .ToList();

            if (feedNamesOverride != null)
            {
                feedNames = feedNamesOverride
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n)
                    .ToList();
            }

            bool hadCategorySelection = categorySelectionSource != null && categorySelectionSource.Count > 0;
            bool hadFeedSelection = feedSelectionSource != null && feedSelectionSource.Count > 0;

            foreach (var name in categoryNames)
            {
                bool isSelected;
                if (hadCategorySelection)
                {
                    if (categorySelectionSource.Contains(name))
                    {
                        isSelected = true;
                    }
                    else if (!categoryKnownSource.Contains(name))
                    {
                        // New category -> default to selected
                        isSelected = true;
                    }
                    else
                    {
                        isSelected = false;
                    }
                }
                else
                {
                    isSelected = true;
                }

                var item = new SelectableFilterItem
                {
                    Name = name,
                    IsSelected = isSelected
                };
                item.PropertyChanged += FilterItem_PropertyChanged;
                FilterCategories.Add(item);
            }

            foreach (var name in feedNames)
            {
                bool isSelected;
                if (hadFeedSelection)
                {
                    if (feedSelectionSource.Contains(name))
                    {
                        isSelected = true;
                    }
                    else if (!feedKnownSource.Contains(name))
                    {
                        // New feed -> default to selected
                        isSelected = true;
                    }
                    else
                    {
                        isSelected = false;
                    }
                }
                else
                {
                    isSelected = true;
                }

                var item = new SelectableFilterItem
                {
                    Name = name,
                    IsSelected = isSelected
                };
                item.PropertyChanged += FilterItem_PropertyChanged;
                FilterFeeds.Add(item);
            }
        }

        private void FilterItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SelectableFilterItem.IsSelected))
            {
                MarkSectionDirty(_selectedSection ?? _myFeedsSection);
                ScheduleFilterApply();
                if (!_suppressFilterStateSave)
                {
                    SaveCurrentSectionFilterState();
                }
            }
        }

        private void SetFilterSelection(IEnumerable<SelectableFilterItem> items, bool isSelected)
        {
            foreach (var item in items)
            {
                item.IsSelected = isSelected;
            }
        }

        private static bool ContainsFilter(string source, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(source) &&
                   source.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ApplyTreeFilter()
        {
            if (CategoryGroups == null || _myFeedsSection == null)
            {
                return;
            }

            var filter = TreeFilterText?.Trim() ?? string.Empty;
            var hasFilter = !string.IsNullOrWhiteSpace(filter);

            if (_allCategoryGroups.Count == 0 && CategoryGroups.Count > 0)
            {
                foreach (var cat in CategoryGroups)
                {
                    _allCategoryGroups.Add(cat);
                }
            }

            var selectedCategories = new HashSet<string>(
                FilterCategories.Where(c => c.IsSelected).Select(c => c.Name),
                StringComparer.OrdinalIgnoreCase);
            var selectedFeeds = new HashSet<string>(
                FilterFeeds.Where(f => f.IsSelected).Select(f => f.Name),
                StringComparer.OrdinalIgnoreCase);

            var categoryFilterActive = FilterCategories.Count > 0 && selectedCategories.Count < FilterCategories.Count;
            var feedFilterActive = FilterFeeds.Count > 0 && selectedFeeds.Count < FilterFeeds.Count;

            var sourceCategories = GetSourceCategoriesForSection(_selectedSection ?? _myFeedsSection);
            var filteringMyFeeds = _selectedSection == null || _selectedSection == _myFeedsSection;
            if (filteringMyFeeds)
            {
                CategoryGroups.Clear();
                _myFeedsSection.Items.Clear();
            }
            else
            {
            var filteredCategories = new List<CategoryGroupViewModel>();
            foreach (var cat in sourceCategories)
            {
                var categoryAllowed = !categoryFilterActive || selectedCategories.Contains(cat.Name);
                var categoryMatchesText = ContainsFilter(cat.Name, filter);

                    var matchingFeeds = cat.Feeds
                        .Where(feed =>
                        {
                            var feedAllowed = !feedFilterActive || selectedFeeds.Contains(feed.Name);
                            if (!feedAllowed || !categoryAllowed)
                            {
                                return false;
                            }

                            if (!hasFilter)
                            {
                                return true;
                            }

                            return ContainsFilter(feed.Name, filter) ||
                                   ContainsFilter(feed.Category, filter) ||
                                   feed.Items.Any(item => ContainsFilter(item.Title, filter) || ContainsFilter(item.Link, filter)) ||
                                   categoryMatchesText;
                        })
                        .ToList();

                    if (!categoryAllowed && matchingFeeds.Count == 0)
                    {
                        continue;
                    }

                    if (!hasFilter && !categoryAllowed)
                    {
                        continue;
                    }

                    if (hasFilter && !categoryMatchesText && matchingFeeds.Count == 0)
                    {
                        continue;
                    }

                    if (matchingFeeds.Count > 0)
                    {
                        var newCat = new CategoryGroupViewModel
                        {
                            Name = cat.Name,
                            IconKind = cat.IconKind,
                            IsExpanded = cat.IsExpanded,
                            HideUnreadIndicators = cat.HideUnreadIndicators,
                            Feeds = new ObservableCollection<FeedGroupViewModel>(matchingFeeds)
                        };
                        filteredCategories.Add(newCat);
                    }
                }

                var targetSection = _selectedSection ?? _myFeedsSection;
                targetSection.Items.Clear();
                foreach (var cat in filteredCategories)
                {
                    targetSection.Items.Add(cat);
                }
                _selectedSection?.UpdateUnreadCount();
                OnPropertyChanged(nameof(VisibleSections));
                return;
            }

            foreach (var cat in sourceCategories)
            {
                var categoryAllowed = !categoryFilterActive || selectedCategories.Contains(cat.Name);
                var categoryMatchesText = ContainsFilter(cat.Name, filter);

                var matchingFeeds = cat.Feeds
                    .Where(feed =>
                    {
                        var feedHasItems = feed.Items.Any() || feed.PagedItems.OfType<FeedItem>().Any();
                        if (!feedHasItems)
                        {
                            return false;
                        }

                        var feedAllowed = !feedFilterActive || selectedFeeds.Contains(feed.Name);
                        if (!feedAllowed || !categoryAllowed)
                        {
                            return false;
                        }

                        if (!hasFilter)
                        {
                            return true;
                        }

                        return ContainsFilter(feed.Name, filter) ||
                               ContainsFilter(feed.Category, filter) ||
                               feed.Items.Any(item => ContainsFilter(item.Title, filter) || ContainsFilter(item.Link, filter)) ||
                               categoryMatchesText;
                    })
                    .ToList();

                // Hide empty categories in Pinned/Read Later
                if (!filteringMyFeeds && matchingFeeds.Count == 0)
                {
                    continue;
                }

                if (!categoryAllowed && matchingFeeds.Count == 0)
                {
                    continue;
                }

                if (!hasFilter && !categoryAllowed)
                {
                    continue;
                }

                if (hasFilter && !categoryMatchesText && matchingFeeds.Count == 0)
                {
                    continue;
                }

                CategoryGroupViewModel displayCategory;
                if (hasFilter || feedFilterActive || categoryFilterActive)
                {
                    displayCategory = new CategoryGroupViewModel
                    {
                        Name = cat.Name,
                        IconKind = cat.IconKind,
                        IsExpanded = cat.IsExpanded,
                        HideUnreadIndicators = cat.HideUnreadIndicators,
                        Feeds = new ObservableCollection<FeedGroupViewModel>(matchingFeeds)
                    };
                }
                else
                {
                    displayCategory = cat;
                }

                if (filteringMyFeeds)
                {
                    CategoryGroups.Add(displayCategory);
                    _myFeedsSection.Items.Add(displayCategory);
                }
                else
                {
                    foreach (var feed in matchingFeeds)
                    {
                        _sectionFilteredFeedsBuffer.Add(feed);
                    }
                }
            }

            if (filteringMyFeeds)
            {
                OnPropertyChanged(nameof(CategoryGroups));
                _myFeedsSection.UpdateUnreadCount();
            }
            else
            {
                var targetSection = _selectedSection ?? _myFeedsSection;
                targetSection.Items.Clear();
                foreach (var feed in _sectionFilteredFeedsBuffer)
                {
                    targetSection.Items.Add(feed);
                }
                _selectedSection?.UpdateUnreadCount();
            }
            OnPropertyChanged(nameof(VisibleSections));
            EnsureSelectionWithinCurrentSection();

            var keySection = _selectedSection ?? _myFeedsSection;
            if (keySection != null)
            {
                _sectionNeedsFilterApply[keySection] = false;
            }
        }

        private void ScheduleFilterApply()
        {
            _filterDebounceTimer.Stop();
            _filterDebounceTimer.Start();
        }

        private List<CategoryGroupViewModel> GetSourceCategoriesForSection(ArticleSectionViewModel section)
        {
            if (section == _pinnedSection || section == _readLaterSection)
            {
                var cached = section == _pinnedSection
                    ? _pinnedSourceFeeds
                    : _readLaterSourceFeeds;

                var feeds = (cached != null && cached.Count > 0)
                    ? cached.ToList()
                    : section.Items.OfType<CategoryGroupViewModel>()
                        .SelectMany(c => c.Feeds)
                        .ToList();

                var grouped = feeds
                    .GroupBy(f => string.IsNullOrWhiteSpace(f.Category) ? "Default" : f.Category, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new CategoryGroupViewModel
                    {
                        Name = g.Key,
                        IconKind = "FolderMultipleOutline",
                        IsExpanded = true,
                        HideUnreadIndicators = section.HideUnreadIndicators,
                        Feeds = new ObservableCollection<FeedGroupViewModel>(g.ToList())
                    })
                    .ToList();

                return grouped;
            }

            return _allCategoryGroups.ToList();
        }

        private void EnsureSelectionWithinCurrentSection()
        {
            var section = _selectedSection ?? _myFeedsSection;
            var visibleItems = BuildVisibleArticleList(section);

            if (visibleItems.Count == 0)
            {
                ClearArticleSelection();
                return;
            }

            if (section == _myFeedsSection)
            {
                // For My Feeds: do not auto-select the first item; only keep current if valid.
                if (_currentSelectedItem != null && visibleItems.Contains(_currentSelectedItem))
                {
                    return;
                }
                // Leave unselected; TryRestoreMyFeedsSelection runs on section change.
                return;
            }

            if (_currentSelectedItem == null || !visibleItems.Contains(_currentSelectedItem))
            {
                OnArticleSelected(visibleItems[0], enableAutoScroll: false);
            }
        }

        private void SaveCurrentSectionFilterState()
        {
            var key = (_selectedSection ?? _myFeedsSection)?.Name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            _savedCategoryFiltersBySection[key] = new HashSet<string>(
                FilterCategories.Where(c => c.IsSelected).Select(c => c.Name),
                StringComparer.OrdinalIgnoreCase);

            _savedFeedFiltersBySection[key] = new HashSet<string>(
                FilterFeeds.Where(f => f.IsSelected).Select(f => f.Name),
                StringComparer.OrdinalIgnoreCase);

            _savedTextFiltersBySection[key] = TreeFilterText ?? string.Empty;

            _savedKnownCategoriesBySection[key] = new HashSet<string>(
                FilterCategories.Select(c => c.Name),
                StringComparer.OrdinalIgnoreCase);

            _savedKnownFeedsBySection[key] = new HashSet<string>(
                FilterFeeds.Select(f => f.Name),
                StringComparer.OrdinalIgnoreCase);
        }

        private void RestoreSectionFilterText(ArticleSectionViewModel section)
        {
            var key = (section ?? _myFeedsSection)?.Name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            _suppressFilterStateSave = true;
            if (_savedTextFiltersBySection.TryGetValue(key, out var text))
            {
                TreeFilterText = text;
            }
            else
            {
                TreeFilterText = string.Empty;
            }
            _suppressFilterStateSave = false;
        }

        private void RebuildFilterListsForSection(ArticleSectionViewModel section, bool resetSelections = false)
        {
            var source = GetSourceCategoriesForSection(section ?? _myFeedsSection);

            var sectionFeeds = (section == _pinnedSection || section == _readLaterSection)
                ? source
                    .SelectMany(c => c.Feeds ?? Enumerable.Empty<FeedGroupViewModel>())
                    .Select(f => f.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n)
                    .ToList()
                : new List<string>();

            var allFeedsForChecklist = sectionFeeds.Count > 0
                ? sectionFeeds
                : _allCategoryGroups
                    .SelectMany(c => c.Feeds ?? Enumerable.Empty<FeedGroupViewModel>())
                    .Select(f => f.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n)
                    .ToList();

            // Show categories in filters for all sections (including Pinned/Read Later)
            var includeCategories = true;
            var sectionKey = (section ?? _myFeedsSection)?.Name ?? string.Empty;
            _savedCategoryFiltersBySection.TryGetValue(sectionKey, out var persistedCategories);
            _savedFeedFiltersBySection.TryGetValue(sectionKey, out var persistedFeeds);
            _savedKnownCategoriesBySection.TryGetValue(sectionKey, out var knownCategories);
            _savedKnownFeedsBySection.TryGetValue(sectionKey, out var knownFeeds);

            _suppressFilterStateSave = true;
            RebuildFilterLists(source, allFeedsForChecklist, includeCategories, resetSelections, persistedCategories, persistedFeeds, knownCategories, knownFeeds);
            _suppressFilterStateSave = false;
            SaveCurrentSectionFilterState();
            ScheduleFilterApply();
            MarkSectionDirty(section ?? _myFeedsSection);
        }

        private void MarkSectionDirty(ArticleSectionViewModel section)
        {
            if (section == null)
            {
                return;
            }

            _sectionNeedsFilterApply[section] = true;
        }

        private void ApplyFilterIfNeeded()
        {
            var section = _selectedSection ?? _myFeedsSection;
            if (section == null)
            {
                return;
            }

            if (!_sectionNeedsFilterApply.TryGetValue(section, out var dirty) || dirty)
            {
                ApplyTreeFilter();
            }
        }

        private static void UpdateItemsInPlace(ObservableCollection<FeedItem> existingItems, List<FeedItem> newItems, HashSet<string> readStates)
        {
            if (existingItems == null || newItems == null)
            {
                return;
            }

            // Map existing by link (trimmed) and drop any duplicates already in the collection.
            var existingMap = new Dictionary<string, FeedItem>(StringComparer.OrdinalIgnoreCase);
            var duplicates = new List<FeedItem>();
            foreach (var item in existingItems)
            {
                var link = item.Link?.Trim();
                if (string.IsNullOrWhiteSpace(link))
                {
                    continue;
                }

                if (!existingMap.ContainsKey(link))
                {
                    existingMap[link] = item;
                }
                else
                {
                    duplicates.Add(item);
                }
            }

            if (duplicates.Count > 0)
            {
                foreach (var dup in duplicates)
                {
                    existingItems.Remove(dup);
                }
            }

            // Remove items that no longer exist
            var newKeys = new HashSet<string>(newItems.Where(i => !string.IsNullOrWhiteSpace(i.Link)).Select(i => i.Link.Trim()), StringComparer.OrdinalIgnoreCase);
            foreach (var old in existingItems.ToList())
            {
                var key = old.Link?.Trim();
                if (!string.IsNullOrWhiteSpace(key) && !newKeys.Contains(key))
                {
                    existingItems.Remove(old);
                }
            }

            for (int idx = 0; idx < newItems.Count; idx++)
            {
                var incoming = newItems[idx];
                var key = incoming.Link?.Trim();
                FeedItem target = null;

                if (!string.IsNullOrWhiteSpace(key) && existingMap.TryGetValue(key, out var found))
                {
                    target = found;
                    // update properties that may change
                    target.Title = incoming.Title;
                    target.Description = incoming.Description;
                    target.PublicationDate = incoming.PublicationDate;
                    target.IsAdvertisement = incoming.IsAdvertisement;
                    target.FeedName = incoming.FeedName;
                }
                else
                {
                    // apply read state if known
                    if (!string.IsNullOrWhiteSpace(key) && readStates.Contains(key))
                    {
                        incoming.IsRead = true;
                    }
                    target = incoming;
                    existingItems.Insert(Math.Min(idx, existingItems.Count), target);
                }

                // ensure correct ordering
                var currentIndex = existingItems.IndexOf(target);
                if (currentIndex != idx)
                {
                if (currentIndex >= 0)
                {
                    var desiredIndex = Math.Min(idx, existingItems.Count - 1);
                    if (currentIndex != desiredIndex)
                    {
                        existingItems.Move(currentIndex, desiredIndex);
                    }
                }
            }
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
                _autoRefreshTimer.Interval = TimeSpan.FromMinutes(_settings.AutoRefreshIntervalMinutes);
                _autoRefreshTimer.Tick += AutoRefreshTimer_Tick;
                _autoRefreshTimer.Start();
                SetNextAutoRefreshTime();

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
                StopAutoRefreshCountdown();
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
            finally
            {
                if (AutoRefresh)
                {
                    SetNextAutoRefreshTime();
                }
            }
        }

        private void SetNextAutoRefreshTime()
        {
            if (!AutoRefresh)
            {
                NextAutoRefreshDisplay = string.Empty;
                return;
            }

            _nextAutoRefreshTime = DateTime.Now.AddMinutes(_settings.AutoRefreshIntervalMinutes);
            UpdateNextAutoRefreshDisplay();
            StartAutoRefreshCountdown();
        }

        private void StartAutoRefreshCountdown()
        {
            StopAutoRefreshCountdown();
            _autoRefreshCountdownTimer = new System.Windows.Threading.DispatcherTimer();
            _autoRefreshCountdownTimer.Interval = TimeSpan.FromSeconds(1);
            _autoRefreshCountdownTimer.Tick += AutoRefreshCountdownTimer_Tick;
            _autoRefreshCountdownTimer.Start();
        }

        private void StopAutoRefreshCountdown()
        {
            if (_autoRefreshCountdownTimer != null)
            {
                _autoRefreshCountdownTimer.Stop();
                _autoRefreshCountdownTimer.Tick -= AutoRefreshCountdownTimer_Tick;
                _autoRefreshCountdownTimer = null;
            }
        }

        private void AutoRefreshCountdownTimer_Tick(object sender, EventArgs e)
        {
            UpdateNextAutoRefreshDisplay();
        }

        private void UpdateNextAutoRefreshDisplay()
        {
            if (!AutoRefresh || _nextAutoRefreshTime == default)
            {
                NextAutoRefreshDisplay = string.Empty;
                return;
            }

            var remaining = _nextAutoRefreshTime - DateTime.Now;
            if (remaining <= TimeSpan.Zero)
            {
                NextAutoRefreshDisplay = "Refreshing soon…";
            }
            else
            {
                NextAutoRefreshDisplay = $"Next refresh in {remaining.Minutes:D2}:{remaining.Seconds:D2}";
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
            if (string.IsNullOrWhiteSpace(html))
            {
                return "<p></p>";
            }

            try
            {
                var document = new HtmlDocument();
                document.OptionFixNestedTags = true;
                document.LoadHtml(html);

                CleanHtmlNode(document.DocumentNode);

                var cleaned = document.DocumentNode.InnerHtml?.Trim();
                if (string.IsNullOrWhiteSpace(cleaned))
                {
                    var fallbackText = NormalizeHtmlToText(html);
                    var collapsedFallback = CollapseWhitespace(fallbackText);
                    return ConvertPlainTextToHtml(collapsedFallback);
                }

                return cleaned;
            }
            catch
            {
                var fallbackText = NormalizeHtmlToText(html);
                var collapsedFallback = CollapseWhitespace(fallbackText);
                return ConvertPlainTextToHtml(collapsedFallback);
            }
        }

        private static void CleanHtmlNode(HtmlNode node)
        {
            if (node == null)
            {
                return;
            }

            if (node.NodeType == HtmlNodeType.Element)
            {
                var tagName = node.Name?.ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(tagName))
                {
                    RemoveNodePreservingChildren(node);
                    return;
                }

                if (BlockedElementNames.Contains(tagName))
                {
                    node.Remove();
                    return;
                }

                if (!AllowedHtmlTags.Contains(tagName))
                {
                    RemoveNodePreservingChildren(node);
                    return;
                }

                SanitizeAttributes(node, tagName);
                if (string.Equals(tagName, "a", StringComparison.OrdinalIgnoreCase))
                {
                    var hasImageChild = node.ChildNodes.Any(child =>
                        child.NodeType == HtmlNodeType.Element &&
                        string.Equals(child.Name, "img", StringComparison.OrdinalIgnoreCase));
                    if (hasImageChild)
                    {
                        var onlyImagesOrWhitespace = node.ChildNodes.All(child =>
                            (child.NodeType == HtmlNodeType.Element && string.Equals(child.Name, "img", StringComparison.OrdinalIgnoreCase)) ||
                            (child.NodeType == HtmlNodeType.Text && string.IsNullOrWhiteSpace(child.InnerText)));
                        if (onlyImagesOrWhitespace)
                        {
                            var parentNode = node.ParentNode;
                            if (parentNode != null)
                            {
                                foreach (var child in node.ChildNodes.ToArray())
                                {
                                    CleanHtmlNode(child);
                                    parentNode.InsertBefore(child, node);
                                }
                                node.Remove();
                                return;
                            }
                        }
                    }
                }
            }

            foreach (var child in node.ChildNodes.ToArray())
            {
                CleanHtmlNode(child);
            }
        }

        private static void RemoveNodePreservingChildren(HtmlNode node)
        {
            if (node?.ParentNode == null)
            {
                node?.Remove();
                return;
            }

            var parent = node.ParentNode;
            var reference = node;
            foreach (var child in node.ChildNodes.ToArray())
            {
                parent.InsertBefore(child, reference);
            }

            parent.RemoveChild(node);
        }

        private static void SanitizeAttributes(HtmlNode node, string tagName)
        {
            var allowedAttributes = AllowedTagAttributes.TryGetValue(tagName, out var attrs)
                ? attrs
                : null;

            foreach (var attribute in node.Attributes.ToArray())
            {
                var attributeName = attribute.Name;
                if (string.IsNullOrWhiteSpace(attributeName))
                {
                    attribute.Remove();
                    continue;
                }

                if (attributeName.StartsWith("on", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(attributeName, "style", StringComparison.OrdinalIgnoreCase))
                {
                    attribute.Remove();
                    continue;
                }

                if (allowedAttributes != null && !allowedAttributes.Contains(attributeName))
                {
                    attribute.Remove();
                    continue;
                }

                if (string.Equals(tagName, "a", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(attributeName, "href", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsSafeLink(attribute.Value))
                    {
                        attribute.Remove();
                        continue;
                    }
                }

                if (string.Equals(tagName, "img", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(attributeName, "src", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsSafeImageSource(attribute.Value))
                    {
                        attribute.Remove();
                        continue;
                    }
                }

                if (string.Equals(attributeName, "class", StringComparison.OrdinalIgnoreCase) && attribute.Value.Length > 128)
                {
                    attribute.Remove();
                }
            }

            if (string.Equals(tagName, "a", StringComparison.OrdinalIgnoreCase))
            {
                if (node.Attributes["href"] == null)
                {
                    RemoveNodePreservingChildren(node);
                    return;
                }

                if (node.Attributes["rel"] == null)
                {
                    node.Attributes.Add("rel", "noopener noreferrer");
                }
            }

            if (string.Equals(tagName, "img", StringComparison.OrdinalIgnoreCase))
            {
                if (node.Attributes["src"] == null)
                {
                    node.Remove();
                    return;
                }

                if (node.Attributes["loading"] == null)
                {
                    node.Attributes.Add("loading", "lazy");
                }
            }

            if (allowedAttributes == null)
            {
                foreach (var attribute in node.Attributes.ToArray())
                {
                    attribute.Remove();
                }
            }
        }

        private static bool IsSafeLink(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var trimmed = value.Trim();
            if (trimmed.StartsWith("javascript", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (trimmed.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (trimmed.StartsWith("//"))
            {
                return true;
            }

            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
            {
                return absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps;
            }

            if (Uri.TryCreate(trimmed, UriKind.Relative, out _))
            {
                return true;
            }

            return false;
        }

        private static bool IsSafeImageSource(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var trimmed = value.Trim();
            if (trimmed.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (trimmed.StartsWith("//"))
            {
                return true;
            }

            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
            {
                return absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps;
            }

            if (Uri.TryCreate(trimmed, UriKind.Relative, out _))
            {
                return true;
            }

            return false;
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

        private void RefreshCurrentArticleHtml()
        {
            // Rebuild the current article HTML (if any) so colors update immediately.
            if (_currentSelectedItem != null)
            {
                SelectedArticleHtml = CreateArticleHtml(_currentSelectedItem);
                SelectedArticleText = BuildArticlePlainText(_currentSelectedItem.Title, _currentSelectedItem.Description);
                return;
            }

            // Fallback: if we had HTML but no tracked item, force the webview to refresh.
            if (!string.IsNullOrEmpty(SelectedArticleHtml))
            {
                UpdateArticleWebView();
            }
        }

        private IEnumerable<FeedItem> GetSectionItemsShallow(ArticleSectionViewModel section)
        {
            if (section == null)
            {
                yield break;
            }

            foreach (var child in section.Items)
            {
                if (child is CategoryGroupViewModel category && category.Feeds != null)
                {
                    foreach (var feed in category.Feeds)
                    {
                        if (feed?.Items == null) continue;
                        foreach (var item in feed.Items)
                        {
                            if (item != null)
                            {
                                yield return item;
                            }
                        }
                    }
                }
                else if (child is FeedGroupViewModel feedGroup && feedGroup.Items != null)
                {
                    foreach (var item in feedGroup.Items)
                    {
                        if (item != null)
                        {
                            yield return item;
                        }
                    }
                }
            }
        }

        private FeedItem FindItemByLinkShallow(ArticleSectionViewModel section, string link)
        {
            if (string.IsNullOrWhiteSpace(link))
            {
                return null;
            }
            var trimmed = link.Trim();
            return GetSectionItemsShallow(section).FirstOrDefault(i =>
                !string.IsNullOrWhiteSpace(i.Link) &&
                string.Equals(i.Link.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
        }

        private void TryRestoreMyFeedsSelection()
        {
            if (_myFeedsSection == null || string.IsNullOrWhiteSpace(_lastMyFeedsSelectedLink))
            {
                return;
            }

            var match = FindItemByLinkShallow(_myFeedsSection, _lastMyFeedsSelectedLink);
            if (match != null)
            {
                OnArticleSelected(match, enableAutoScroll: false);
            }
        }

        private string NormalizeExternalLink(string link)
        {
            if (string.IsNullOrWhiteSpace(link))
            {
                return string.Empty;
            }

            if (Uri.TryCreate(link.Trim(), UriKind.Absolute, out var uri) &&
                uri.Scheme == Uri.UriSchemeHttps)
            {
                return uri.AbsoluteUri;
            }

            return string.Empty;
        }

        private void TryPromptOpenExternal(string url)
        {
            if (_externalPromptInProgress)
            {
                return;
            }

            _externalPromptInProgress = true;
            try
            {
                var normalizedUrl = NormalizeExternalLink(url);
                if (string.IsNullOrEmpty(normalizedUrl))
                {
                    return;
                }

                if (!_browserService.ConfirmAndOpenExternal(normalizedUrl, out var cancelled))
                {
                    if (!cancelled)
                    {
                        System.Windows.MessageBox.Show(
                            $"Could not open URL: {normalizedUrl}",
                            "Error",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Error);
                    }
                }
            }
            finally
            {
                _externalPromptInProgress = false;
            }
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
