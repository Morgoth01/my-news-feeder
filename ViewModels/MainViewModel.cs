using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Net;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Data;
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
        public event Action ArchiveItemsChanged;
        public event Action ArticleLabelsChanged;
        public event Action ArticleNotesChanged;
        public Func<double> RequestTreeScrollOffset;
        public event Action<double> ScrollOffsetRestoreRequested;
        public event Action<FeedItem> ScrollSelectionToTopRequested;
        public Func<bool> HasOpenContextMenu;
        public Func<bool> IsTerminalModeActive;

        private readonly FeedService _feedService;
        private readonly SettingsService _settingsService;
        private readonly BrowserService _browserService;
        private readonly DesktopNotificationService _desktopNotificationService;
        private readonly ArticleReaderService _articleReaderService;
        private readonly List<CategoryGroupViewModel> _allCategoryGroups = new List<CategoryGroupViewModel>();
        private List<FeedGroupViewModel> _pinnedSourceFeeds = new List<FeedGroupViewModel>();
        private List<FeedGroupViewModel> _readLaterSourceFeeds = new List<FeedGroupViewModel>();
        private List<FeedGroupViewModel> _archivedSourceFeeds = new List<FeedGroupViewModel>();
        private List<Feed> _feeds;
        private AppSettings _settings;
        private string _selectedArticleText;
        private string _selectedArticleLink;
        private string _selectedArticleHtml;
        private string _keyword;
        private bool _isBrowserVisible = false;
        private bool _isArticleTransitionOverlayVisible;
        private bool _isContentFullscreen;
        private double _browserHeight = 400;
        private double _articleWebViewHeight = 400;
        private Microsoft.Web.WebView2.Wpf.WebView2 _articleWebView;
        private bool _isApplyingArticleZoomFactor;
        private bool _isReaderViewReady;
        private Microsoft.Web.WebView2.Wpf.WebView2 _linkWebView;
        private bool _isShowContentAlwaysOn = false;
        private int _maxFeeds = 10;
        private int _pendingAutoRefreshIntervalMinutes = 10;
        private int _pendingLiveRefreshIntervalSeconds = 60;
        private bool _pendingAutoRefresh;
        private bool _pendingLiveRefresh;
        private bool _pendingStartInTerminal;
        private string _pendingTerminalThemeName = "Default";
        private bool _pendingTerminalBootAnimationEnabled = true;
        private bool _pendingAdBlockerEnabled = true;
        private bool _pendingAdvertisementFilterEnabled;
        private string _pendingAdvertisementKeywordsText = string.Empty;
        private bool _pendingDesktopNotificationsEnabled;
        private string _pendingImportantKeywordsText = string.Empty;
        private double _pendingTreeWidth;
        private HashSet<string> _readArticleLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _pinnedArticleLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _readLaterArticleLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _archivedArticleLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _suppressSelectionDuringRefresh;
        private double _lastTreeScrollOffset;
        private System.Windows.Threading.DispatcherTimer _autoRefreshTimer;
        private System.Windows.Threading.DispatcherTimer _cacheCleanupTimer;
        private string _copyLinkButtonText = "Copy Link";
        private ArticleSectionViewModel _pinnedSection;
        private ArticleSectionViewModel _readLaterSection;
        private ArticleSectionViewModel _archivedSection;
        private ArticleSectionViewModel _myFeedsSection;
        private bool _sectionRefreshPending;
        private const int MaxExtraPerFeed = 50;
        private bool _refreshQueued;
        private double _lastInlineMoveOffset;
        private FeedItem _currentSelectedItem;
        private string _lastMyFeedsSelectedLink;
        private bool _isLoading;
        private bool _isBrowserPageLoading;
        private bool _isBrowserTransitionVisible;
        private string _pendingBrowserNavigationUrl;
        private bool _pendingBrowserNavigationUseClear;
        private bool _suppressAutoScroll;
        private DateTime _nextAutoRefreshTime;
        private System.Windows.Threading.DispatcherTimer _autoRefreshCountdownTimer;
        private string _nextAutoRefreshDisplay;
        private bool _useCompactArticleCards;
        private bool _pendingDarkMode;
        private bool _pendingAutoUpdateCheck;
        private bool _autoUpdatePromptedThisSession;
        private bool _initialRefreshRequested;
        private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);
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
        private readonly Dictionary<string, ReaderArticleContent> _readerContentCache = new Dictionary<string, ReaderArticleContent>(StringComparer.OrdinalIgnoreCase);
        private bool _isReaderModeActive;
        private bool _isReaderModeLoading;
        private bool _hasImportantNotifications;
        private readonly HashSet<string> _knownArticleLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _hasCompletedInitialRefresh;
        private bool _hasShownEmbeddedBrowserWarmupHintThisSession;
        private DateTime _lastNotificationUtc = DateTime.MinValue;
        private ArchiveWindow _archiveWindow;
        private BrowserSession _embeddedBrowserSession;
        private bool _isEmbeddedVideoPlaybackEnabled;
        private string _selectedMainCategoryName;
        private string _selectedMainFeedName;
        private string _currentArticleListTitle = "Latest Articles";
        private static readonly TimeSpan NotificationCooldown = TimeSpan.FromSeconds(10);
        private const int MaxNotificationsPerRefresh = 3;
        private const int DefaultArchiveAutoCleanupDays = 90;

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

        private static readonly HashSet<string> TrackingQueryParameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "utm_source",
            "utm_medium",
            "utm_campaign",
            "utm_term",
            "utm_content",
            "utm_id",
            "gclid",
            "dclid",
            "fbclid",
            "mc_cid",
            "mc_eid",
            "igshid",
            "mkt_tok",
            "spm",
            "ref",
            "ref_src",
            "ref_url"
        };

        public SnackbarMessageQueue SnackbarMessageQueue { get; }

        // Window height properties with persisten
        private double _articleWindowHeight = 350;
        private double _browserWindowHeight = 350;

        public int[] AvailableRefreshIntervals => AppSettings.AvailableRefreshIntervals;
        public int[] AvailableLiveRefreshIntervals => AppSettings.AvailableLiveRefreshIntervals;

        public ObservableCollection<CategoryGroupViewModel> CategoryGroups { get; set; }
        public ObservableCollection<ArticleSectionViewModel> ArticleSections { get; set; }
        public ObservableCollection<ArticleSectionViewModel> MainVisibleSections { get; } = new ObservableCollection<ArticleSectionViewModel>();
        public ObservableCollection<FeedItem> CurrentArticleItems { get; } = new ObservableCollection<FeedItem>();
        public ICollectionView CurrentArticleItemsView { get; }
        public ArticleSectionViewModel PinnedSection => _pinnedSection;
        public ArticleSectionViewModel ReadLaterSection => _readLaterSection;
        public ArticleSectionViewModel ArchivedSection => _archivedSection;
        public string CurrentArticleListTitle
        {
            get => _currentArticleListTitle;
            private set
            {
                if (_currentArticleListTitle != value)
                {
                    _currentArticleListTitle = value;
                    OnPropertyChanged(nameof(CurrentArticleListTitle));
                }
            }
        }
        public ArticleSectionViewModel SelectedSection
        {
            get => _selectedSection;
            set
            {
                if (value?.OpensInWindow == true)
                {
                    ShowLibraryWindow(LibrarySectionMode.Archive);
                    OnPropertyChanged(nameof(SelectedSection));
                    return;
                }

                if (_selectedSection != value)
                {
                    // If leaving My Feeds, remember the current article.
                    if (_selectedSection == _myFeedsSection && _currentSelectedItem != null && !string.IsNullOrWhiteSpace(_currentSelectedItem.Link))
                    {
                        _lastMyFeedsSelectedLink = _currentSelectedItem.Link.Trim();
                    }

                    _selectedSection = value;
                    _selectedMainCategoryName = null;
                    _selectedMainFeedName = null;
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

                    RefreshCurrentArticleList();
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
                return ArticleSections?.Where(section => section?.OpensInWindow != true) ?? Enumerable.Empty<ArticleSectionViewModel>();
            }
        }
        public ICommand CopyLinkCommand { get; }
        public ICommand CopyArticleLinkCommand { get; }
        public ICommand MarkUnreadCommand { get; }
        public ICommand PinArticleCommand { get; }
        public ICommand ReadLaterArticleCommand { get; }
        public ICommand ArchiveArticleCommand { get; }
        public ICommand ArchiveSelectedArticleCommand { get; }
        public ICommand OpenArchiveWindowCommand { get; }
        public ICommand OpenFeedAllWindowCommand { get; }
        public ICommand OpenFeedAllWindowForFeedCommand { get; }
        public ICommand OpenLibraryWindowCommand { get; }
        public ICommand OpenReadLaterWindowCommand { get; }
        public ICommand ArticleClickCommand { get; }
        public ICommand OpenArticleInWindowCommand { get; }
        public ICommand SelectMainCategoryCommand { get; }
        public ICommand SelectMainFeedCommand { get; }
        public ICommand SelectMainAllArticlesCommand { get; }
        public ICommand MarkGroupAsReadCommand { get; }
        public ICommand MarkGroupAsUnreadCommand { get; }
        public ICommand PinSelectedArticleCommand { get; }
        public ICommand ReadLaterSelectedArticleCommand { get; }
        public ICommand MarkSelectedArticleUnreadCommand { get; }
        public ICommand OpenSelectedArticleInWindowCommand { get; }
        public ICommand ToggleDarkModeCommand { get; }
        public ICommand ShowImportantNotificationsCommand { get; }
        public ICommand NavigateSelectionCommand { get; }
        public ICommand NavigateFeedCommand { get; }
        public ICommand ToggleContentFullscreenCommand { get; }

        public bool HasImportantNotifications
        {
            get => _hasImportantNotifications;
            private set
            {
                if (_hasImportantNotifications != value)
                {
                    _hasImportantNotifications = value;
                    OnPropertyChanged(nameof(HasImportantNotifications));
                }
            }
        }

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
                    OnPropertyChanged(nameof(IsArticleWebViewVisible));
                    OnPropertyChanged(nameof(IsFeedSummaryVisible));
                    OnPropertyChanged(nameof(IsArticleTransitionOverlayVisible));
                    OnPropertyChanged(nameof(ShowLoadingOverlay));
                }
            }
        }

        private bool IsBackgroundRefreshMode => _hasCompletedInitialRefresh && _settings?.AutoRefresh == true && _settings.LiveRefresh;

        public bool IsNotLoading => !_isLoading || IsBackgroundRefreshMode;
        public bool ShowLoadingOverlay => _isLoading && !IsBackgroundRefreshMode;
        public bool IsBrowserPageLoading
        {
            get => _isBrowserPageLoading;
            set
            {
                if (_isBrowserPageLoading != value)
                {
                    _isBrowserPageLoading = value;
                    OnPropertyChanged(nameof(IsBrowserPageLoading));
                    OnPropertyChanged(nameof(ShowBrowserLoadingOverlay));
                }
            }
        }

        public bool ShowBrowserLoadingOverlay => _isBrowserTransitionVisible && IsBrowserVisible;

        public bool IsEmbeddedVideoButtonVisible => IsBrowserVisible && !_isReaderModeActive;

        public string EnableVideoButtonText => _isEmbeddedVideoPlaybackEnabled ? "Media Allowed" : "Allow Media";

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
                    SaveSettingsPreservingImportantNotifications();
                }
            }
        }

        public string SelectedArticleText
        {
            get => _selectedArticleText;
            set
            {
                if (string.Equals(_selectedArticleText, value, StringComparison.Ordinal))
                {
                    return;
                }

                _selectedArticleText = value;
                OnPropertyChanged(nameof(SelectedArticleText));
            }
        }

        public string SelectedArticleLink
        {
            get => _selectedArticleLink;
            set
            {
                if (string.Equals(_selectedArticleLink, value, StringComparison.Ordinal))
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(value) && _isContentFullscreen)
                {
                    SetContentFullscreen(false);
                }

                _selectedArticleLink = value;
                OnPropertyChanged(nameof(SelectedArticleLink));
                (ToggleContentFullscreenCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (EnableEmbeddedVideoCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public string SelectedArticleHtml
        {
            get => _selectedArticleHtml;
            set
            {
                if (string.Equals(_selectedArticleHtml, value, StringComparison.Ordinal))
                {
                    return;
                }

                _selectedArticleHtml = value;
                OnPropertyChanged(nameof(SelectedArticleHtml));
                if (_isReaderModeActive)
                {
                    UpdateArticleWebView();
                }
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
                if (!value && _isContentFullscreen)
                {
                    SetContentFullscreen(false);
                }

                _isBrowserVisible = value;
                OnPropertyChanged(nameof(IsBrowserVisible));
                OnPropertyChanged(nameof(ShowContentButtonText));
                OnPropertyChanged(nameof(IsEmbeddedVideoButtonVisible));
                (ToggleContentFullscreenCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (EnableEmbeddedVideoCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public bool IsContentFullscreen
        {
            get => _isContentFullscreen;
            private set
            {
                if (_isContentFullscreen == value)
                {
                    return;
                }

                _isContentFullscreen = value;
                OnPropertyChanged(nameof(IsContentFullscreen));
                OnPropertyChanged(nameof(ContentFullscreenButtonText));
                OnPropertyChanged(nameof(ContentFullscreenToolTip));
                (ToggleContentFullscreenCommand as RelayCommand)?.RaiseCanExecuteChanged();
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
                    SaveSettingsPreservingImportantNotifications();

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
                    SaveSettingsPreservingImportantNotifications();

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

        public string ContentFullscreenButtonText => IsContentFullscreen ? "Exit Fullscreen" : "Fullscreen";

        public string ContentFullscreenToolTip => IsContentFullscreen
            ? "Exit fullscreen (Esc or F11)"
            : "Open content in fullscreen (F11)";

        public string ReaderModeButtonText
        {
            get
            {
                if (_isReaderModeLoading)
                {
                    return "Loading Reader...";
                }

                return _isReaderModeActive ? "Feed Summary" : "Reader Mode";
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
                SaveSettingsPreservingImportantNotifications();
            }
            catch (Exception)
            {
                // Ignore persistence failures; read state will be rebuilt next successful save.
            }
        }

        private List<ArticleLabelDefinition> ResolveArticleLabelsForLink(string key)
        {
            if (_settings == null || string.IsNullOrWhiteSpace(key))
            {
                return new List<ArticleLabelDefinition>();
            }

            var definitions = _settings.ArticleLabels ?? new List<ArticleLabelDefinition>();
            var assignments = _settings.ArticleLabelAssignments ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (!assignments.TryGetValue(key.Trim(), out var names) || names == null || names.Count == 0)
            {
                return new List<ArticleLabelDefinition>();
            }

            return names
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => definitions.FirstOrDefault(def => string.Equals(def?.Name?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)))
                .Where(def => def != null)
                .GroupBy(def => def.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First().Clone())
                .OrderBy(def => def.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private void ApplyStoredLabelsToItem(FeedItem item)
        {
            if (item == null)
            {
                return;
            }

            var key = item.Link?.Trim();
            item.SetLabels(string.IsNullOrWhiteSpace(key)
                ? Array.Empty<ArticleLabelDefinition>()
                : ResolveArticleLabelsForLink(key));
        }

        private void ApplyStoredLabelsToLink(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            var resolved = ResolveArticleLabelsForLink(key);
            foreach (var item in EnumerateAllTrackedItems().Where(item =>
                         string.Equals(item.Link?.Trim(), key.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                item.SetLabels(resolved);
            }
        }

        private void ApplyStoredLabelsToAllKnownItems()
        {
            foreach (var item in EnumerateAllTrackedItems())
            {
                ApplyStoredLabelsToItem(item);
            }
        }

        private string ResolveArticleNoteForLink(string key)
        {
            if (_settings?.ArticleNoteAssignments == null || string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            return _settings.ArticleNoteAssignments.TryGetValue(key.Trim(), out var note)
                ? note ?? string.Empty
                : string.Empty;
        }

        private void ApplyStoredNoteToItem(FeedItem item)
        {
            if (item == null)
            {
                return;
            }

            var key = item.Link?.Trim();
            item.Note = string.IsNullOrWhiteSpace(key)
                ? string.Empty
                : ResolveArticleNoteForLink(key);
        }

        private void ApplyStoredNoteToLink(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            var note = ResolveArticleNoteForLink(key);
            foreach (var item in EnumerateAllTrackedItems().Where(item =>
                         string.Equals(item.Link?.Trim(), key.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                item.Note = note;
            }
        }

        private void ApplyStoredNotesToAllKnownItems()
        {
            foreach (var item in EnumerateAllTrackedItems())
            {
                ApplyStoredNoteToItem(item);
            }
        }

        private IEnumerable<FeedItem> EnumerateAllTrackedItems()
        {
            var seen = new HashSet<FeedItem>();

            IEnumerable<FeedItem> EnumerateSection(ArticleSectionViewModel section)
            {
                foreach (var item in GetSectionItemsShallow(section))
                {
                    yield return item;
                }
            }

            foreach (var item in EnumerateSection(_pinnedSection)
                .Concat(EnumerateSection(_readLaterSection))
                .Concat(EnumerateSection(_archivedSection))
                .Concat(EnumerateSection(_myFeedsSection))
                .Concat(_settings?.PinnedArticleSnapshots ?? Enumerable.Empty<FeedItem>())
                .Concat(_settings?.ReadLaterArticleSnapshots ?? Enumerable.Empty<FeedItem>())
                .Concat(_settings?.ArchivedArticleSnapshots ?? Enumerable.Empty<FeedItem>()))
            {
                if (item != null && seen.Add(item))
                {
                    yield return item;
                }
            }
        }

        private void OnArchiveItemsChanged()
        {
            try
            {
                ArchiveItemsChanged?.Invoke();
            }
            catch
            {
                // Ignore archive window refresh failures; archive data remains valid.
            }
        }

        private void PersistPinnedAndSavedLists()
        {
            try
            {
                _settings.PinnedArticleLinks = new HashSet<string>(_pinnedArticleLinks, StringComparer.OrdinalIgnoreCase);
                _settings.ReadLaterArticleLinks = new HashSet<string>(_readLaterArticleLinks, StringComparer.OrdinalIgnoreCase);
                _settings.ArchivedArticleLinks = new HashSet<string>(_archivedArticleLinks, StringComparer.OrdinalIgnoreCase);
                _settings.PinnedArticleSnapshots = new List<FeedItem>(_settings.PinnedArticleSnapshots ?? new List<FeedItem>());
                _settings.ReadLaterArticleSnapshots = new List<FeedItem>(_settings.ReadLaterArticleSnapshots ?? new List<FeedItem>());
                _settings.ArchivedArticleSnapshots = new List<FeedItem>(_settings.ArchivedArticleSnapshots ?? new List<FeedItem>());
                SaveSettingsPreservingImportantNotifications();
            }
            catch (Exception)
            {
                // Ignore persistence failures; lists will be refreshed on next save.
            }
        }

        private void SyncImportantNotificationSettingsFromService()
        {
            if (_settings == null || _desktopNotificationService == null)
            {
                return;
            }

            try
            {
                _settings.RecentImportantNotifications = _desktopNotificationService.GetRecentImportantItemsSnapshot();
                _settings.ImportantNotificationsRetentionHours = _desktopNotificationService.GetConfiguredRetentionHours();
            }
            catch (Exception)
            {
                // Ignore sync failures; regular app settings should still be saveable.
            }
        }

        private void SaveSettingsPreservingImportantNotifications()
        {
            if (_settings == null)
            {
                return;
            }

            try
            {
                SyncImportantNotificationSettingsFromService();
                _settingsService.SaveSettings(_settings);
            }
            catch (Exception)
            {
                // Ignore save failures to avoid interrupting UI interactions.
            }
        }

        public bool DarkMode => _settings.DarkMode;
        public bool StartInTerminal => _settings?.StartInTerminal == true;
        public string TerminalThemeName => string.IsNullOrWhiteSpace(_settings?.TerminalThemeName) ? "Default" : _settings.TerminalThemeName;
        public bool TerminalBootAnimationEnabled => _settings?.TerminalBootAnimationEnabled != false;
        public IReadOnlyList<string> AvailableTerminalThemes => TerminalTheme.AllThemes.Select(theme => theme.Name).ToList();

        public void SetStartInTerminal(bool enabled)
        {
            if (_settings == null || _settings.StartInTerminal == enabled)
            {
                return;
            }

            _settings.StartInTerminal = enabled;
            _pendingStartInTerminal = enabled;
            SaveSettingsPreservingImportantNotifications();
            OnPropertyChanged(nameof(StartInTerminal));
            OnPropertyChanged(nameof(PendingStartInTerminal));
        }

        public bool IsArticleWebViewVisible
        {
            get => _isReaderModeActive && _isReaderViewReady && IsNotLoading;
        }

        public bool IsFeedSummaryVisible => (!_isReaderModeActive || !_isReaderViewReady) && IsNotLoading;

        public string FeedSummaryTitle => _currentSelectedItem?.Title ?? string.Empty;

        public string FeedSummaryFeedName => _currentSelectedItem?.FeedName ?? string.Empty;

        public string FeedSummaryPublicationDate => _currentSelectedItem == null
            ? string.Empty
            : _currentSelectedItem.PublicationDate.ToString("dd.MM.yyyy HH:mm");

        public string FeedSummaryDescription
        {
            get
            {
                if (_currentSelectedItem == null)
                {
                    return string.Empty;
                }

                var text = CollapseWhitespace(NormalizeHtmlToText(_currentSelectedItem.Description));
                return string.IsNullOrWhiteSpace(text) ? "No summary is available for this article." : text;
            }
        }

        public string FeedSummaryLink => NormalizeExternalLink(_currentSelectedItem?.Link);

        public double FeedSummaryTitleFontSize => 20.0 * NormalizeArticleZoomFactor(_settings?.FeedSummaryZoomFactor ?? 1.0);

        public double FeedSummaryMetaFontSize => 12.0 * NormalizeArticleZoomFactor(_settings?.FeedSummaryZoomFactor ?? 1.0);

        public double FeedSummaryBodyFontSize => 14.0 * NormalizeArticleZoomFactor(_settings?.FeedSummaryZoomFactor ?? 1.0);

        public double FeedSummaryBodyLineHeight => 19.6 * NormalizeArticleZoomFactor(_settings?.FeedSummaryZoomFactor ?? 1.0);

        public bool IsArticleTransitionOverlayVisible
        {
            get => _isArticleTransitionOverlayVisible && IsNotLoading;
            private set
            {
                if (_isArticleTransitionOverlayVisible == value)
                {
                    return;
                }

                _isArticleTransitionOverlayVisible = value;
                OnPropertyChanged(nameof(IsArticleTransitionOverlayVisible));
            }
        }

        public void SetTerminalThemeName(string themeName)
        {
            var normalizedTheme = NormalizeTerminalThemeName(themeName);
            if (_settings == null || string.Equals(TerminalThemeName, normalizedTheme, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _settings.TerminalThemeName = normalizedTheme;
            _pendingTerminalThemeName = normalizedTheme;
            SaveSettingsPreservingImportantNotifications();
            OnPropertyChanged(nameof(TerminalThemeName));
            OnPropertyChanged(nameof(PendingTerminalThemeName));
        }

        public void SetTerminalBootAnimationEnabled(bool enabled)
        {
            if (_settings == null || TerminalBootAnimationEnabled == enabled)
            {
                return;
            }

            _settings.TerminalBootAnimationEnabled = enabled;
            _pendingTerminalBootAnimationEnabled = enabled;
            SaveSettingsPreservingImportantNotifications();
            OnPropertyChanged(nameof(TerminalBootAnimationEnabled));
            OnPropertyChanged(nameof(PendingTerminalBootAnimationEnabled));
        }

        public bool PendingStartInTerminal
        {
            get => _pendingStartInTerminal;
            set
            {
                if (_pendingStartInTerminal != value)
                {
                    _pendingStartInTerminal = value;
                    OnPropertyChanged(nameof(PendingStartInTerminal));
                }
            }
        }

        public string PendingTerminalThemeName
        {
            get => _pendingTerminalThemeName;
            set
            {
                var normalizedTheme = NormalizeTerminalThemeName(value);
                if (!string.Equals(_pendingTerminalThemeName, normalizedTheme, StringComparison.OrdinalIgnoreCase))
                {
                    _pendingTerminalThemeName = normalizedTheme;
                    OnPropertyChanged(nameof(PendingTerminalThemeName));
                }
            }
        }

        public bool PendingTerminalBootAnimationEnabled
        {
            get => _pendingTerminalBootAnimationEnabled;
            set
            {
                if (_pendingTerminalBootAnimationEnabled != value)
                {
                    _pendingTerminalBootAnimationEnabled = value;
                    OnPropertyChanged(nameof(PendingTerminalBootAnimationEnabled));
                }
            }
        }

        private static string NormalizeTerminalThemeName(string themeName)
        {
            var requestedName = string.IsNullOrWhiteSpace(themeName) ? "Default" : themeName.Trim();
            var theme = TerminalTheme.AllThemes.FirstOrDefault(item =>
                string.Equals(item.Name, requestedName, StringComparison.OrdinalIgnoreCase));
            return theme?.Name ?? "Default";
        }

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
            _embeddedBrowserSession?.SetDarkMode(_settings.DarkMode);
            InvalidateAllArticlePreviewCaches();
            ApplyArticleWebViewTheme();
            RefreshCurrentArticleHtml();
        }

        private void NavigateCurrentArticleInBrowser()
        {
            if (string.IsNullOrWhiteSpace(SelectedArticleLink))
            {
                return;
            }

            ScheduleBrowserNavigation(SelectedArticleLink, useClearNavigation: false);
        }

        private bool CanEnableEmbeddedVideoPlayback()
        {
            return IsBrowserVisible &&
                   !_isReaderModeActive &&
                   !_isEmbeddedVideoPlaybackEnabled &&
                   !string.IsNullOrWhiteSpace(SelectedArticleLink);
        }

        private async Task EnableEmbeddedVideoPlaybackAsync()
        {
            if (!CanEnableEmbeddedVideoPlayback())
            {
                return;
            }

            _isEmbeddedVideoPlaybackEnabled = true;
            _embeddedBrowserSession?.SetMediaPlaybackEnabled(true);
            OnPropertyChanged(nameof(EnableVideoButtonText));
            (EnableEmbeddedVideoCommand as RelayCommand)?.RaiseCanExecuteChanged();

            NavigateCurrentArticleInBrowser();
            await Task.CompletedTask;
        }

        public void SetAutoUpdatePreference(bool enabled)
        {
            _settings.AutoUpdateCheckEnabled = enabled;
            _pendingAutoUpdateCheck = enabled;
            _autoUpdateEnabledCached = enabled;
            SaveSettingsPreservingImportantNotifications();
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

        public Task PrimeEmbeddedBrowserAsync()
        {
            return _embeddedBrowserSession?.PrimeAsync() ?? Task.CompletedTask;
        }

        public BrowserSession CreateBrowserSession(Microsoft.Web.WebView2.Wpf.WebView2 webView = null)
        {
            return _browserService.CreateSession(webView);
        }

        public void StartInitialRefresh()
        {
            if (_initialRefreshRequested)
            {
                return;
            }

            _initialRefreshRequested = true;
            _ = RefreshFeedsAsync();
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
                    OnPropertyChanged(nameof(IsAutoRefreshModeOptionsVisible));
                    OnPropertyChanged(nameof(IsIntervalAutoRefreshSettingsVisible));
                    OnPropertyChanged(nameof(IsLiveRefreshSettingsVisible));
                    OnPropertyChanged(nameof(IsLiveAutoRefreshSelected));
                    OnPropertyChanged(nameof(IsLiveRefreshExpertModeWarningVisible));
                    OnPropertyChanged(nameof(AutoRefreshIntervalDisplay));
                }
            }
        }

        public bool PendingLiveRefresh
        {
            get => _pendingLiveRefresh;
            set
            {
                if (_pendingLiveRefresh != value)
                {
                    _pendingLiveRefresh = value;
                    OnPropertyChanged(nameof(PendingLiveRefresh));
                    OnPropertyChanged(nameof(IsIntervalAutoRefreshSettingsVisible));
                    OnPropertyChanged(nameof(IsLiveRefreshSettingsVisible));
                    OnPropertyChanged(nameof(IsLiveAutoRefreshSelected));
                    OnPropertyChanged(nameof(IsLiveRefreshExpertModeWarningVisible));
                    OnPropertyChanged(nameof(AutoRefreshIntervalDisplay));
                }
            }
        }

        public void DiscardPendingSettingsChanges()
        {
            ResetPendingSettingsFromActiveSettings();
        }

        private void ResetPendingSettingsFromActiveSettings()
        {
            if (_settings == null)
            {
                return;
            }

            _maxFeeds = _settings.MaxFeeds > 0 ? _settings.MaxFeeds : 10;
            _isShowContentAlwaysOn = _settings.IsShowContentAlwaysOn;
            _pendingDarkMode = _settings.DarkMode;
            _pendingAutoRefresh = _settings.AutoRefresh;
            _pendingLiveRefresh = _settings.LiveRefresh;
            _pendingLiveRefreshIntervalSeconds = NormalizeLiveRefreshIntervalSeconds(_settings.LiveRefreshIntervalSeconds);
            _pendingAutoRefreshIntervalMinutes = _settings.AutoRefreshIntervalMinutes > 0 ? _settings.AutoRefreshIntervalMinutes : 10;
            _pendingTreeWidth = _settings.TreeWidth;
            _pendingAutoUpdateCheck = _settings.AutoUpdateCheckEnabled;
            _autoUpdateEnabledCached = _settings.AutoUpdateCheckEnabled;
            _pendingStartInTerminal = _settings.StartInTerminal;
            _pendingTerminalThemeName = NormalizeTerminalThemeName(_settings.TerminalThemeName);
            _pendingTerminalBootAnimationEnabled = _settings.TerminalBootAnimationEnabled;
            _pendingAdBlockerEnabled = _settings.AdBlockerEnabled;
            _pendingAdvertisementFilterEnabled = _settings.AdvertisementFilterEnabled;
            _pendingAdvertisementKeywordsText = string.Join(Environment.NewLine, _settings.AdvertisementKeywords ?? new List<string>());
            _pendingDesktopNotificationsEnabled = _settings.EnableNotifications;
            _pendingImportantKeywordsText = string.Join(Environment.NewLine, _settings.ImportantKeywords ?? new List<string>());

            OnPropertyChanged(nameof(MaxFeeds));
            OnPropertyChanged(nameof(CurrentFeedSettingsDisplay));
            OnPropertyChanged(nameof(IsShowContentAlwaysOn));
            OnPropertyChanged(nameof(ShowContentButtonText));
            OnPropertyChanged(nameof(PendingDarkMode));
            OnPropertyChanged(nameof(PendingAutoRefresh));
            OnPropertyChanged(nameof(PendingLiveRefresh));
            OnPropertyChanged(nameof(PendingLiveRefreshIntervalSeconds));
            OnPropertyChanged(nameof(PendingAutoRefreshIntervalMinutes));
            OnPropertyChanged(nameof(PendingTreeWidth));
            OnPropertyChanged(nameof(PendingAutoUpdateCheck));
            OnPropertyChanged(nameof(PendingStartInTerminal));
            OnPropertyChanged(nameof(PendingTerminalThemeName));
            OnPropertyChanged(nameof(PendingTerminalBootAnimationEnabled));
            OnPropertyChanged(nameof(PendingAdBlockerEnabled));
            OnPropertyChanged(nameof(PendingAdvertisementFilterEnabled));
            OnPropertyChanged(nameof(PendingAdvertisementKeywordsText));
            OnPropertyChanged(nameof(PendingDesktopNotificationsEnabled));
            OnPropertyChanged(nameof(PendingImportantKeywordsText));
            OnPropertyChanged(nameof(IsAutoRefreshModeOptionsVisible));
            OnPropertyChanged(nameof(IsIntervalAutoRefreshSettingsVisible));
            OnPropertyChanged(nameof(IsLiveRefreshSettingsVisible));
            OnPropertyChanged(nameof(IsLiveAutoRefreshSelected));
            OnPropertyChanged(nameof(IsLiveRefreshExpertModeWarningVisible));
            OnPropertyChanged(nameof(AutoRefreshIntervalDisplay));
        }

        public bool IsAutoRefreshModeOptionsVisible => PendingAutoRefresh;
        public bool IsIntervalAutoRefreshSettingsVisible => PendingAutoRefresh && !PendingLiveRefresh;
        public bool IsLiveRefreshSettingsVisible => PendingAutoRefresh && PendingLiveRefresh;
        public bool IsLiveAutoRefreshSelected => PendingAutoRefresh && PendingLiveRefresh;
        public bool IsLiveRefreshExpertModeWarningVisible => IsLiveRefreshSettingsVisible && PendingLiveRefreshIntervalSeconds <= 10;
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
                SaveSettingsPreservingImportantNotifications();

                _browserService.SetAdBlockerEnabled(value);
                _embeddedBrowserSession?.SetAdBlockerEnabled(value);
            }
        }

        public bool PendingAdBlockerEnabled
        {
            get => _pendingAdBlockerEnabled;
            set
            {
                if (_pendingAdBlockerEnabled != value)
                {
                    _pendingAdBlockerEnabled = value;
                    OnPropertyChanged(nameof(PendingAdBlockerEnabled));
                }
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
                SaveSettingsPreservingImportantNotifications();
            }
        }

        public bool PendingAdvertisementFilterEnabled
        {
            get => _pendingAdvertisementFilterEnabled;
            set
            {
                if (_pendingAdvertisementFilterEnabled != value)
                {
                    _pendingAdvertisementFilterEnabled = value;
                    OnPropertyChanged(nameof(PendingAdvertisementFilterEnabled));
                }
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
                SaveSettingsPreservingImportantNotifications();
            }
        }

        public string PendingAdvertisementKeywordsText
        {
            get => _pendingAdvertisementKeywordsText;
            set
            {
                var normalized = value ?? string.Empty;
                if (!string.Equals(_pendingAdvertisementKeywordsText, normalized, StringComparison.Ordinal))
                {
                    _pendingAdvertisementKeywordsText = normalized;
                    OnPropertyChanged(nameof(PendingAdvertisementKeywordsText));
                }
            }
        }

        public bool DesktopNotificationsEnabled
        {
            get => _settings.EnableNotifications;
            set
            {
                if (_settings.EnableNotifications == value)
                {
                    return;
                }

                _settings.EnableNotifications = value;
                OnPropertyChanged(nameof(DesktopNotificationsEnabled));
                SaveSettingsPreservingImportantNotifications();
            }
        }

        public bool PendingDesktopNotificationsEnabled
        {
            get => _pendingDesktopNotificationsEnabled;
            set
            {
                if (_pendingDesktopNotificationsEnabled != value)
                {
                    _pendingDesktopNotificationsEnabled = value;
                    OnPropertyChanged(nameof(PendingDesktopNotificationsEnabled));
                }
            }
        }

        public string ImportantKeywordsText
        {
            get
            {
                var keywords = _settings.ImportantKeywords ?? new List<string>();
                return string.Join(Environment.NewLine, keywords);
            }
            set
            {
                var parsedKeywords = ParseKeywordList(value);
                _settings.ImportantKeywords = parsedKeywords;
                OnPropertyChanged(nameof(ImportantKeywordsText));
                SaveSettingsPreservingImportantNotifications();
            }
        }

        public string PendingImportantKeywordsText
        {
            get => _pendingImportantKeywordsText;
            set
            {
                var normalized = value ?? string.Empty;
                if (!string.Equals(_pendingImportantKeywordsText, normalized, StringComparison.Ordinal))
                {
                    _pendingImportantKeywordsText = normalized;
                    OnPropertyChanged(nameof(PendingImportantKeywordsText));
                }
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
                _embeddedBrowserSession?.SetDarkMode(_settings.DarkMode);

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

        public int PendingLiveRefreshIntervalSeconds
        {
            get => _pendingLiveRefreshIntervalSeconds;
            set
            {
                var normalized = NormalizeLiveRefreshIntervalSeconds(value);
                if (_pendingLiveRefreshIntervalSeconds != normalized)
                {
                    _pendingLiveRefreshIntervalSeconds = normalized;
                    OnPropertyChanged(nameof(PendingLiveRefreshIntervalSeconds));
                    OnPropertyChanged(nameof(IsLiveRefreshExpertModeWarningVisible));
                    OnPropertyChanged(nameof(AutoRefreshIntervalDisplay));
                }
            }
        }

        public string AutoRefreshIntervalDisplay
        {
            get
            {
                if (PendingLiveRefresh)
                {
                    return $"Live mode (every {PendingLiveRefreshIntervalSeconds} seconds)";
                }

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
        public ICommand ToggleReaderModeCommand { get; }
        public ICommand AdBlockerSettingsCommand { get; }
        public ICommand BrowserBackCommand { get; }
        public ICommand BrowserForwardCommand { get; }
        public ICommand BrowserReloadCommand { get; }
        public ICommand EnableEmbeddedVideoCommand { get; }
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
            BrowserService browserService,
            ArticleReaderService articleReaderService = null)
        {
            _feedService = feedService;
            _settingsService = settingsService;
            _browserService = browserService;
            _articleReaderService = articleReaderService ?? new ArticleReaderService();
            try
            {
                _desktopNotificationService = new DesktopNotificationService(_settingsService);
                _desktopNotificationService.RecentImportantItemCountChanged += OnRecentImportantItemCountChanged;
                HasImportantNotifications = _desktopNotificationService.GetRecentImportantItemCount() > 0;
            }
            catch (Exception)
            {
                _desktopNotificationService = null;
                HasImportantNotifications = false;
            }
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
                _pendingLiveRefresh = _settings.LiveRefresh;
                _pendingLiveRefreshIntervalSeconds = NormalizeLiveRefreshIntervalSeconds(_settings.LiveRefreshIntervalSeconds);
                _pendingAutoRefreshIntervalMinutes = _settings.AutoRefreshIntervalMinutes > 0 ? _settings.AutoRefreshIntervalMinutes : 10;
                _pendingTreeWidth = _settings.TreeWidth;
                _pendingAutoUpdateCheck = _settings.AutoUpdateCheckEnabled;
                _pendingStartInTerminal = _settings.StartInTerminal;
                _pendingTerminalThemeName = NormalizeTerminalThemeName(_settings.TerminalThemeName);
                _pendingTerminalBootAnimationEnabled = _settings.TerminalBootAnimationEnabled;
                ResetPendingSettingsFromActiveSettings();
                _readArticleLinks = new HashSet<string>(_settings.ReadArticleLinks ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                _pinnedArticleLinks = new HashSet<string>(_settings.PinnedArticleLinks ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                _readLaterArticleLinks = new HashSet<string>(_settings.ReadLaterArticleLinks ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                _archivedArticleLinks = new HashSet<string>(_settings.ArchivedArticleLinks ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                ApplyArchiveAutoCleanupSilently();
            }
            catch (Exception)
            {
                _settings = new AppSettings();
                _pendingDarkMode = _settings.DarkMode;
                _pendingAutoRefresh = _settings.AutoRefresh;
                _pendingLiveRefresh = _settings.LiveRefresh;
                _pendingLiveRefreshIntervalSeconds = NormalizeLiveRefreshIntervalSeconds(_settings.LiveRefreshIntervalSeconds);
                _pendingAutoRefreshIntervalMinutes = _settings.AutoRefreshIntervalMinutes > 0 ? _settings.AutoRefreshIntervalMinutes : 10;
                _pendingTreeWidth = _settings.TreeWidth;
                _pendingAutoUpdateCheck = _settings.AutoUpdateCheckEnabled;
                _pendingStartInTerminal = _settings.StartInTerminal;
                _pendingTerminalThemeName = NormalizeTerminalThemeName(_settings.TerminalThemeName);
                _pendingTerminalBootAnimationEnabled = _settings.TerminalBootAnimationEnabled;
                _autoUpdateEnabledCached = _settings.AutoUpdateCheckEnabled;
                ResetPendingSettingsFromActiveSettings();
            }

            SyncImportantNotificationSettingsFromService();

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
            if (_settings.ImportantKeywords == null)
            {
                _settings.ImportantKeywords = new List<string>();
            }
            ResetPendingSettingsFromActiveSettings();

            FilterCategories = new ObservableCollection<SelectableFilterItem>();
            FilterFeeds = new ObservableCollection<SelectableFilterItem>();
            CurrentArticleItemsView = CollectionViewSource.GetDefaultView(CurrentArticleItems);
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
            _archivedSection = new ArticleSectionViewModel
            {
                Name = "Archive",
                IconKind = "ArchiveOutline",
                IsExpanded = _settings.SectionExpandedStates.TryGetValue("Archive", out var archiveExpanded) ? archiveExpanded : true,
                HideUnreadIndicators = true,
                OpensInWindow = true
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
            ArticleSections.Add(_archivedSection);
            ArticleSections.Add(_myFeedsSection);
            MainVisibleSections.Add(_myFeedsSection);
            _pinnedSection.PropertyChanged += SectionOnPropertyChanged;
            _readLaterSection.PropertyChanged += SectionOnPropertyChanged;
            _archivedSection.PropertyChanged += SectionOnPropertyChanged;
            _myFeedsSection.PropertyChanged += SectionOnPropertyChanged;
            _sectionNeedsFilterApply[_pinnedSection] = true;
            _sectionNeedsFilterApply[_readLaterSection] = true;
            _sectionNeedsFilterApply[_archivedSection] = true;
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
                _settings.LiveRefreshIntervalSeconds = NormalizeLiveRefreshIntervalSeconds(_settings.LiveRefreshIntervalSeconds);
                _pendingLiveRefreshIntervalSeconds = _settings.LiveRefreshIntervalSeconds;
            }
            catch (Exception)
            {
                _settings.AutoRefreshIntervalMinutes = 10;
                _pendingAutoRefreshIntervalMinutes = 10;
                _settings.LiveRefreshIntervalSeconds = 60;
                _pendingLiveRefreshIntervalSeconds = 60;
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
            ToggleContentFullscreenCommand = new RelayCommand(_ => ToggleContentFullscreen(),
                                                              _ => CanToggleContentFullscreen());
            ToggleReaderModeCommand = new RelayCommand(async _ => await ToggleReaderModeAsync(), _ => CanToggleReaderMode());
            AdBlockerSettingsCommand = new RelayCommand(async _ => await ShowAdBlockerSettingsAsync());
            BrowserBackCommand = new RelayCommand(_ => _embeddedBrowserSession?.GoBack());
            BrowserForwardCommand = new RelayCommand(_ => _embeddedBrowserSession?.GoForward());
            BrowserReloadCommand = new RelayCommand(_ => NavigateCurrentArticleInBrowser(),
                                                    _ => !string.IsNullOrWhiteSpace(SelectedArticleLink));
            EnableEmbeddedVideoCommand = new RelayCommand(async _ => await EnableEmbeddedVideoPlaybackAsync(),
                                                          _ => CanEnableEmbeddedVideoPlayback());
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
                    if (section.OpensInWindow)
                    {
                        ShowLibraryWindow(LibrarySectionMode.Archive);
                        OnPropertyChanged(nameof(SelectedSection));
                        return;
                    }

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
            CopyArticleLinkCommand = new RelayCommand(
                param => CopyLinksFromItems(GetCommandFeedItems(param)),
                param => CanCopySingleArticleLink(GetCommandFeedItems(param)));
            MarkUnreadCommand = new RelayCommand(param => MarkItemsAsUnread(GetCommandFeedItems(param)));
            PinArticleCommand = new RelayCommand(param => ExecuteForCommandItems(param, PinArticle));
            ReadLaterArticleCommand = new RelayCommand(param => ExecuteForCommandItems(param, AddToReadLater));
            ArchiveArticleCommand = new RelayCommand(param => ExecuteForCommandItems(param, ArchiveArticle));
            ArchiveSelectedArticleCommand = new RelayCommand(_ => ArchiveSelectedArticle(), _ => _currentSelectedItem != null);
            OpenArchiveWindowCommand = new RelayCommand(_ => ShowLibraryWindow(LibrarySectionMode.Archive));
            OpenLibraryWindowCommand = new RelayCommand(_ => ShowLibraryWindow(LibrarySectionMode.Pinned));
            OpenReadLaterWindowCommand = new RelayCommand(_ => ShowLibraryWindow(LibrarySectionMode.ReadLater));
            OpenFeedAllWindowCommand = new RelayCommand(param => ShowFeedAllWindow(param as FeedGroupViewModel), param => param is FeedGroupViewModel);
            OpenFeedAllWindowForFeedCommand = new RelayCommand(param => ShowFeedAllWindow(param as Feed), param => param is Feed feed && !string.IsNullOrWhiteSpace(feed.Url));
            ArticleClickCommand = new RelayCommand(param => OnArticleSelected(param as FeedItem), param => param is FeedItem);
            OpenArticleInWindowCommand = new RelayCommand(param => _ = OpenArticleInWindowAsync(param as FeedItem), param => param is FeedItem);
            SelectMainCategoryCommand = new RelayCommand(param => SelectMainCategory(param as CategoryGroupViewModel), param => param is CategoryGroupViewModel);
            SelectMainFeedCommand = new RelayCommand(param => SelectMainFeed(param as FeedGroupViewModel), param => param is FeedGroupViewModel);
            SelectMainAllArticlesCommand = new RelayCommand(_ => SelectMainAllArticles());
            MarkGroupAsReadCommand = new RelayCommand(param => MarkGroupAsRead(param), param => HasUnreadItems(param));
            MarkGroupAsUnreadCommand = new RelayCommand(param => MarkGroupAsUnread(param), param => HasReadItems(param));
            PinSelectedArticleCommand = new RelayCommand(_ => PinSelectedArticle(), _ => _currentSelectedItem != null);
            ReadLaterSelectedArticleCommand = new RelayCommand(_ => AddSelectedArticleToReadLater(), _ => _currentSelectedItem != null);
            MarkSelectedArticleUnreadCommand = new RelayCommand(_ => MarkSelectedArticleUnread(), _ => _currentSelectedItem != null);
            OpenSelectedArticleInWindowCommand = new RelayCommand(_ => _ = OpenSelectedArticleInWindowAsync(), _ => _currentSelectedItem != null);
            ToggleDarkModeCommand = new RelayCommand(_ => ToggleDarkModeQuick());
            ShowImportantNotificationsCommand = new RelayCommand(_ => ShowLatestImportantNotificationsWindow());
            NavigateSelectionCommand = new RelayCommand(param => MoveSelection(param), _ => true);
            NavigateFeedCommand = new RelayCommand(param => MoveSelectionByFeed(param), _ => true);

            ApplyActiveDarkMode();

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
            _embeddedBrowserSession ??= _browserService.CreateSession();
            _embeddedBrowserSession.SetDarkMode(_settings.DarkMode);
            _embeddedBrowserSession.SetAdBlockerEnabled(_settings.AdBlockerEnabled);
            _embeddedBrowserSession.SetMediaPlaybackEnabled(_isEmbeddedVideoPlaybackEnabled);
            _embeddedBrowserSession.SetWebView(webView);

            if (_linkWebView != null)
            {
                if (_linkWebView.CoreWebView2 != null)
                {
                    _linkWebView.CoreWebView2.DOMContentLoaded -= LinkWebView_DOMContentLoaded;
                    _linkWebView.CoreWebView2.ContentLoading -= LinkWebView_ContentLoading;
                    _linkWebView.CoreWebView2.NavigationStarting -= LinkWebView_NavigationStarting;
                    _linkWebView.CoreWebView2.NavigationCompleted -= LinkWebView_NavigationCompleted;
                    _linkWebView.CoreWebView2.DOMContentLoaded += LinkWebView_DOMContentLoaded;
                    _linkWebView.CoreWebView2.ContentLoading += LinkWebView_ContentLoading;
                    _linkWebView.CoreWebView2.NavigationStarting += LinkWebView_NavigationStarting;
                    _linkWebView.CoreWebView2.NavigationCompleted += LinkWebView_NavigationCompleted;
                    return;
                }

                _linkWebView.CoreWebView2InitializationCompleted += (s, e) =>
                {
                    if (e.IsSuccess)
                    {
                        _embeddedBrowserSession?.SetDarkMode(_settings.DarkMode);
                        _embeddedBrowserSession?.SetAdBlockerEnabled(_settings.AdBlockerEnabled);
                        _linkWebView.CoreWebView2.DOMContentLoaded -= LinkWebView_DOMContentLoaded;
                        _linkWebView.CoreWebView2.ContentLoading -= LinkWebView_ContentLoading;
                        _linkWebView.CoreWebView2.NavigationStarting -= LinkWebView_NavigationStarting;
                        _linkWebView.CoreWebView2.NavigationCompleted -= LinkWebView_NavigationCompleted;
                        _linkWebView.CoreWebView2.DOMContentLoaded += LinkWebView_DOMContentLoaded;
                        _linkWebView.CoreWebView2.ContentLoading += LinkWebView_ContentLoading;
                        _linkWebView.CoreWebView2.NavigationStarting += LinkWebView_NavigationStarting;
                        _linkWebView.CoreWebView2.NavigationCompleted += LinkWebView_NavigationCompleted;
                    }
                };
            }
        }

        private void LinkWebView_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Uri) ||
                e.Uri.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _isBrowserTransitionVisible = true;
            IsBrowserPageLoading = true;
            OnPropertyChanged(nameof(ShowBrowserLoadingOverlay));
        }

        private void LinkWebView_ContentLoading(object sender, CoreWebView2ContentLoadingEventArgs e)
        {
        }

        private void LinkWebView_DOMContentLoaded(object sender, CoreWebView2DOMContentLoadedEventArgs e)
        {
            if (_isBrowserTransitionVisible)
            {
                _isBrowserTransitionVisible = false;
                OnPropertyChanged(nameof(ShowBrowserLoadingOverlay));
            }
        }

        private void LinkWebView_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            IsBrowserPageLoading = false;
            if (!e.IsSuccess && _isBrowserTransitionVisible)
            {
                _isBrowserTransitionVisible = false;
                OnPropertyChanged(nameof(ShowBrowserLoadingOverlay));
            }

            if (!string.IsNullOrWhiteSpace(_pendingBrowserNavigationUrl))
            {
                var pendingUrl = _pendingBrowserNavigationUrl;
                var pendingUseClear = _pendingBrowserNavigationUseClear;
                _pendingBrowserNavigationUrl = null;
                _pendingBrowserNavigationUseClear = false;

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null)
                {
                    ScheduleBrowserNavigation(pendingUrl, pendingUseClear);
                    return;
                }

                dispatcher.BeginInvoke(new Action(() => ScheduleBrowserNavigation(pendingUrl, pendingUseClear)), DispatcherPriority.Background);
            }
        }

        public void SetArticleWebView(Microsoft.Web.WebView2.Wpf.WebView2 webView)
        {
            _articleWebView = webView;

            if (_articleWebView?.CoreWebView2 != null)
            {
                ConfigureArticleWebView();
            }
            else
            {
                _articleWebView.CoreWebView2InitializationCompleted += (s, e) =>
                {
                    if (e.IsSuccess)
                    {
                        ConfigureArticleWebView();
                    }
                };
            }
        }

        private void ConfigureArticleWebView()
        {
            if (_articleWebView?.CoreWebView2 == null)
            {
                return;
            }

            _articleWebView.CoreWebView2.Settings.IsScriptEnabled = false;
            _articleWebView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
            _articleWebView.CoreWebView2.NavigationStarting -= OnArticleNavigationStarting;
            _articleWebView.CoreWebView2.NewWindowRequested -= OnArticleNewWindowRequested;
            _articleWebView.CoreWebView2.NavigationStarting += OnArticleNavigationStarting;
            _articleWebView.CoreWebView2.NewWindowRequested += OnArticleNewWindowRequested;
            _articleWebView.ZoomFactorChanged -= ArticleWebView_ZoomFactorChanged;
            _articleWebView.ZoomFactorChanged += ArticleWebView_ZoomFactorChanged;
            ApplyArticleWebViewTheme();
            ApplyCurrentArticleZoomFactor();
        }

        private void ArticleWebView_ZoomFactorChanged(object sender, EventArgs e)
        {
            if (_isApplyingArticleZoomFactor)
            {
                return;
            }

            SaveCurrentArticleZoomFactor();
        }

        public void SaveCurrentArticleZoomFactor()
        {
            if (_settings == null || _articleWebView == null)
            {
                return;
            }

            var zoom = ClampArticleZoomFactor(_articleWebView.ZoomFactor);
            var currentZoom = _isReaderModeActive
                ? NormalizeArticleZoomFactor(_settings.ReaderZoomFactor)
                : NormalizeArticleZoomFactor(_settings.FeedSummaryZoomFactor);
            if (Math.Abs(currentZoom - zoom) < 0.001)
            {
                return;
            }

            if (_isReaderModeActive)
            {
                _settings.ReaderZoomFactor = zoom;
            }
            else
            {
                _settings.FeedSummaryZoomFactor = zoom;
            }

            SaveSettingsPreservingImportantNotifications();
        }

        public void AdjustCurrentArticleZoomFactor(int direction)
        {
            if (_settings == null || _articleWebView == null || direction == 0)
            {
                return;
            }

            var currentZoom = _isReaderModeActive
                ? NormalizeArticleZoomFactor(_settings.ReaderZoomFactor)
                : NormalizeArticleZoomFactor(_settings.FeedSummaryZoomFactor);
            var nextZoom = ClampArticleZoomFactor(Math.Round((currentZoom + (direction > 0 ? 0.1 : -0.1)) * 10.0) / 10.0);
            if (Math.Abs(currentZoom - nextZoom) < 0.001)
            {
                return;
            }

            if (_isReaderModeActive)
            {
                _settings.ReaderZoomFactor = nextZoom;
            }
            else
            {
                _settings.FeedSummaryZoomFactor = nextZoom;
                RaiseFeedSummaryZoomPropertiesChanged();
            }

            SaveSettingsPreservingImportantNotifications();
            ApplyCurrentArticleZoomFactor();
        }

        private void ApplyCurrentArticleZoomFactor()
        {
            if (_articleWebView == null || _settings == null)
            {
                return;
            }

            var zoom = _isReaderModeActive
                ? NormalizeArticleZoomFactor(_settings.ReaderZoomFactor)
                : NormalizeArticleZoomFactor(_settings.FeedSummaryZoomFactor);
            if (Math.Abs(_articleWebView.ZoomFactor - zoom) >= 0.001)
            {
                try
                {
                    _isApplyingArticleZoomFactor = true;
                    _articleWebView.ZoomFactor = zoom;
                }
                finally
                {
                    _isApplyingArticleZoomFactor = false;
                }
            }
        }

        private static double NormalizeArticleZoomFactor(double zoom)
        {
            return ClampArticleZoomFactor(zoom <= 0 ? 1.0 : zoom);
        }

        private static double ClampArticleZoomFactor(double zoom)
        {
            return Math.Max(0.5, Math.Min(3.0, zoom));
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

        private bool CanToggleReaderMode()
        {
            if (_isReaderModeLoading || _currentSelectedItem == null)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(NormalizeExternalLink(_currentSelectedItem.Link));
        }

        private void SetReaderModeActive(bool value)
        {
            if (_isReaderModeActive == value)
            {
                return;
            }

            _isReaderModeActive = value;
            if (!value)
            {
                SetReaderViewReady(false);
            }
            OnPropertyChanged(nameof(ReaderModeButtonText));
            OnPropertyChanged(nameof(IsEmbeddedVideoButtonVisible));
            OnPropertyChanged(nameof(IsArticleWebViewVisible));
            OnPropertyChanged(nameof(IsFeedSummaryVisible));
            (EnableEmbeddedVideoCommand as RelayCommand)?.RaiseCanExecuteChanged();

            if (value)
            {
                ResetEmbeddedVideoPlayback();
            }

            ApplyCurrentArticleZoomFactor();
        }

        private void SetReaderViewReady(bool value)
        {
            if (_isReaderViewReady == value)
            {
                return;
            }

            _isReaderViewReady = value;
            OnPropertyChanged(nameof(IsArticleWebViewVisible));
            OnPropertyChanged(nameof(IsFeedSummaryVisible));
        }

        private void RaiseFeedSummaryPropertiesChanged()
        {
            OnPropertyChanged(nameof(FeedSummaryTitle));
            OnPropertyChanged(nameof(FeedSummaryFeedName));
            OnPropertyChanged(nameof(FeedSummaryPublicationDate));
            OnPropertyChanged(nameof(FeedSummaryDescription));
            OnPropertyChanged(nameof(FeedSummaryLink));
        }

        private void RaiseFeedSummaryZoomPropertiesChanged()
        {
            OnPropertyChanged(nameof(FeedSummaryTitleFontSize));
            OnPropertyChanged(nameof(FeedSummaryMetaFontSize));
            OnPropertyChanged(nameof(FeedSummaryBodyFontSize));
            OnPropertyChanged(nameof(FeedSummaryBodyLineHeight));
        }

        private void SetReaderModeLoading(bool value)
        {
            if (_isReaderModeLoading == value)
            {
                return;
            }

            _isReaderModeLoading = value;
            OnPropertyChanged(nameof(ReaderModeButtonText));
            (ToggleReaderModeCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void RestoreFeedSummaryView()
        {
            if (_currentSelectedItem == null)
            {
                return;
            }

            SelectedArticleHtml = GetOrCreateArticleSummaryHtml(_currentSelectedItem);
            SelectedArticleText = GetOrCreateArticlePlainText(_currentSelectedItem);
            ResetEmbeddedVideoPlayback();
        }

        private void ResetEmbeddedVideoPlayback()
        {
            if (!_isEmbeddedVideoPlaybackEnabled && (_embeddedBrowserSession == null || !_embeddedBrowserSession.IsMediaPlaybackEnabled))
            {
                return;
            }

            _isEmbeddedVideoPlaybackEnabled = false;
            _embeddedBrowserSession?.SetMediaPlaybackEnabled(false);
            OnPropertyChanged(nameof(EnableVideoButtonText));
            (EnableEmbeddedVideoCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private async Task ToggleReaderModeAsync()
        {
            if (_currentSelectedItem == null)
            {
                return;
            }

            if (_isReaderModeActive)
            {
                SetReaderModeActive(false);
                RestoreFeedSummaryView();
                return;
            }

            var selectedItem = _currentSelectedItem;
            var selectedLink = NormalizeExternalLink(selectedItem.Link);
            if (string.IsNullOrWhiteSpace(selectedLink))
            {
                SnackbarMessageQueue?.Enqueue("Reader Mode: article URL is not available.");
                return;
            }

            SetReaderModeLoading(true);
            var readerNavigationStarted = false;
            try
            {
                if (!_readerContentCache.TryGetValue(selectedLink, out var readerContent))
                {
                    readerContent = await _articleReaderService.ExtractAsync(selectedLink);
                    if (readerContent != null)
                    {
                        _readerContentCache[selectedLink] = readerContent;
                    }
                }

                if (readerContent == null || string.IsNullOrWhiteSpace(readerContent.HtmlContent))
                {
                    SnackbarMessageQueue?.Enqueue("Reader Mode: full text could not be extracted for this article.");
                    return;
                }

                // User changed selection while extraction was running.
                if (!ReferenceEquals(selectedItem, _currentSelectedItem))
                {
                    return;
                }

                var readerItem = new FeedItem
                {
                    FeedName = selectedItem.FeedName,
                    FeedUrl = selectedItem.FeedUrl,
                    Title = string.IsNullOrWhiteSpace(readerContent.Title) ? selectedItem.Title : readerContent.Title,
                    Description = readerContent.HtmlContent,
                    Link = selectedItem.Link,
                    PublicationDate = selectedItem.PublicationDate,
                    IsRead = selectedItem.IsRead,
                    IsPinned = selectedItem.IsPinned,
                    IsReadLater = selectedItem.IsReadLater,
                    IsArchived = selectedItem.IsArchived,
                    ArchivedAt = selectedItem.ArchivedAt
                };

                SetReaderModeActive(true);
                SetReaderViewReady(false);
                SelectedArticleHtml = CreateArticleHtml(readerItem);
                readerNavigationStarted = true;
                SelectedArticleText = string.IsNullOrWhiteSpace(readerContent.PlainText)
                    ? BuildArticlePlainText(readerItem.Title, readerItem.Description)
                    : readerContent.PlainText;
            }
            catch (Exception)
            {
                SnackbarMessageQueue?.Enqueue("Reader Mode: failed to load article content.");
            }
            finally
            {
                if (!readerNavigationStarted)
                {
                    SetReaderModeLoading(false);
                }
            }
        }

        public void OnArticleSelected(
            FeedItem feedItem,
            bool enableAutoScroll = true,
            bool preserveOpenWebViews = false)
        {
            if (feedItem == null)
            {
                return;
            }

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
            RaiseSelectionShortcutCommandStates();
            RaiseFeedSummaryPropertiesChanged();

            var normalizedLink = NormalizeExternalLink(feedItem.Link);
            var preserveCurrentViewState =
                preserveOpenWebViews &&
                !string.IsNullOrWhiteSpace(normalizedLink) &&
                string.Equals(normalizedLink, SelectedArticleLink, StringComparison.OrdinalIgnoreCase);

            if (!preserveCurrentViewState)
            {
                ResetEmbeddedVideoPlayback();
                SetReaderModeActive(false);
                SelectedArticleHtml = GetOrCreateArticleSummaryHtml(feedItem);
                SelectedArticleText = GetOrCreateArticlePlainText(feedItem);
                SelectedArticleLink = normalizedLink;
                (ToggleReaderModeCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }

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

            if (preserveCurrentViewState)
            {
                return;
            }

            if (IsShowContentAlwaysOn)
            {
                IsBrowserVisible = true;

                try
                {
                    if (!string.IsNullOrEmpty(SelectedArticleLink))
                    {
                        ScheduleBrowserNavigation(SelectedArticleLink, useClearNavigation: false);
                    }
                }
                catch (Exception)
                {
                    // Ignore navigation failures; the browser fallback handles external links.
                }
            }
            else
            {
                // Keep browser pane only for refresh-driven silent restores.
                if (preserveOpenWebViews && IsBrowserVisible && !string.IsNullOrEmpty(SelectedArticleLink))
                {
                    try
                    {
                        ScheduleBrowserNavigation(SelectedArticleLink, useClearNavigation: false);
                    }
                    catch (Exception)
                    {
                        // Ignore navigation failures; user can still retry with Show Content.
                    }
                }
                else
                {
                    ResetEmbeddedVideoPlayback();
                    ClearBrowserContent();
                    IsBrowserVisible = false;
                }
            }

        }

        private Task OpenArticleInWindowAsync(FeedItem feedItem)
        {
            if (feedItem == null)
            {
                return Task.CompletedTask;
            }

            try
            {
                OnArticleSelected(feedItem);

                var articleWindow = new ArticleWindow(
                    feedItem.Title,
                    SelectedArticleHtml,
                    SelectedArticleLink,
                    DarkMode,
                    CreateBrowserSession(),
                    AdBlockerEnabled,
                    new Action<string>(PromptOpenExternalLink))
                {
                    Owner = Application.Current?.MainWindow
                };

                articleWindow.Show();
            }
            catch (Exception)
            {
                SnackbarMessageQueue?.Enqueue("Could not open article in a separate window.");
            }

            return Task.CompletedTask;
        }

        private void PinSelectedArticle()
        {
            if (_currentSelectedItem == null)
            {
                return;
            }

            PinArticle(_currentSelectedItem);
        }

        private void AddSelectedArticleToReadLater()
        {
            if (_currentSelectedItem == null)
            {
                return;
            }

            AddToReadLater(_currentSelectedItem);
        }

        private void MarkSelectedArticleUnread()
        {
            if (_currentSelectedItem == null)
            {
                return;
            }

            MarkAsUnread(_currentSelectedItem);
        }

        private void ArchiveSelectedArticle()
        {
            if (_currentSelectedItem == null)
            {
                return;
            }

            ArchiveArticle(_currentSelectedItem);
        }

        private async Task OpenSelectedArticleInWindowAsync()
        {
            if (_currentSelectedItem == null)
            {
                return;
            }

            await OpenArticleInWindowAsync(_currentSelectedItem);
        }

        private void ToggleDarkModeQuick()
        {
            _settings.DarkMode = !_settings.DarkMode;
            _pendingDarkMode = _settings.DarkMode;
            SaveSettingsPreservingImportantNotifications();
            OnPropertyChanged(nameof(DarkMode));
            OnPropertyChanged(nameof(PendingDarkMode));
            ApplyActiveDarkMode();
        }

        private void ShowLatestImportantNotificationsWindow()
        {
            try
            {
                _desktopNotificationService?.ShowLatestImportantFeedsWindow();
            }
            catch (Exception)
            {
                // Ignore tray window errors to keep shortcuts non-disruptive.
            }
        }

        public ArchiveViewPreferences GetArchiveViewPreferences()
        {
            return _settings?.ArchiveViewPreferences?.Clone() ?? new ArchiveViewPreferences();
        }

        public FeedAllWindowPreferences GetFeedAllWindowPreferences()
        {
            return _settings?.FeedAllWindowPreferences?.Clone() ?? new FeedAllWindowPreferences();
        }

        public FeedManagerWindowPreferences GetFeedManagerWindowPreferences()
        {
            return _settings?.FeedManagerWindowPreferences?.Clone() ?? new FeedManagerWindowPreferences();
        }

        public IReadOnlyList<ArticleLabelDefinition> GetArticleLabels()
        {
            return (_settings?.ArticleLabels ?? new List<ArticleLabelDefinition>())
                .Where(label => label != null && !string.IsNullOrWhiteSpace(label.Name))
                .Select(label => label.Clone())
                .OrderBy(label => label.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public IReadOnlyList<SavedLabelColorDefinition> GetSavedLabelColors()
        {
            var savedDefinitions = (_settings?.SavedLabelColorDefinitions ?? new List<SavedLabelColorDefinition>())
                .Where(color => color != null && !string.IsNullOrWhiteSpace(color.Name) && !string.IsNullOrWhiteSpace(color.ColorHex))
                .Select(color => color.Clone());

            var legacyDefinitions = (_settings?.SavedLabelColors ?? new List<string>())
                .Where(color => !string.IsNullOrWhiteSpace(color))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(color => new SavedLabelColorDefinition
                {
                    Name = color.Trim(),
                    ColorHex = color.Trim()
                });

            return savedDefinitions
                .Concat(legacyDefinitions)
                .GroupBy(color => color.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(color => color.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public string GetArticleNote(FeedItem item)
        {
            var key = item?.Link?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            return ResolveArticleNoteForLink(key);
        }

        public IReadOnlyList<ArchiveSavedView> GetArchiveSavedViews()
        {
            return (_settings?.ArchiveSavedViews ?? new List<ArchiveSavedView>())
                .Where(view => view != null && !string.IsNullOrWhiteSpace(view.Name))
                .Select(view => view.Clone())
                .OrderBy(view => view.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public ArchiveSavedView GetArchiveSavedView(string name)
        {
            if (_settings?.ArchiveSavedViews == null || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            return _settings.ArchiveSavedViews
                .FirstOrDefault(view => string.Equals(view?.Name?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase))
                ?.Clone();
        }

        public void SaveArchiveViewPreferences(ArchiveViewPreferences preferences)
        {
            if (_settings == null || preferences == null)
            {
                return;
            }

            _settings.ArchiveViewPreferences = preferences.Clone();
            SaveSettingsPreservingImportantNotifications();
        }

        public void SaveFeedAllWindowPreferences(FeedAllWindowPreferences preferences)
        {
            if (_settings == null || preferences == null)
            {
                return;
            }

            _settings.FeedAllWindowPreferences = preferences.Clone();
            SaveSettingsPreservingImportantNotifications();
        }

        public void SaveFeedManagerWindowPreferences(FeedManagerWindowPreferences preferences)
        {
            if (_settings == null || preferences == null)
            {
                return;
            }

            _settings.FeedManagerWindowPreferences = preferences.Clone();
            SaveSettingsPreservingImportantNotifications();
        }

        public async Task<List<FeedItem>> FetchAvailableArticlesForFeedAsync(string feedName, int maxItems = 500)
        {
            if (string.IsNullOrWhiteSpace(feedName))
            {
                return new List<FeedItem>();
            }

            var feed = _feeds?.FirstOrDefault(f =>
                f?.IsEnabled == true &&
                string.Equals(f.Name?.Trim(), feedName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (feed == null)
            {
                return new List<FeedItem>();
            }

            var advertisementKeywords = _settings.AdvertisementFilterEnabled
                ? GetAdvertisementKeywordsForFiltering()
                : new List<string>();

            var items = await _feedService.FetchArticlesForFeedAsync(
                feed,
                keywordFilter: null,
                maxItems: Math.Max(1, maxItems),
                advertisementKeywords: advertisementKeywords.Count > 0 ? advertisementKeywords : null).ConfigureAwait(false);

            var deduplicated = DeduplicateLinear(items)
                .OrderByDescending(item => item?.PublicationDate ?? DateTime.MinValue)
                .ToList();

            foreach (var item in deduplicated)
            {
                ApplyStoredStateToExternalItem(item);
            }

            return deduplicated;
        }

        public List<string> GetAdvertisementKeywordsSnapshot()
        {
            return _settings?.AdvertisementFilterEnabled == true
                ? GetAdvertisementKeywordsForFiltering()
                : new List<string>();
        }

        public string CreateArticleHtmlForPreview(FeedItem item)
        {
            return item == null ? string.Empty : CreateArticleHtml(item);
        }

        public void MarkArticleAsReadFromExternalView(FeedItem item)
        {
            if (item == null || item.IsRead)
            {
                return;
            }

            item.IsRead = true;
            var key = item.Link?.Trim();
            if (!string.IsNullOrWhiteSpace(key))
            {
                _readArticleLinks.Add(key);
                foreach (var trackedItem in EnumerateAllTrackedItems().Where(tracked =>
                             string.Equals(tracked.Link?.Trim(), key, StringComparison.OrdinalIgnoreCase)))
                {
                    trackedItem.IsRead = true;
                }
                PersistReadState();
            }
        }

        public void ApplyStoredStateToExternalViewItem(FeedItem item)
        {
            ApplyStoredStateToExternalItem(item);
        }

        public void SaveArticleLabel(string name, string colorHex)
        {
            if (_settings == null || string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            _settings.ArticleLabels ??= new List<ArticleLabelDefinition>();
            var normalizedName = name.Trim();
            var normalizedColor = string.IsNullOrWhiteSpace(colorHex) ? "#7C3AED" : colorHex.Trim();
            var existing = _settings.ArticleLabels
                .FirstOrDefault(label => string.Equals(label?.Name?.Trim(), normalizedName, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                _settings.ArticleLabels.Add(new ArticleLabelDefinition
                {
                    Name = normalizedName,
                    ColorHex = normalizedColor
                });
            }
            else
            {
                existing.Name = normalizedName;
                existing.ColorHex = normalizedColor;
            }

            ApplyStoredLabelsToAllKnownItems();
            SaveSettingsPreservingImportantNotifications();
            ArticleLabelsChanged?.Invoke();
            OnArchiveItemsChanged();
        }

        public void SaveCustomLabelColor(string name, string colorHex)
        {
            if (_settings == null || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(colorHex))
            {
                return;
            }

            _settings.SavedLabelColorDefinitions ??= new List<SavedLabelColorDefinition>();
            _settings.SavedLabelColors ??= new List<string>();

            var normalizedName = name.Trim();
            var normalizedColor = colorHex.Trim();
            var existing = _settings.SavedLabelColorDefinitions
                .FirstOrDefault(color => string.Equals(color?.Name?.Trim(), normalizedName, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                _settings.SavedLabelColorDefinitions.Add(new SavedLabelColorDefinition
                {
                    Name = normalizedName,
                    ColorHex = normalizedColor
                });
            }
            else
            {
                existing.Name = normalizedName;
                existing.ColorHex = normalizedColor;
            }

            _settings.SavedLabelColors.RemoveAll(color => string.Equals(color?.Trim(), normalizedColor, StringComparison.OrdinalIgnoreCase));
            SaveSettingsPreservingImportantNotifications();
            ArticleLabelsChanged?.Invoke();
        }

        public bool DeleteCustomLabelColor(string name)
        {
            if (_settings == null || string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            _settings.SavedLabelColorDefinitions ??= new List<SavedLabelColorDefinition>();
            var normalizedName = name.Trim();
            var removed = _settings.SavedLabelColorDefinitions.RemoveAll(color =>
                string.Equals(color?.Name?.Trim(), normalizedName, StringComparison.OrdinalIgnoreCase));
            if (removed <= 0)
            {
                return false;
            }

            SaveSettingsPreservingImportantNotifications();
            ArticleLabelsChanged?.Invoke();
            return true;
        }

        public void RenameArticleLabel(string oldName, string newName, string colorHex)
        {
            if (_settings == null || string.IsNullOrWhiteSpace(newName))
            {
                return;
            }

            var normalizedOldName = oldName?.Trim();
            var normalizedNewName = newName.Trim();
            if (string.IsNullOrWhiteSpace(normalizedOldName) ||
                string.Equals(normalizedOldName, normalizedNewName, StringComparison.OrdinalIgnoreCase))
            {
                SaveArticleLabel(normalizedNewName, colorHex);
                return;
            }

            _settings.ArticleLabels ??= new List<ArticleLabelDefinition>();
            _settings.ArticleLabelAssignments ??= new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var key in _settings.ArticleLabelAssignments.Keys.ToList())
            {
                var nextValues = (_settings.ArticleLabelAssignments[key] ?? new List<string>())
                    .Select(value => string.Equals(value?.Trim(), normalizedOldName, StringComparison.OrdinalIgnoreCase)
                        ? normalizedNewName
                        : value?.Trim())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                _settings.ArticleLabelAssignments[key] = nextValues;
            }

            _settings.ArticleLabels.RemoveAll(label =>
                string.Equals(label?.Name?.Trim(), normalizedOldName, StringComparison.OrdinalIgnoreCase));
            SaveArticleLabel(normalizedNewName, colorHex);
        }

        public bool DeleteArticleLabel(string name)
        {
            if (_settings == null || string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            var normalizedName = name.Trim();
            var removedLabel = _settings.ArticleLabels?.RemoveAll(
                label => string.Equals(label?.Name?.Trim(), normalizedName, StringComparison.OrdinalIgnoreCase)) ?? 0;
            var assignmentsChanged = false;

            if (_settings.ArticleLabelAssignments != null)
            {
                foreach (var key in _settings.ArticleLabelAssignments.Keys.ToList())
                {
                    var currentValues = _settings.ArticleLabelAssignments[key] ?? new List<string>();
                    var nextValues = currentValues
                        .Where(value => !string.Equals(value?.Trim(), normalizedName, StringComparison.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (nextValues.Count == currentValues.Count)
                    {
                        continue;
                    }

                    assignmentsChanged = true;
                    if (nextValues.Count == 0)
                    {
                        _settings.ArticleLabelAssignments.Remove(key);
                    }
                    else
                    {
                        _settings.ArticleLabelAssignments[key] = nextValues;
                    }
                }
            }

            if (removedLabel <= 0 && !assignmentsChanged)
            {
                return false;
            }

            ApplyStoredLabelsToAllKnownItems();
            SaveSettingsPreservingImportantNotifications();
            ArticleLabelsChanged?.Invoke();
            OnArchiveItemsChanged();
            return true;
        }

        public void SetArticleLabels(IEnumerable<FeedItem> items, IEnumerable<string> labelNames)
        {
            if (_settings == null)
            {
                return;
            }

            var normalizedLabels = (labelNames ?? Enumerable.Empty<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var keys = (items ?? Enumerable.Empty<FeedItem>())
                .Select(item => item?.Link?.Trim())
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (keys.Count == 0)
            {
                return;
            }

            _settings.ArticleLabelAssignments ??= new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in keys)
            {
                if (normalizedLabels.Count == 0)
                {
                    _settings.ArticleLabelAssignments.Remove(key);
                }
                else
                {
                    _settings.ArticleLabelAssignments[key] = new List<string>(normalizedLabels);
                }

                ApplyStoredLabelsToLink(key);
            }

            SaveSettingsPreservingImportantNotifications();
            ArticleLabelsChanged?.Invoke();
            OnArchiveItemsChanged();
        }

        public void ToggleArticleLabel(IEnumerable<FeedItem> items, string labelName)
        {
            if (_settings == null || string.IsNullOrWhiteSpace(labelName))
            {
                return;
            }

            var normalizedLabel = labelName.Trim();
            var materializedItems = (items ?? Enumerable.Empty<FeedItem>())
                .Where(item => item != null)
                .Distinct()
                .ToList();
            if (materializedItems.Count == 0)
            {
                return;
            }

            _settings.ArticleLabelAssignments ??= new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var shouldRemove = materializedItems.All(item =>
                GetArticleLabelNames(item).Any(existing => string.Equals(existing, normalizedLabel, StringComparison.OrdinalIgnoreCase)));

            foreach (var item in materializedItems)
            {
                var key = item.Link?.Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                var nextLabels = GetArticleLabelNames(item)
                    .Where(existing => !shouldRemove || !string.Equals(existing, normalizedLabel, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (!shouldRemove && !nextLabels.Any(existing => string.Equals(existing, normalizedLabel, StringComparison.OrdinalIgnoreCase)))
                {
                    nextLabels.Add(normalizedLabel);
                }

                if (nextLabels.Count == 0)
                {
                    _settings.ArticleLabelAssignments.Remove(key);
                }
                else
                {
                    _settings.ArticleLabelAssignments[key] = nextLabels
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
                }

                ApplyStoredLabelsToLink(key);
            }

            SaveSettingsPreservingImportantNotifications();
            ArticleLabelsChanged?.Invoke();
            OnArchiveItemsChanged();
        }

        public IReadOnlyList<string> GetArticleLabelNames(FeedItem item)
        {
            var key = item?.Link?.Trim();
            if (_settings?.ArticleLabelAssignments == null || string.IsNullOrWhiteSpace(key))
            {
                return Array.Empty<string>();
            }

            return _settings.ArticleLabelAssignments.TryGetValue(key, out var names)
                ? names.Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList()
                : Array.Empty<string>();
        }

        public void SetArticleNote(FeedItem item, string note)
        {
            if (_settings == null || item == null)
            {
                return;
            }

            var key = item.Link?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            var normalizedNote = note?.Trim() ?? string.Empty;
            _settings.ArticleNoteAssignments ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(normalizedNote))
            {
                _settings.ArticleNoteAssignments.Remove(key);
            }
            else
            {
                _settings.ArticleNoteAssignments[key] = normalizedNote;
            }

            ApplyStoredNoteToLink(key);
            SaveSettingsPreservingImportantNotifications();
            ArticleNotesChanged?.Invoke();
            OnArchiveItemsChanged();
        }

        public void SaveArchiveSavedView(string name, ArchiveViewPreferences preferences)
        {
            if (_settings == null || preferences == null || string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            _settings.ArchiveSavedViews ??= new List<ArchiveSavedView>();
            var normalizedName = name.Trim();
            var savedViewPreferences = preferences.Clone();
            savedViewPreferences.WindowState = "maximized";
            savedViewPreferences.WindowWidth = null;
            savedViewPreferences.WindowHeight = null;
            savedViewPreferences.WindowLeft = null;
            savedViewPreferences.WindowTop = null;
            var existing = _settings.ArchiveSavedViews
                .FirstOrDefault(view => string.Equals(view?.Name?.Trim(), normalizedName, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                _settings.ArchiveSavedViews.Add(new ArchiveSavedView
                {
                    Name = normalizedName,
                    Preferences = savedViewPreferences
                });
            }
            else
            {
                existing.Name = normalizedName;
                existing.Preferences = savedViewPreferences;
            }

            SaveSettingsPreservingImportantNotifications();
        }

        public bool DeleteArchiveSavedView(string name)
        {
            if (_settings?.ArchiveSavedViews == null || string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            var removed = _settings.ArchiveSavedViews.RemoveAll(
                view => string.Equals(view?.Name?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (removed <= 0)
            {
                return false;
            }

            SaveSettingsPreservingImportantNotifications();
            return true;
        }

        public string GetNormalizedArticleLink(FeedItem item)
        {
            return NormalizeExternalLink(item?.Link);
        }

        public bool TryOpenArticleExternally(FeedItem item)
        {
            var normalizedLink = NormalizeExternalLink(item?.Link);
            if (string.IsNullOrWhiteSpace(normalizedLink))
            {
                return false;
            }

            return _browserService.TryOpenExternalLink(normalizedLink);
        }

        public void PromptOpenExternalLink(string url)
        {
            TryPromptOpenExternal(url);
        }

        public bool GetArchiveAutoCleanupEnabled()
        {
            return _settings?.ArchiveAutoCleanupEnabled == true;
        }

        public int GetArchiveAutoCleanupDays()
        {
            return NormalizeArchiveAutoCleanupDays(_settings?.ArchiveAutoCleanupDays ?? DefaultArchiveAutoCleanupDays);
        }

        public void SaveArchiveAutoCleanupSettings(bool enabled, int days)
        {
            if (_settings == null)
            {
                return;
            }

            _settings.ArchiveAutoCleanupEnabled = enabled;
            _settings.ArchiveAutoCleanupDays = NormalizeArchiveAutoCleanupDays(days);
            SaveSettingsPreservingImportantNotifications();
        }

        private void ApplyStoredStateToExternalItem(FeedItem item)
        {
            if (item == null)
            {
                return;
            }

            ApplyStoredLabelsToItem(item);
            ApplyStoredNoteToItem(item);

            var key = item.Link?.Trim();
            item.IsRead = !string.IsNullOrWhiteSpace(key) && _readArticleLinks.Contains(key);

            if (string.IsNullOrWhiteSpace(key))
            {
                item.IsPinned = false;
                item.IsReadLater = false;
                item.IsArchived = false;
                item.ArchivedAt = null;
                return;
            }

            var archivedAt = (_settings?.ArchivedArticleSnapshots ?? new List<FeedItem>())
                .Where(snapshot => string.Equals(snapshot?.Link?.Trim(), key, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(snapshot => snapshot?.ArchivedAt ?? DateTime.MinValue)
                .Select(snapshot => snapshot?.ArchivedAt)
                .FirstOrDefault();

            if (_archivedArticleLinks.Contains(key))
            {
                item.IsArchived = true;
                item.IsPinned = false;
                item.IsReadLater = false;
                item.ArchivedAt = archivedAt ?? item.ArchivedAt ?? ResolveArchiveRetentionDate(item) ?? DateTime.Now;
                return;
            }

            if (_pinnedArticleLinks.Contains(key))
            {
                item.IsPinned = true;
                item.IsReadLater = false;
                item.IsArchived = false;
                item.ArchivedAt = null;
                return;
            }

            if (_readLaterArticleLinks.Contains(key))
            {
                item.IsReadLater = true;
                item.IsPinned = false;
                item.IsArchived = false;
                item.ArchivedAt = null;
                return;
            }

            item.IsPinned = false;
            item.IsReadLater = false;
            item.IsArchived = false;
            item.ArchivedAt = null;
        }

        public int ApplyArchiveAutoCleanupPolicy()
        {
            var removedCount = DeleteArchivedKeys(GetArchivedKeysOlderThanConfiguredRetention(requireEnabled: false), refreshUi: true);
            if (removedCount > 0)
            {
                SnackbarMessageQueue?.Enqueue($"Archive cleanup deleted {removedCount} archived article{(removedCount == 1 ? string.Empty : "s")}.");
            }

            return removedCount;
        }

        private void ShowLibraryWindow(LibrarySectionMode initialMode)
        {
            try
            {
                if (_archiveWindow == null || !_archiveWindow.IsLoaded)
                {
                    var archivePreferences = GetArchiveViewPreferences();
                    _archiveWindow = new ArchiveWindow(initialMode)
                    {
                        Owner = Application.Current?.MainWindow,
                        DataContext = this
                    };
                    _archiveWindow.PrepareInitialWindowPlacement(archivePreferences);
                    _archiveWindow.Closed += ArchiveWindowOnClosed;
                    _archiveWindow.Show();
                    return;
                }

                if (_archiveWindow.WindowState == WindowState.Minimized)
                {
                    _archiveWindow.WindowState = WindowState.Normal;
                }

                _archiveWindow.SwitchToMode(initialMode);
                _archiveWindow.Activate();
            }
            catch (Exception)
            {
                SnackbarMessageQueue?.Enqueue("Could not open library window.");
            }
        }

        private void ShowFeedAllWindow(FeedGroupViewModel feedGroup)
        {
            if (feedGroup == null || string.IsNullOrWhiteSpace(feedGroup.Name))
            {
                return;
            }

            try
            {
                var feed = _feeds?.FirstOrDefault(f =>
                    f?.IsEnabled == true &&
                    string.Equals(f.Name?.Trim(), feedGroup.Name.Trim(), StringComparison.OrdinalIgnoreCase));
                if (feed == null)
                {
                    SnackbarMessageQueue?.Enqueue("Could not find the selected feed.");
                    return;
                }

                ShowFeedAllWindow(feed);
            }
            catch (Exception)
            {
                SnackbarMessageQueue?.Enqueue("Could not open feed window.");
            }
        }

        private void ShowFeedAllWindow(Feed feed)
        {
            if (feed == null || string.IsNullOrWhiteSpace(feed.Url))
            {
                return;
            }

            try
            {
                var feedSnapshot = CloneFeedSnapshot(feed);
                var window = new FeedAllWindow(this, feedSnapshot)
                {
                    Owner = Application.Current?.MainWindow
                };
                window.PrepareInitialWindowPlacement(GetFeedAllWindowPreferences());
                window.Show();
            }
            catch (Exception)
            {
                SnackbarMessageQueue?.Enqueue("Could not open feed window.");
            }
        }

        public IReadOnlyList<Feed> GetEnabledFeedsForShowAll()
        {
            return (_feeds ?? new List<Feed>())
                .Where(feed =>
                    feed?.IsEnabled == true &&
                    !string.IsNullOrWhiteSpace(feed.Name) &&
                    !string.IsNullOrWhiteSpace(feed.Url))
                .OrderBy(feed => string.IsNullOrWhiteSpace(feed.Category) ? "Default" : feed.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(feed => feed.Name, StringComparer.OrdinalIgnoreCase)
                .Select(CloneFeedSnapshot)
                .ToList();
        }

        private static Feed CloneFeedSnapshot(Feed feed)
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

        private int NormalizeArchiveAutoCleanupDays(int days)
        {
            return Math.Max(1, Math.Min(days, 3650));
        }

        private static DateTime? ResolveArchiveRetentionDate(FeedItem item)
        {
            if (item?.ArchivedAt.HasValue == true)
            {
                return item.ArchivedAt.Value;
            }

            if (item != null && item.PublicationDate > DateTime.MinValue)
            {
                return item.PublicationDate;
            }

            return null;
        }

        private Dictionary<string, DateTime> BuildArchivedRetentionDateLookup()
        {
            var lookup = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

            void ConsiderItem(FeedItem item)
            {
                var key = item?.Link?.Trim();
                var retentionDate = ResolveArchiveRetentionDate(item);
                if (string.IsNullOrWhiteSpace(key) || !retentionDate.HasValue)
                {
                    return;
                }

                if (lookup.TryGetValue(key, out var existing))
                {
                    if (retentionDate.Value < existing)
                    {
                        lookup[key] = retentionDate.Value;
                    }

                    return;
                }

                lookup[key] = retentionDate.Value;
            }

            foreach (var category in _archivedSection?.Items?.OfType<CategoryGroupViewModel>() ?? Enumerable.Empty<CategoryGroupViewModel>())
            {
                foreach (var feed in category.Feeds ?? Enumerable.Empty<FeedGroupViewModel>())
                {
                    foreach (var item in feed.Items ?? Enumerable.Empty<FeedItem>())
                    {
                        ConsiderItem(item);
                    }
                }
            }

            foreach (var feed in _archivedSection?.Items?.OfType<FeedGroupViewModel>() ?? Enumerable.Empty<FeedGroupViewModel>())
            {
                foreach (var item in feed.Items ?? Enumerable.Empty<FeedItem>())
                {
                    ConsiderItem(item);
                }
            }

            foreach (var snapshot in _settings?.ArchivedArticleSnapshots ?? Enumerable.Empty<FeedItem>())
            {
                ConsiderItem(snapshot);
            }

            return lookup;
        }

        private List<string> GetArchivedKeysOlderThanConfiguredRetention(bool requireEnabled = true)
        {
            if (_settings == null)
            {
                return new List<string>();
            }

            if (requireEnabled && _settings.ArchiveAutoCleanupEnabled != true)
            {
                return new List<string>();
            }

            var retentionDays = NormalizeArchiveAutoCleanupDays(_settings.ArchiveAutoCleanupDays);
            var cutoff = DateTime.Now.Date.AddDays(-retentionDays);
            return BuildArchivedRetentionDateLookup()
                .Where(entry => entry.Value.Date <= cutoff)
                .Select(entry => entry.Key)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private Dictionary<string, FeedItem> BuildArchivedItemLookup()
        {
            var lookup = new Dictionary<string, FeedItem>(StringComparer.OrdinalIgnoreCase);

            void AddItem(FeedItem item)
            {
                var key = item?.Link?.Trim();
                if (string.IsNullOrWhiteSpace(key) || lookup.ContainsKey(key))
                {
                    return;
                }

                lookup[key] = item;
            }

            foreach (var category in _archivedSection?.Items?.OfType<CategoryGroupViewModel>() ?? Enumerable.Empty<CategoryGroupViewModel>())
            {
                foreach (var feed in category.Feeds ?? Enumerable.Empty<FeedGroupViewModel>())
                {
                    foreach (var item in feed.Items ?? Enumerable.Empty<FeedItem>())
                    {
                        AddItem(item);
                    }
                }
            }

            foreach (var feed in _archivedSection?.Items?.OfType<FeedGroupViewModel>() ?? Enumerable.Empty<FeedGroupViewModel>())
            {
                foreach (var item in feed.Items ?? Enumerable.Empty<FeedItem>())
                {
                    AddItem(item);
                }
            }

            foreach (var snapshot in _settings?.ArchivedArticleSnapshots ?? Enumerable.Empty<FeedItem>())
            {
                AddItem(snapshot);
            }

            return lookup;
        }

        private int DeleteArchivedKeys(IEnumerable<string> keys, bool refreshUi)
        {
            _settings.ArchivedArticleSnapshots ??= new List<FeedItem>();
            var normalizedKeys = new HashSet<string>(
                keys?.Where(key => !string.IsNullOrWhiteSpace(key)).Select(key => key.Trim()) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            normalizedKeys.IntersectWith(_archivedArticleLinks);
            if (normalizedKeys.Count == 0)
            {
                return 0;
            }

            var archivedItems = BuildArchivedItemLookup();
            SuppressAutoScroll = true;

            foreach (var key in normalizedKeys)
            {
                _archivedArticleLinks.Remove(key);
                _settings.ArchivedArticleSnapshots.RemoveAll(item => string.Equals(item.Link?.Trim(), key, StringComparison.OrdinalIgnoreCase));

                if (archivedItems.TryGetValue(key, out var item))
                {
                    item.IsArchived = false;
                    item.IsPinned = false;
                    item.IsReadLater = false;
                    item.ArchivedAt = null;
                    item.IsSelected = false;
                }

                RemoveArticleFromSectionFeeds(_archivedSection, key);
            }

            PersistPinnedAndSavedLists();

            SuppressAutoScroll = false;
            return normalizedKeys.Count;
        }

        private void ApplyArchiveAutoCleanupSilently()
        {
            DeleteArchivedKeys(GetArchivedKeysOlderThanConfiguredRetention(requireEnabled: true), refreshUi: false);
        }

        private void ArchiveWindowOnClosed(object sender, EventArgs e)
        {
            if (_archiveWindow == null)
            {
                return;
            }

            _archiveWindow.Closed -= ArchiveWindowOnClosed;
            _archiveWindow = null;
        }

        private void OnRecentImportantItemCountChanged(int count)
        {
            HasImportantNotifications = count > 0;
            SyncImportantNotificationSettingsFromService();
        }

        private void RaiseSelectionShortcutCommandStates()
        {
            (PinSelectedArticleCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ReadLaterSelectedArticleCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ArchiveSelectedArticleCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (MarkSelectedArticleUnreadCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (OpenSelectedArticleInWindowCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (CopyLinkCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ToggleReaderModeCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ToggleContentFullscreenCommand as RelayCommand)?.RaiseCanExecuteChanged();
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
            SetReaderModeActive(false);
            (ToggleReaderModeCommand as RelayCommand)?.RaiseCanExecuteChanged();
            RaiseSelectionShortcutCommandStates();
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

            var visibleItems = CurrentArticleItems.ToList();
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

        private void MoveSelectionByFeed(object param)
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

            var visibleFeeds = CategoryGroups
                .SelectMany(category => category.Feeds ?? Enumerable.Empty<FeedGroupViewModel>())
                .Where(feed => (feed.Items?.Count ?? 0) > 0)
                .ToList();
            if (visibleFeeds.Count == 0)
            {
                return;
            }

            var currentFeedIndex = -1;
            if (!string.IsNullOrWhiteSpace(_selectedMainFeedName))
            {
                currentFeedIndex = visibleFeeds.FindIndex(feed =>
                    string.Equals(feed.Name, _selectedMainFeedName, StringComparison.OrdinalIgnoreCase));
            }

            if (currentFeedIndex < 0 && _currentSelectedItem != null)
            {
                currentFeedIndex = visibleFeeds.FindIndex(feed => feed.Items.Contains(_currentSelectedItem));
            }

            int targetFeedIndex;
            if (currentFeedIndex < 0)
            {
                targetFeedIndex = delta > 0 ? 0 : visibleFeeds.Count - 1;
            }
            else
            {
                targetFeedIndex = currentFeedIndex + delta;
                if (targetFeedIndex < 0) targetFeedIndex = 0;
                if (targetFeedIndex >= visibleFeeds.Count) targetFeedIndex = visibleFeeds.Count - 1;
                if (targetFeedIndex == currentFeedIndex)
                {
                    return;
                }
            }

            var targetFeed = visibleFeeds[targetFeedIndex];
            SelectMainFeed(targetFeed);
            if (_currentSelectedItem != null)
            {
                ScrollSelectionToTopRequested?.Invoke(_currentSelectedItem);
            }
        }

        private List<FeedGroupViewModel> BuildVisibleFeedList(ArticleSectionViewModel section)
        {
            var result = new List<FeedGroupViewModel>();
            if (section == null)
            {
                return result;
            }

            foreach (var child in section.Items)
            {
                if (child is CategoryGroupViewModel category)
                {
                    foreach (var feed in category.Feeds)
                    {
                        if (feed == null)
                        {
                            continue;
                        }

                        var hasVisibleItem = feed.PagedItems.OfType<FeedItem>().Any() || feed.Items.Any();
                        if (hasVisibleItem)
                        {
                            result.Add(feed);
                        }
                    }
                }
                else if (child is FeedGroupViewModel feedGroup)
                {
                    var hasVisibleItem = feedGroup.PagedItems.OfType<FeedItem>().Any() || feedGroup.Items.Any();
                    if (hasVisibleItem)
                    {
                        result.Add(feedGroup);
                    }
                }
            }

            return result;
        }

        private void ClearBrowserContent()
        {
            try
            {
                SetContentFullscreen(false);
                ResetEmbeddedVideoPlayback();
                _embeddedBrowserSession?.NavigateToPlaceholder();
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

            _articleWebView.CoreWebView2.NavigationCompleted -= ArticleWebView_NavigationCompleted;
            _articleWebView.CoreWebView2.NavigationCompleted += ArticleWebView_NavigationCompleted;
            IsArticleTransitionOverlayVisible = IsArticleWebViewVisible;
            _articleWebView.NavigateToString(SelectedArticleHtml);
        }

        private async void ArticleWebView_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                if (_isReaderModeActive)
                {
                    ApplyCurrentArticleZoomFactor();
                    await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                    SetReaderViewReady(true);
                    SetReaderModeLoading(false);
                    SnackbarMessageQueue?.Enqueue("Reader Mode loaded.");
                }

                IsArticleTransitionOverlayVisible = false;
                await Task.Delay(500);
                await AdjustArticleHeightAsync();
                return;
            }

            SetReaderModeLoading(false);
            if (_isReaderModeActive)
            {
                SetReaderModeActive(false);
            }

            IsArticleTransitionOverlayVisible = false;
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
            feedManagerWindow.PrepareInitialWindowPlacement(GetFeedManagerWindowPreferences());
            feedManagerWindow.ShowDialog();

            if (feedManagerViewModel.WasClosedBySave)
            {
                // Reload settings to pick up new Categories and expanded states
                _settings = _settingsService.LoadSettings();
                _settings.LiveRefreshIntervalSeconds = NormalizeLiveRefreshIntervalSeconds(_settings.LiveRefreshIntervalSeconds);
                _readArticleLinks = new HashSet<string>(_settings.ReadArticleLinks ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                _pendingDarkMode = _settings.DarkMode;
                _pendingAutoRefresh = _settings.AutoRefresh;
                _pendingLiveRefresh = _settings.LiveRefresh;
                _pendingLiveRefreshIntervalSeconds = NormalizeLiveRefreshIntervalSeconds(_settings.LiveRefreshIntervalSeconds);
                _pendingAutoRefreshIntervalMinutes = _settings.AutoRefreshIntervalMinutes > 0 ? _settings.AutoRefreshIntervalMinutes : 10;
                _pendingTreeWidth = _settings.TreeWidth;
                _pendingStartInTerminal = _settings.StartInTerminal;
                _pendingTerminalThemeName = NormalizeTerminalThemeName(_settings.TerminalThemeName);
                _pendingTerminalBootAnimationEnabled = _settings.TerminalBootAnimationEnabled;
                _isShowContentAlwaysOn = _settings.IsShowContentAlwaysOn;
                OnPropertyChanged(nameof(DarkMode));
                OnPropertyChanged(nameof(PendingDarkMode));
                OnPropertyChanged(nameof(StartInTerminal));
                OnPropertyChanged(nameof(PendingStartInTerminal));
                OnPropertyChanged(nameof(TerminalThemeName));
                OnPropertyChanged(nameof(PendingTerminalThemeName));
                OnPropertyChanged(nameof(TerminalBootAnimationEnabled));
                OnPropertyChanged(nameof(PendingTerminalBootAnimationEnabled));
                OnPropertyChanged(nameof(PendingAutoRefresh));
                OnPropertyChanged(nameof(PendingLiveRefresh));
                OnPropertyChanged(nameof(PendingLiveRefreshIntervalSeconds));
                OnPropertyChanged(nameof(PendingAutoRefreshIntervalMinutes));
                OnPropertyChanged(nameof(PendingTreeWidth));
                OnPropertyChanged(nameof(IsShowContentAlwaysOn));
                OnPropertyChanged(nameof(IsAutoRefreshModeOptionsVisible));
                OnPropertyChanged(nameof(IsIntervalAutoRefreshSettingsVisible));
                OnPropertyChanged(nameof(IsLiveRefreshSettingsVisible));
                OnPropertyChanged(nameof(IsLiveAutoRefreshSelected));
                OnPropertyChanged(nameof(IsLiveRefreshExpertModeWarningVisible));
                OnPropertyChanged(nameof(AutoRefreshIntervalDisplay));
                OnPropertyChanged(nameof(IsNotLoading));
                OnPropertyChanged(nameof(ShowLoadingOverlay));
                if (_settings.AdvertisementKeywords == null || _settings.AdvertisementKeywords.Count == 0)
                {
                    _settings.AdvertisementKeywords = new List<string>(AppSettings.DefaultAdvertisementKeywords);
                }
                if (_settings.ImportantKeywords == null)
                {
                    _settings.ImportantKeywords = new List<string>();
                }
                ResetPendingSettingsFromActiveSettings();

                OnPropertyChanged(nameof(AdvertisementFilterEnabled));
                OnPropertyChanged(nameof(AdvertisementKeywordsText));
                OnPropertyChanged(nameof(DesktopNotificationsEnabled));
                OnPropertyChanged(nameof(ImportantKeywordsText));

                // Reload feeds and refresh the tree
                _feeds = _settingsService.LoadFeeds();
                _ = RefreshFeedsAsync();
            }
        }

        private void ToggleBrowserContent()
        {
            if (!IsBrowserVisible)
            {
                if (!TryShowBrowserContent(useClearNavigation: false))
                {
                    return;
                }
            }
            else
            {
                if (IsContentFullscreen)
                {
                    SetContentFullscreen(false);
                }

                if (!IsShowContentAlwaysOn)
                {
                    IsBrowserVisible = false;
                    _embeddedBrowserSession?.NavigateToBlank();
                }
                else
                {
                }
            }
        }

        private bool CanToggleContentFullscreen()
        {
            return IsContentFullscreen || IsBrowserVisible || !string.IsNullOrWhiteSpace(SelectedArticleLink);
        }

        private void ToggleContentFullscreen()
        {
            if (IsContentFullscreen)
            {
                SetContentFullscreen(false);
                return;
            }

            if (!TryShowBrowserContent(useClearNavigation: true))
            {
                return;
            }

            SetContentFullscreen(true);
        }

        public void ExitContentFullscreen()
        {
            SetContentFullscreen(false);
        }

        private void SetContentFullscreen(bool isFullscreen)
        {
            IsContentFullscreen = isFullscreen;
        }

        private bool TryShowBrowserContent(bool useClearNavigation)
        {
            if (string.IsNullOrWhiteSpace(SelectedArticleLink))
            {
                System.Windows.MessageBox.Show("No article selected or no link available.",
                    "Information", System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return false;
            }

            _isBrowserTransitionVisible = true;
            OnPropertyChanged(nameof(ShowBrowserLoadingOverlay));
            IsBrowserVisible = true;
            BrowserHeight = Math.Max(BrowserHeight, 400);
            ScheduleBrowserNavigation(SelectedArticleLink, useClearNavigation);

            return true;
        }

        private async void ScheduleBrowserNavigation(string url, bool useClearNavigation)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            if (IsBrowserPageLoading)
            {
                _pendingBrowserNavigationUrl = url;
                _pendingBrowserNavigationUseClear = useClearNavigation;
                return;
            }

            NotifyEmbeddedBrowserWarmupHintOnce();
            IsBrowserPageLoading = true;
            await ExecuteBrowserNavigationAsync(url, useClearNavigation);
        }

        private async Task ExecuteBrowserNavigationAsync(string url, bool useClearNavigation)
        {
            try
            {
                var browserSession = _embeddedBrowserSession;
                if (browserSession == null)
                {
                    IsBrowserPageLoading = false;
                    _isBrowserTransitionVisible = false;
                    OnPropertyChanged(nameof(ShowBrowserLoadingOverlay));
                    return;
                }

                browserSession.SetDarkMode(_settings.DarkMode);
                browserSession.SetAdBlockerEnabled(_settings.AdBlockerEnabled);
                browserSession.SetMediaPlaybackEnabled(_isEmbeddedVideoPlaybackEnabled);

                if (useClearNavigation)
                {
                    browserSession.NavigateToBlank();
                    await Task.Delay(120);
                }

                var navigated = await browserSession.NavigateFastAsync(url);
                if (!navigated)
                {
                    IsBrowserPageLoading = false;
                    _isBrowserTransitionVisible = false;
                    OnPropertyChanged(nameof(ShowBrowserLoadingOverlay));
                }
            }
            catch (Exception)
            {
                IsBrowserPageLoading = false;
                _isBrowserTransitionVisible = false;
                OnPropertyChanged(nameof(ShowBrowserLoadingOverlay));
                // Ignore navigation failures; users can retry from the browser toolbar.
            }
        }

        public bool NotifyEmbeddedBrowserWarmupHintOnce()
        {
            if (_hasShownEmbeddedBrowserWarmupHintThisSession)
            {
                return false;
            }

            _hasShownEmbeddedBrowserWarmupHintThisSession = true;
            SnackbarMessageQueue?.Enqueue("The first Show Content load after app start can take longer. After that, pages usually open faster.");
            return true;
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

                message += $"\nAdBlocker is currently: {(AdBlockerEnabled ? "Enabled" : "Disabled")}\n\n" +
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
            _embeddedBrowserSession?.NavigateToBlank();
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
            SaveSettingsPreservingImportantNotifications();
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
            SaveSettingsPreservingImportantNotifications();
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
            SaveSettingsPreservingImportantNotifications();
            return expandedStates;
        }

        private static List<string> ParseKeywordList(string value)
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

        private static List<string> ParseAdvertisementKeywords(string value)
        {
            return ParseKeywordList(value);
        }

        private List<string> GetAdvertisementKeywordsForFiltering()
        {
            var keywords = _settings.AdvertisementKeywords;
            if (keywords == null || keywords.Count == 0)
            {
                return new List<string>();
            }

            return ParseKeywordList(string.Join(Environment.NewLine, keywords));
        }

        private void SyncMyFeedsSection()
        {
            if (_myFeedsSection == null || CategoryGroups == null)
            {
                return;
            }

            var desiredItems = CategoryGroups
                .Cast<object>()
                .ToList();
            SyncCollectionInPlace(_myFeedsSection.Items, desiredItems);
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
                    _archivedSection?.UpdateUnreadCount();
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
            RefreshCurrentArticleList();
            _pinnedSection?.UpdateUnreadCount();
            _readLaterSection?.UpdateUnreadCount();
            _archivedSection?.UpdateUnreadCount();
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

                    UpdateItemsInPlace(feedVm, groupedItems[feedName], _readArticleLinks);
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
            else if (section == _archivedSection)
            {
                _archivedSourceFeeds = groupedItems
                    .Select(kvp => new FeedGroupViewModel
                    {
                        Name = kvp.Key,
                        Category = ResolveCategoryNameForFeed(kvp.Key),
                        IsExpanded = feedStates.TryGetValue(kvp.Key, out var feedExpanded) ? feedExpanded : true,
                        HideUnreadIndicators = section.HideUnreadIndicators,
                        Items = new ObservableCollection<FeedItem>(kvp.Value)
                    })
                    .ToList();
                OnArchiveItemsChanged();
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
            if (section == _archivedSection)
            {
                OnArchiveItemsChanged();
            }
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
            RemoveArticleFromSectionFeeds(_archivedSection, key);
        }

        private void AddItemToCache(ArticleSectionViewModel section, string feedName, string categoryName, FeedItem item)
        {
            if (section != _pinnedSection && section != _readLaterSection && section != _archivedSection)
            {
                return;
            }

            var cache = section == _pinnedSection
                ? _pinnedSourceFeeds
                : section == _readLaterSection
                    ? _readLaterSourceFeeds
                    : _archivedSourceFeeds;
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
            if (section != _pinnedSection && section != _readLaterSection && section != _archivedSection)
            {
                return;
            }

            var cache = section == _pinnedSection
                ? _pinnedSourceFeeds
                : section == _readLaterSection
                    ? _readLaterSourceFeeds
                    : _archivedSourceFeeds;
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
            if (section == _archivedSection)
            {
                OnArchiveItemsChanged();
            }
        }

        private static FeedItem CloneSnapshot(FeedItem item)
        {
            return new FeedItem
            {
                FeedName = item.FeedName,
                FeedUrl = item.FeedUrl,
                Title = item.Title,
                Description = item.Description,
                Link = item.Link,
                PublicationDate = item.PublicationDate,
                IsRead = item.IsRead,
                IsPinned = item.IsPinned,
                IsReadLater = item.IsReadLater,
                IsArchived = item.IsArchived,
                ArchivedAt = item.ArchivedAt
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

        private List<FeedItem> GetCommandFeedItems(object parameter)
        {
            if (parameter is FeedItem item)
            {
                return new List<FeedItem> { item };
            }

            if (parameter is IEnumerable<FeedItem> items)
            {
                return items.Where(candidate => candidate != null).Distinct().ToList();
            }

            return new List<FeedItem>();
        }

        private void ExecuteForCommandItems(object parameter, Action<FeedItem> action)
        {
            var items = GetCommandFeedItems(parameter);
            if (items.Count == 0 || action == null)
            {
                return;
            }

            foreach (var item in items)
            {
                action(item);
            }
        }

        private void CopyLinksFromItems(IReadOnlyList<FeedItem> items)
        {
            if (items == null || items.Count == 0)
            {
                return;
            }

            if (items.Count == 1)
            {
                CopyLinkFromItem(items[0]);
                return;
            }

            var links = items
                .Select(item => NormalizeExternalLink(item?.Link))
                .Where(link => !string.IsNullOrWhiteSpace(link))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (links.Count == 0)
            {
                return;
            }

            try
            {
                Clipboard.SetText(string.Join(Environment.NewLine, links));
            }
            catch (Exception)
            {
                // Ignore clipboard errors.
            }
        }

        private bool CanCopySingleArticleLink(IReadOnlyList<FeedItem> items)
        {
            return items?.Count == 1 && !string.IsNullOrWhiteSpace(NormalizeExternalLink(items[0]?.Link));
        }

        private void MarkItemsAsUnread(IReadOnlyList<FeedItem> items)
        {
            if (items == null || items.Count == 0)
            {
                return;
            }

            var changed = false;
            foreach (var item in items.Where(item => item != null))
            {
                item.IsRead = false;
                var key = item.Link?.Trim();
                if (!string.IsNullOrWhiteSpace(key))
                {
                    changed |= _readArticleLinks.Remove(key);
                }
            }

            if (changed)
            {
                PersistReadState();
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

        private void MarkGroupAsRead(object target)
        {
            var unreadItems = EnumerateTargetItems(target)
                .Where(item => item?.IsRead == false)
                .ToList();

            if (unreadItems.Count == 0)
            {
                return;
            }

            var changed = false;
            foreach (var item in unreadItems)
            {
                item.IsRead = true;
                var key = item.Link?.Trim();
                if (!string.IsNullOrWhiteSpace(key))
                {
                    changed |= _readArticleLinks.Add(key);
                }
            }

            if (changed)
            {
                PersistReadState();
            }
        }

        private void MarkGroupAsUnread(object target)
        {
            var readItems = EnumerateTargetItems(target)
                .Where(item => item?.IsRead == true)
                .ToList();

            if (readItems.Count == 0)
            {
                return;
            }

            var changed = false;
            foreach (var item in readItems)
            {
                item.IsRead = false;
                var key = item.Link?.Trim();
                if (!string.IsNullOrWhiteSpace(key))
                {
                    changed |= _readArticleLinks.Remove(key);
                }
            }

            if (changed)
            {
                PersistReadState();
            }
        }

        private bool HasUnreadItems(object target)
        {
            return EnumerateTargetItems(target).Any(item => item?.IsRead == false);
        }

        private bool HasReadItems(object target)
        {
            return EnumerateTargetItems(target).Any(item => item?.IsRead == true);
        }

        private IEnumerable<FeedItem> EnumerateTargetItems(object target)
        {
            switch (target)
            {
                case ArticleSectionViewModel section:
                    foreach (var item in EnumerateSectionItems(section))
                    {
                        yield return item;
                    }
                    break;
                case CategoryGroupViewModel category:
                    foreach (var feed in category.Feeds ?? Enumerable.Empty<FeedGroupViewModel>())
                    {
                        foreach (var item in feed.Items ?? Enumerable.Empty<FeedItem>())
                        {
                            yield return item;
                        }
                    }
                    break;
                case FeedGroupViewModel feed:
                    foreach (var item in feed.Items ?? Enumerable.Empty<FeedItem>())
                    {
                        yield return item;
                    }
                    break;
                case CollectionViewGroup group:
                    foreach (var item in EnumerateCollectionViewGroupItems(group))
                    {
                        yield return item;
                    }
                    break;
                case IEnumerable<FeedItem> items:
                    foreach (var item in items.Where(item => item != null))
                    {
                        yield return item;
                    }
                    break;
            }
        }

        private IEnumerable<FeedItem> EnumerateCollectionViewGroupItems(CollectionViewGroup group)
        {
            foreach (var child in group?.Items ?? Enumerable.Empty<object>())
            {
                switch (child)
                {
                    case FeedItem item:
                        yield return item;
                        break;
                    case CollectionViewGroup nestedGroup:
                        foreach (var nestedItem in EnumerateCollectionViewGroupItems(nestedGroup))
                        {
                            yield return nestedItem;
                        }
                        break;
                }
            }
        }

        private IEnumerable<FeedItem> EnumerateSectionItems(ArticleSectionViewModel section)
        {
            foreach (var child in section?.Items ?? Enumerable.Empty<object>())
            {
                switch (child)
                {
                    case FeedGroupViewModel feed:
                        foreach (var item in feed.Items ?? Enumerable.Empty<FeedItem>())
                        {
                            yield return item;
                        }
                        break;
                    case CategoryGroupViewModel category:
                        foreach (var feed in category.Feeds ?? Enumerable.Empty<FeedGroupViewModel>())
                        {
                            foreach (var item in feed.Items ?? Enumerable.Empty<FeedItem>())
                            {
                                yield return item;
                            }
                        }
                        break;
                }
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
                _settings.ArchivedArticleSnapshots.RemoveAll(f => string.Equals(f.Link?.Trim(), key, StringComparison.OrdinalIgnoreCase));
                PersistPinnedAndSavedLists();

                item.IsPinned = false;
                item.IsReadLater = false;
                item.IsArchived = false;
                item.ArchivedAt = null;
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
            _archivedArticleLinks.Remove(key);
            _settings.ReadLaterArticleSnapshots.RemoveAll(f => string.Equals(f.Link?.Trim(), key, StringComparison.OrdinalIgnoreCase));
            _settings.PinnedArticleSnapshots.RemoveAll(f => string.Equals(f.Link?.Trim(), key, StringComparison.OrdinalIgnoreCase));
            _settings.ArchivedArticleSnapshots.RemoveAll(f => string.Equals(f.Link?.Trim(), key, StringComparison.OrdinalIgnoreCase));
            _settings.PinnedArticleSnapshots.Add(CloneSnapshot(item));
            PersistPinnedAndSavedLists();

            item.IsPinned = true;
            item.IsReadLater = false;
            item.IsArchived = false;
            item.ArchivedAt = null;
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
                item.IsArchived = false;
                item.ArchivedAt = null;
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
            _archivedArticleLinks.Remove(key);
            _settings.PinnedArticleSnapshots.RemoveAll(f => string.Equals(f.Link?.Trim(), key, StringComparison.OrdinalIgnoreCase));
            _settings.ReadLaterArticleSnapshots.RemoveAll(f => string.Equals(f.Link?.Trim(), key, StringComparison.OrdinalIgnoreCase));
            _settings.ArchivedArticleSnapshots.RemoveAll(f => string.Equals(f.Link?.Trim(), key, StringComparison.OrdinalIgnoreCase));
            _settings.ReadLaterArticleSnapshots.Add(CloneSnapshot(item));
            PersistPinnedAndSavedLists();

            item.IsReadLater = true;
            item.IsPinned = false;
            item.IsArchived = false;
            item.ArchivedAt = null;
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

        private void ArchiveArticle(FeedItem item)
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
            if (_archivedArticleLinks.Contains(key))
            {
                _archivedArticleLinks.Remove(key);
                _settings.ArchivedArticleSnapshots.RemoveAll(f => string.Equals(f.Link?.Trim(), key, StringComparison.OrdinalIgnoreCase));
                PersistPinnedAndSavedLists();

                item.IsArchived = false;
                item.IsPinned = false;
                item.IsReadLater = false;
                item.ArchivedAt = null;
                RemoveArticleFromSectionFeeds(_archivedSection, key);
                AddArticleBackToCategories(item);
                RefreshSectionsDeferred(_lastInlineMoveOffset);
                if (neighbor != null)
                {
                    OnArticleSelected(neighbor, enableAutoScroll: false);
                }
                SuppressAutoScroll = false;
                return;
            }

            item.ArchivedAt = DateTime.Now;
            _archivedArticleLinks.Add(key);
            _pinnedArticleLinks.Remove(key);
            _readLaterArticleLinks.Remove(key);
            _settings.PinnedArticleSnapshots.RemoveAll(f => string.Equals(f.Link?.Trim(), key, StringComparison.OrdinalIgnoreCase));
            _settings.ReadLaterArticleSnapshots.RemoveAll(f => string.Equals(f.Link?.Trim(), key, StringComparison.OrdinalIgnoreCase));
            _settings.ArchivedArticleSnapshots.RemoveAll(f => string.Equals(f.Link?.Trim(), key, StringComparison.OrdinalIgnoreCase));
            _settings.ArchivedArticleSnapshots.Add(CloneSnapshot(item));
            PersistPinnedAndSavedLists();

            item.IsArchived = true;
            item.IsPinned = false;
            item.IsReadLater = false;
            RemoveArticleFromAllCollections(key);
            AddArticleToSection(_archivedSection, item);
            RefreshSectionsDeferred(_lastInlineMoveOffset);
            item.IsSelected = false;
            if (neighbor != null)
            {
                OnArticleSelected(neighbor, enableAutoScroll: false);
            }
            SuppressAutoScroll = false;
        }

        private List<string> GetImportantKeywordsForNotifications()
        {
            var keywords = _settings.ImportantKeywords;
            if (keywords == null || keywords.Count == 0)
            {
                return new List<string>();
            }

            return ParseKeywordList(string.Join(Environment.NewLine, keywords));
        }

        private List<FeedItem> GetNewImportantItemsForNotifications(IEnumerable<FeedItem> items, IReadOnlyList<string> importantKeywords)
        {
            var matches = new List<FeedItem>();
            if (!_settings.EnableNotifications || items == null)
            {
                return matches;
            }

            if (!_hasCompletedInitialRefresh)
            {
                return matches;
            }

            var feedsByUrl = _feeds
                .Where(feed => feed?.IsImportant == true && !string.IsNullOrWhiteSpace(feed.Url))
                .GroupBy(feed => feed.Url.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            var importantFeedNames = new HashSet<string>(
                _feeds
                    .Where(feed => feed?.IsImportant == true && !string.IsNullOrWhiteSpace(feed.Name))
                    .Select(feed => feed.Name.Trim()),
                StringComparer.OrdinalIgnoreCase);

            foreach (var item in items)
            {
                var link = item?.Link?.Trim();
                if (string.IsNullOrWhiteSpace(link))
                {
                    continue;
                }

                if (_knownArticleLinks.Contains(link))
                {
                    continue;
                }

                var fromImportantFeed = IsImportantFeedMatch(item, feedsByUrl, importantFeedNames);
                var fromImportantKeyword = MatchesImportantKeyword(item, importantKeywords);

                if (fromImportantFeed || fromImportantKeyword)
                {
                    matches.Add(item);
                }
            }

            return matches;
        }

        private static bool IsImportantFeedMatch(
            FeedItem item,
            IReadOnlyDictionary<string, Feed> feedsByUrl,
            IReadOnlySet<string> importantFeedNames)
        {
            var feedUrl = item?.FeedUrl?.Trim();
            if (!string.IsNullOrWhiteSpace(feedUrl) && feedsByUrl.ContainsKey(feedUrl))
            {
                return true;
            }

            var feedName = item?.FeedName?.Trim();
            return !string.IsNullOrWhiteSpace(feedName) && importantFeedNames.Contains(feedName);
        }

        private static bool MatchesImportantKeyword(FeedItem item, IReadOnlyList<string> keywords)
        {
            if (keywords == null || keywords.Count == 0)
            {
                return false;
            }

            var searchTargets = new[]
            {
                item?.Title,
                item?.Description,
                item?.FeedName,
                item?.Link
            };

            foreach (var target in searchTargets)
            {
                if (string.IsNullOrWhiteSpace(target))
                {
                    continue;
                }

                foreach (var keyword in keywords)
                {
                    if (target.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void UpdateKnownArticleLinks(IEnumerable<FeedItem> items)
        {
            if (items == null)
            {
                return;
            }

            foreach (var item in items)
            {
                var link = item?.Link?.Trim();
                if (string.IsNullOrWhiteSpace(link))
                {
                    continue;
                }

                _knownArticleLinks.Add(link);
            }
        }

        private void TryNotifyImportantItems(IReadOnlyList<FeedItem> candidates)
        {
            if (!_settings.EnableNotifications || candidates == null || candidates.Count == 0)
            {
                return;
            }

            var utcNow = DateTime.UtcNow;
            if (_lastNotificationUtc != DateTime.MinValue && (utcNow - _lastNotificationUtc) < NotificationCooldown)
            {
                return;
            }

            var topItems = candidates
                .OrderByDescending(item => item.PublicationDate)
                .Take(MaxNotificationsPerRefresh)
                .ToList();

            try
            {
                _desktopNotificationService?.ShowImportantArticles(topItems, showWindowsToast: IsTerminalModeActive?.Invoke() != true);
                _lastNotificationUtc = utcNow;
            }
            catch (Exception)
            {
                // Ignore desktop notification errors to keep refresh stable.
            }
        }

        private static List<FeedItem> RemoveCrossFeedDuplicates(
            IEnumerable<FeedItem> items,
            int? perFeedQuota = null,
            IReadOnlyList<string> preferredFeedOrder = null)
        {
            var deduplicated = new List<FeedItem>();
            if (items == null)
            {
                return deduplicated;
            }

            var allItems = items.ToList();
            var technicalItems = allItems.Where(IsTechnicalFeedEntry).ToList();
            var articleItems = allItems.Where(item => !IsTechnicalFeedEntry(item)).ToList();

            if (articleItems.Count == 0)
            {
                deduplicated.AddRange(technicalItems);
                return deduplicated;
            }

            if (!perFeedQuota.HasValue || perFeedQuota.Value <= 0)
            {
                deduplicated.AddRange(DeduplicateLinear(articleItems));
                deduplicated.AddRange(technicalItems);
                return deduplicated;
            }

            var perFeedLimit = perFeedQuota.Value;
            var feedBuckets = articleItems
                .GroupBy(GetFeedDedupeKey)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderByDescending(item => item.PublicationDate)
                        .ToList(),
                    StringComparer.OrdinalIgnoreCase);

            var orderedFeedKeys = BuildFeedTraversalOrder(feedBuckets.Keys, preferredFeedOrder);
            var bucketIndexes = orderedFeedKeys.ToDictionary(key => key, _ => 0, StringComparer.OrdinalIgnoreCase);
            var bucketCounts = orderedFeedKeys.ToDictionary(key => key, _ => 0, StringComparer.OrdinalIgnoreCase);
            var seenCanonicalLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenContentFingerprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var addedInPass = true;

            while (addedInPass)
            {
                addedInPass = false;

                foreach (var feedKey in orderedFeedKeys)
                {
                    if (bucketCounts[feedKey] >= perFeedLimit)
                    {
                        continue;
                    }

                    if (!feedBuckets.TryGetValue(feedKey, out var feedItems) || feedItems.Count == 0)
                    {
                        continue;
                    }

                    var index = bucketIndexes[feedKey];
                    while (index < feedItems.Count)
                    {
                        var item = feedItems[index];
                        index++;

                        var linkKey = CreateCanonicalLinkKey(item.Link);
                        var contentKey = CreateContentFingerprint(item);

                        var duplicateByLink = !string.IsNullOrWhiteSpace(linkKey) && seenCanonicalLinks.Contains(linkKey);
                        var duplicateByContent = !string.IsNullOrWhiteSpace(contentKey) && seenContentFingerprints.Contains(contentKey);

                        if (duplicateByLink || duplicateByContent)
                        {
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(linkKey))
                        {
                            seenCanonicalLinks.Add(linkKey);
                        }

                        if (!string.IsNullOrWhiteSpace(contentKey))
                        {
                            seenContentFingerprints.Add(contentKey);
                        }

                        deduplicated.Add(item);
                        bucketCounts[feedKey] = bucketCounts[feedKey] + 1;
                        addedInPass = true;
                        break;
                    }

                    bucketIndexes[feedKey] = index;
                }
            }

            deduplicated.AddRange(technicalItems);
            return deduplicated;
        }

        private static List<FeedItem> DeduplicateLinear(IEnumerable<FeedItem> items)
        {
            var result = new List<FeedItem>();
            var seenCanonicalLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenContentFingerprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items)
            {
                if (item == null)
                {
                    continue;
                }

                var linkKey = CreateCanonicalLinkKey(item.Link);
                var contentKey = CreateContentFingerprint(item);

                var duplicateByLink = !string.IsNullOrWhiteSpace(linkKey) && seenCanonicalLinks.Contains(linkKey);
                var duplicateByContent = !string.IsNullOrWhiteSpace(contentKey) && seenContentFingerprints.Contains(contentKey);

                if (duplicateByLink || duplicateByContent)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(linkKey))
                {
                    seenCanonicalLinks.Add(linkKey);
                }

                if (!string.IsNullOrWhiteSpace(contentKey))
                {
                    seenContentFingerprints.Add(contentKey);
                }

                result.Add(item);
            }

            return result;
        }

        private static IReadOnlyList<string> BuildFeedTraversalOrder(
            IEnumerable<string> bucketKeys,
            IReadOnlyList<string> preferredFeedOrder)
        {
            var order = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (preferredFeedOrder != null)
            {
                foreach (var key in preferredFeedOrder)
                {
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    var normalized = key.Trim();
                    if (seen.Add(normalized))
                    {
                        order.Add(normalized);
                    }
                }
            }

            foreach (var key in bucketKeys)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                var normalized = key.Trim();
                if (seen.Add(normalized))
                {
                    order.Add(normalized);
                }
            }

            return order;
        }

        private static string GetFeedDedupeKey(FeedItem item)
        {
            if (!string.IsNullOrWhiteSpace(item?.FeedName))
            {
                return item.FeedName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(item?.FeedUrl))
            {
                return item.FeedUrl.Trim();
            }

            return "Feed";
        }

        private static bool IsTechnicalFeedEntry(FeedItem item)
        {
            var title = item?.Title;
            if (string.IsNullOrWhiteSpace(title))
            {
                return false;
            }

            return title.StartsWith("[ERROR]", StringComparison.OrdinalIgnoreCase) ||
                   title.StartsWith("[BLOCKED]", StringComparison.OrdinalIgnoreCase);
        }

        private static string CreateCanonicalLinkKey(string link)
        {
            if (string.IsNullOrWhiteSpace(link))
            {
                return string.Empty;
            }

            var trimmed = link.Trim();
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            {
                return trimmed;
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            var builder = new UriBuilder(uri)
            {
                Host = uri.Host.ToLowerInvariant(),
                Fragment = string.Empty
            };

            if ((string.Equals(builder.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) && builder.Port == 443) ||
                (string.Equals(builder.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && builder.Port == 80))
            {
                builder.Port = -1;
            }

            var normalizedPath = uri.AbsolutePath;
            if (!string.IsNullOrWhiteSpace(normalizedPath) && normalizedPath.Length > 1)
            {
                normalizedPath = normalizedPath.TrimEnd('/');
            }
            builder.Path = string.IsNullOrWhiteSpace(normalizedPath) ? "/" : normalizedPath;

            var queryValues = System.Web.HttpUtility.ParseQueryString(uri.Query ?? string.Empty);
            var queryPairs = new List<string>();
            foreach (var key in queryValues.AllKeys.Where(k => !string.IsNullOrWhiteSpace(k)).OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            {
                if (TrackingQueryParameters.Contains(key))
                {
                    continue;
                }

                var values = queryValues.GetValues(key);
                if (values == null || values.Length == 0)
                {
                    queryPairs.Add($"{Uri.EscapeDataString(key)}=");
                    continue;
                }

                foreach (var value in values.Where(v => v != null).OrderBy(v => v, StringComparer.Ordinal))
                {
                    queryPairs.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
                }
            }

            builder.Query = queryPairs.Count > 0 ? string.Join("&", queryPairs) : string.Empty;
            var canonical = builder.Uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.PathAndQuery, UriFormat.SafeUnescaped);
            return canonical.TrimEnd('/');
        }

        private static string CreateContentFingerprint(FeedItem item)
        {
            var normalizedTitle = NormalizeFingerprintText(item?.Title);
            if (string.IsNullOrWhiteSpace(normalizedTitle) || normalizedTitle.Length < 20)
            {
                return string.Empty;
            }

            var normalizedDescription = NormalizeFingerprintText(CollapseWhitespace(NormalizeHtmlToText(item?.Description ?? string.Empty)));
            if (normalizedDescription.Length > 180)
            {
                normalizedDescription = normalizedDescription.Substring(0, 180);
            }

            var datePart = item?.PublicationDate > DateTime.MinValue
                ? item.PublicationDate.ToUniversalTime().ToString("yyyyMMdd")
                : "00000000";

            if (string.IsNullOrWhiteSpace(normalizedDescription))
            {
                return $"{normalizedTitle}|{datePart}";
            }

            return $"{normalizedTitle}|{normalizedDescription}|{datePart}";
        }

        private static string NormalizeFingerprintText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var decoded = WebUtility.HtmlDecode(value) ?? string.Empty;
            var builder = new System.Text.StringBuilder(decoded.Length);
            var previousWasWhitespace = false;

            foreach (var ch in decoded)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    builder.Append(char.ToLowerInvariant(ch));
                    previousWasWhitespace = false;
                    continue;
                }

                if (char.IsWhiteSpace(ch))
                {
                    if (!previousWasWhitespace)
                    {
                        builder.Append(' ');
                        previousWasWhitespace = true;
                    }
                }
            }

            return builder.ToString().Trim();
        }

        private async Task RefreshFeedsAsync(bool suppressSummaryBanner = false)
        {
            if (!await _refreshLock.WaitAsync(0))
            {
                return;
            }

            IsLoading = true;
            var completeInitialRefreshAfterLoad = !_hasCompletedInitialRefresh;
            string linkToRestore = null;
            string refreshSummaryMessage = null;
            List<FeedItem> notificationCandidates = null;
            List<FeedItem> notificationSourceItems = null;
            var duplicateItemsRemoved = 0;
            var refreshTimer = Stopwatch.StartNew();
            try
            {
                await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

                // Capture current scroll offset (view will provide it)
                _lastTreeScrollOffset = RequestTreeScrollOffset?.Invoke() ?? 0;

                if (_feeds.Count == 0)
                {
                    _allCategoryGroups.Clear();
            CategoryGroups.Clear();
            UpdateFeedSection(_pinnedSection, new Dictionary<string, List<FeedItem>>(StringComparer.OrdinalIgnoreCase));
            UpdateFeedSection(_readLaterSection, new Dictionary<string, List<FeedItem>>(StringComparer.OrdinalIgnoreCase));
            UpdateFeedSection(_archivedSection, new Dictionary<string, List<FeedItem>>(StringComparer.OrdinalIgnoreCase));
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
                var extraPerFeed = Math.Min(
                    MaxExtraPerFeed,
                    GetSectionMaxPerFeed(_pinnedSection) +
                    GetSectionMaxPerFeed(_readLaterSection) +
                    GetSectionMaxPerFeed(_archivedSection));
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

                // The main-window keyword search must not influence "important keyword"
                // notifications. When a search filter is active, notifications are evaluated
                // from a second unfiltered feed snapshot.
                if (_settings.EnableNotifications && !string.IsNullOrWhiteSpace(Keyword))
                {
                    notificationSourceItems = await _feedService.FetchArticlesAsync(
                        _feeds,
                        keywordFilter: null,
                        maxItems: targetPerFeed,
                        advertisementKeywords: advertisementKeywords.Count > 0 ? advertisementKeywords : null);
                }

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
                    if (notificationSourceItems != null)
                    {
                        notificationSourceItems = notificationSourceItems
                            .Where(item => !item.IsAdvertisement)
                            .ToList();
                    }
                }

                var itemsBeforeDedupe = items;

                var articlesBeforeDedupe = itemsBeforeDedupe.Count(item => !IsTechnicalFeedEntry(item));
                var preferredFeedOrder = _feeds
                    .Where(feed => feed?.IsEnabled == true && !string.IsNullOrWhiteSpace(feed.Name))
                    .Select(feed => feed.Name.Trim())
                    .ToList();

                items = RemoveCrossFeedDuplicates(
                    itemsBeforeDedupe,
                    perFeedQuota: targetPerFeed,
                    preferredFeedOrder: preferredFeedOrder);

                if (notificationSourceItems != null)
                {
                    notificationSourceItems = RemoveCrossFeedDuplicates(
                        notificationSourceItems,
                        perFeedQuota: targetPerFeed,
                        preferredFeedOrder: preferredFeedOrder);
                }

                var articlesAfterDedupe = items.Count(item => !IsTechnicalFeedEntry(item));
                duplicateItemsRemoved = Math.Max(0, articlesBeforeDedupe - articlesAfterDedupe);

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
                var archivedLookup = new Dictionary<string, List<FeedItem>>(StringComparer.OrdinalIgnoreCase);
                ApplyArchiveAutoCleanupSilently();
                var archivedTimestamps = (_settings.ArchivedArticleSnapshots ?? new List<FeedItem>())
                    .Where(s => !string.IsNullOrWhiteSpace(s?.Link))
                    .GroupBy(s => s.Link.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        g => g.Key,
                        g => g.OrderByDescending(s => s.ArchivedAt ?? DateTime.MinValue)
                              .FirstOrDefault()?.ArchivedAt,
                        StringComparer.OrdinalIgnoreCase);

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
                        snap.IsArchived = targetLookup == archivedLookup;
                        ApplyStoredLabelsToItem(snap);
                        ApplyStoredNoteToItem(snap);
                        AddToLookup(targetLookup, snap);
                    }
                }

                var regularItems = new List<FeedItem>();

                foreach (var item in items)
                {
                    var key = item.Link?.Trim();
                    ApplyStoredLabelsToItem(item);
                    ApplyStoredNoteToItem(item);
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        if (_archivedArticleLinks.Contains(key))
                        {
                            item.IsArchived = true;
                            item.IsPinned = false;
                            item.IsReadLater = false;
                            item.ArchivedAt = archivedTimestamps.TryGetValue(key, out var archivedAt)
                                ? archivedAt
                                : item.ArchivedAt ?? ResolveArchiveRetentionDate(item) ?? DateTime.Now;
                            AddToLookup(archivedLookup, item);
                            continue;
                        }

                        if (_pinnedArticleLinks.Contains(key))
                        {
                            item.IsPinned = true;
                            item.IsReadLater = false;
                            item.IsArchived = false;
                            item.ArchivedAt = null;
                            AddToLookup(pinnedLookup, item);
                            continue;
                        }

                        if (_readLaterArticleLinks.Contains(key))
                        {
                            item.IsReadLater = true;
                            item.IsPinned = false;
                            item.IsArchived = false;
                            item.ArchivedAt = null;
                            AddToLookup(readLaterLookup, item);
                            continue;
                        }
                    }

                    item.IsPinned = false;
                    item.IsReadLater = false;
                    item.IsArchived = false;
                    item.ArchivedAt = null;
                    regularItems.Add(item);
                }

                AddSnapshotIfMissing(_settings.PinnedArticleSnapshots, pinnedLookup);
                AddSnapshotIfMissing(_settings.ReadLaterArticleSnapshots, readLaterLookup);
                AddSnapshotIfMissing(_settings.ArchivedArticleSnapshots, archivedLookup);

                items = regularItems;

                // Clamp items per feed to the configured MaxFeeds for My Feeds display
                items = items
                    .GroupBy(i => string.IsNullOrWhiteSpace(i.FeedName) ? "Feed" : i.FeedName)
                    .SelectMany(g => g
                        .OrderByDescending(it => it.PublicationDate)
                        .Take(MaxFeeds))
                    .ToList();

                if (notificationSourceItems != null)
                {
                    notificationSourceItems = notificationSourceItems
                        .GroupBy(i => string.IsNullOrWhiteSpace(i.FeedName) ? "Feed" : i.FeedName)
                        .SelectMany(g => g
                            .OrderByDescending(it => it.PublicationDate)
                            .Take(MaxFeeds))
                        .ToList();
                }

                // Important notifications ignore the main-window search keyword and only use
                // Important Feeds / Important Keywords from Settings.
                var visibleItems = items.ToList();
                var notificationItems = notificationSourceItems ?? visibleItems;
                var importantKeywords = GetImportantKeywordsForNotifications();
                notificationCandidates = RemoveCrossFeedDuplicates(
                    GetNewImportantItemsForNotifications(notificationItems, importantKeywords));
                UpdateKnownArticleLinks(notificationItems);

                // Group items by category
                var categorizedItems = items
                    .Where(it =>
                    {
                        var link = it.Link?.Trim();
                        if (string.IsNullOrWhiteSpace(link))
                        {
                            return true;
                        }
                        return !_pinnedArticleLinks.Contains(link) &&
                               !_readLaterArticleLinks.Contains(link) &&
                               !_archivedArticleLinks.Contains(link);
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
                await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);

                // Update categories in-place
                for (int categoryIndex = 0; categoryIndex < desiredCategoryOrder.Count; categoryIndex++)
                {
                    var categoryName = desiredCategoryOrder[categoryIndex];
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

                        UpdateItemsInPlace(feedVm, feedGroup.ToList(), previousReadStates);
                    }

                    if ((categoryIndex + 1) % 2 == 0)
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
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

                OnPropertyChanged(nameof(CurrentFeedSettingsDisplay));
                UpdateFeedSection(_pinnedSection, pinnedLookup);
                UpdateFeedSection(_readLaterSection, readLaterLookup);
                UpdateFeedSection(_archivedSection, archivedLookup);
                SyncMyFeedsSection();
                SelectedSection ??= _myFeedsSection;

                _allCategoryGroups.Clear();
                foreach (var cat in CategoryGroups)
                {
                    _allCategoryGroups.Add(cat);
                }
                RebuildFilterListsForSection(_selectedSection ?? _myFeedsSection, scheduleFilterApply: false);
                ApplyTreeFilter();

                TryNotifyImportantItems(notificationCandidates);

                refreshTimer.Stop();
                refreshSummaryMessage =
                    $"Refreshed {totalEnabledFeeds} feed{(totalEnabledFeeds == 1 ? "" : "s")} in {refreshTimer.Elapsed.TotalSeconds:F1}s - " +
                    $"{totalFetchedItems} item{(totalFetchedItems == 1 ? "" : "s")}" +
                    (duplicateItemsRemoved > 0 ? $", {duplicateItemsRemoved} duplicates removed" : string.Empty) +
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

                    if (match == null && _archivedSection != null)
                    {
                        match = _archivedSection.Items
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
                if (completeInitialRefreshAfterLoad)
                {
                    _hasCompletedInitialRefresh = true;
                    OnPropertyChanged(nameof(IsNotLoading));
                    OnPropertyChanged(nameof(ShowLoadingOverlay));
                }
                _suppressSelectionDuringRefresh = false;
                if (!string.IsNullOrWhiteSpace(linkToRestore))
                {
                    SelectionRestoreRequested?.Invoke(linkToRestore);
                }
                // Always attempt to restore previous scroll position
                ScrollOffsetRestoreRequested?.Invoke(_lastTreeScrollOffset);
                if (!string.IsNullOrWhiteSpace(refreshSummaryMessage) && !suppressSummaryBanner)
                {
                    SnackbarMessageQueue?.Enqueue(refreshSummaryMessage);
                }
                _refreshLock.Release();
            }
        }

        private void SaveFeedSettings()
        {
            try
            {
                _settings.MaxFeeds = _maxFeeds;
                _settings.IsShowContentAlwaysOn = _isShowContentAlwaysOn;
                _settings.AutoRefresh = _pendingAutoRefresh;
                _settings.LiveRefresh = _pendingLiveRefresh;
                _settings.LiveRefreshIntervalSeconds = NormalizeLiveRefreshIntervalSeconds(_pendingLiveRefreshIntervalSeconds);
                _pendingLiveRefreshIntervalSeconds = _settings.LiveRefreshIntervalSeconds;
                _settings.AutoRefreshIntervalMinutes = _pendingAutoRefreshIntervalMinutes;
                _settings.TreeWidth = _pendingTreeWidth;
                _settings.DarkMode = _pendingDarkMode;
                _settings.AutoUpdateCheckEnabled = _autoUpdateEnabledCached;
                _settings.StartInTerminal = _pendingStartInTerminal;
                _settings.TerminalThemeName = NormalizeTerminalThemeName(_pendingTerminalThemeName);
                _pendingTerminalThemeName = _settings.TerminalThemeName;
                _settings.TerminalBootAnimationEnabled = _pendingTerminalBootAnimationEnabled;
                _settings.AdBlockerEnabled = _pendingAdBlockerEnabled;
                _settings.AdvertisementFilterEnabled = _pendingAdvertisementFilterEnabled;
                _settings.AdvertisementKeywords = ParseAdvertisementKeywords(_pendingAdvertisementKeywordsText);
                _settings.EnableNotifications = _pendingDesktopNotificationsEnabled;
                _settings.ImportantKeywords = ParseKeywordList(_pendingImportantKeywordsText);
                SaveSettingsPreservingImportantNotifications();

                OnPropertyChanged(nameof(CurrentFeedSettingsDisplay));
                OnPropertyChanged(nameof(MaxFeeds));
                OnPropertyChanged(nameof(DarkMode));
                OnPropertyChanged(nameof(StartInTerminal));
                OnPropertyChanged(nameof(TerminalThemeName));
                OnPropertyChanged(nameof(TerminalBootAnimationEnabled));
                OnPropertyChanged(nameof(AdBlockerEnabled));
                OnPropertyChanged(nameof(AdvertisementFilterEnabled));
                OnPropertyChanged(nameof(AdvertisementKeywordsText));
                OnPropertyChanged(nameof(DesktopNotificationsEnabled));
                OnPropertyChanged(nameof(ImportantKeywordsText));
                OnPropertyChanged(nameof(AutoRefresh));
                OnPropertyChanged(nameof(PendingAutoRefresh));
                OnPropertyChanged(nameof(PendingLiveRefresh));
                OnPropertyChanged(nameof(PendingLiveRefreshIntervalSeconds));
                OnPropertyChanged(nameof(PendingTreeWidth));
                OnPropertyChanged(nameof(IsAutoRefreshModeOptionsVisible));
                OnPropertyChanged(nameof(IsIntervalAutoRefreshSettingsVisible));
                OnPropertyChanged(nameof(IsLiveRefreshSettingsVisible));
                OnPropertyChanged(nameof(IsLiveAutoRefreshSelected));
                OnPropertyChanged(nameof(IsLiveRefreshExpertModeWarningVisible));
                OnPropertyChanged(nameof(AutoRefreshIntervalDisplay));
                OnPropertyChanged(nameof(IsNotLoading));
                OnPropertyChanged(nameof(ShowLoadingOverlay));
                _pendingDarkMode = _settings.DarkMode;
                OnPropertyChanged(nameof(PendingDarkMode));
                _pendingStartInTerminal = _settings.StartInTerminal;
                OnPropertyChanged(nameof(PendingStartInTerminal));
                OnPropertyChanged(nameof(PendingTerminalThemeName));
                OnPropertyChanged(nameof(PendingTerminalBootAnimationEnabled));
                _pendingAdBlockerEnabled = _settings.AdBlockerEnabled;
                _pendingAdvertisementFilterEnabled = _settings.AdvertisementFilterEnabled;
                _pendingAdvertisementKeywordsText = string.Join(Environment.NewLine, _settings.AdvertisementKeywords ?? new List<string>());
                _pendingDesktopNotificationsEnabled = _settings.EnableNotifications;
                _pendingImportantKeywordsText = string.Join(Environment.NewLine, _settings.ImportantKeywords ?? new List<string>());
                OnPropertyChanged(nameof(PendingAdBlockerEnabled));
                OnPropertyChanged(nameof(PendingAdvertisementFilterEnabled));
                OnPropertyChanged(nameof(PendingAdvertisementKeywordsText));
                OnPropertyChanged(nameof(PendingDesktopNotificationsEnabled));
                OnPropertyChanged(nameof(PendingImportantKeywordsText));

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
                _browserService.SetAdBlockerEnabled(_settings.AdBlockerEnabled);
                _embeddedBrowserSession?.SetAdBlockerEnabled(_settings.AdBlockerEnabled);

                _ = RefreshFeedsAsync();
                SnackbarMessageQueue?.Enqueue("Settings have been saved.");
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

            SaveSettingsPreservingImportantNotifications();

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

            if (filteringMyFeeds && !hasFilter && !categoryFilterActive && !feedFilterActive)
            {
                SyncCollectionInPlace(CategoryGroups, sourceCategories);
                SyncCollectionInPlace(_myFeedsSection.Items, sourceCategories.Cast<object>().ToList());
                _myFeedsSection.UpdateUnreadCount();
                RefreshCurrentArticleList();
                EnsureSelectionWithinCurrentSection();
                _sectionNeedsFilterApply[_myFeedsSection] = false;
                return;
            }

            if (!filteringMyFeeds)
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
                        filteredCategories.Add(new CategoryGroupViewModel
                        {
                            Name = cat.Name,
                            IconKind = cat.IconKind,
                            IsExpanded = cat.IsExpanded,
                            HideUnreadIndicators = cat.HideUnreadIndicators,
                            Feeds = new ObservableCollection<FeedGroupViewModel>(matchingFeeds)
                        });
                    }
                }

                var targetSection = _selectedSection ?? _myFeedsSection;
                var desiredItems = filteredCategories.Cast<object>().ToList();
                SyncCollectionInPlace(targetSection.Items, desiredItems);
                _selectedSection?.UpdateUnreadCount();
                RefreshCurrentArticleList();
                EnsureSelectionWithinCurrentSection();
                _sectionNeedsFilterApply[targetSection] = false;
                return;
            }

            var myFeedsDisplayCategories = new List<CategoryGroupViewModel>();
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
                    myFeedsDisplayCategories.Add(displayCategory);
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
                SyncCollectionInPlace(CategoryGroups, myFeedsDisplayCategories);
                SyncCollectionInPlace(_myFeedsSection.Items, myFeedsDisplayCategories.Cast<object>().ToList());
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
            RefreshCurrentArticleList();
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
            if (section == _pinnedSection || section == _readLaterSection || section == _archivedSection)
            {
                var cached = section == _pinnedSection
                    ? _pinnedSourceFeeds
                    : section == _readLaterSection
                        ? _readLaterSourceFeeds
                        : _archivedSourceFeeds;

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
            var visibleItems = CurrentArticleItems.ToList();

            if (visibleItems.Count == 0)
            {
                ClearArticleSelection();
                return;
            }

            if (section == _myFeedsSection)
            {
                // For My Feeds: keep the current selection if it is still valid,
                // otherwise fall back to the first visible article in the list.
                if (_currentSelectedItem != null && visibleItems.Contains(_currentSelectedItem))
                {
                    return;
                }

                OnArticleSelected(visibleItems[0], enableAutoScroll: false);
                return;
            }

            if (_currentSelectedItem == null || !visibleItems.Contains(_currentSelectedItem))
            {
                OnArticleSelected(visibleItems[0], enableAutoScroll: false);
            }
        }

        private void SelectMainAllArticles()
        {
            _selectedMainCategoryName = null;
            _selectedMainFeedName = null;
            RefreshCurrentArticleList();
        }

        private void SelectMainCategory(CategoryGroupViewModel category)
        {
            if (category == null)
            {
                return;
            }

            _selectedMainCategoryName = category.Name;
            _selectedMainFeedName = null;
            RefreshCurrentArticleList();
        }

        private void SelectMainFeed(FeedGroupViewModel feed)
        {
            if (feed == null)
            {
                return;
            }

            _selectedMainCategoryName = feed.Category;
            _selectedMainFeedName = feed.Name;
            RefreshCurrentArticleList();
        }

        private void RefreshCurrentArticleList()
        {
            var items = BuildMainArticleListForCurrentScope();
            SyncCollectionInPlace(CurrentArticleItems, items);
            UpdateNavigationSelectionState();
            UpdateCurrentArticleListTitle();

            using (CurrentArticleItemsView.DeferRefresh())
            {
                CurrentArticleItemsView.GroupDescriptions.Clear();
                if (string.IsNullOrWhiteSpace(_selectedMainFeedName))
                {
                    CurrentArticleItemsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FeedItem.FeedName)));
                }
            }

            if (_currentSelectedItem != null && CurrentArticleItems.Contains(_currentSelectedItem))
            {
                return;
            }

            if (CurrentArticleItems.Count > 0)
            {
                OnArticleSelected(CurrentArticleItems[0], enableAutoScroll: false);
            }
            else
            {
                ClearArticleSelection();
            }
        }

        private List<FeedItem> BuildMainArticleListForCurrentScope()
        {
            if (!string.IsNullOrWhiteSpace(_selectedMainFeedName))
            {
                var selectedFeed = CategoryGroups
                    .SelectMany(category => category.Feeds ?? Enumerable.Empty<FeedGroupViewModel>())
                    .FirstOrDefault(feed => string.Equals(feed.Name, _selectedMainFeedName, StringComparison.OrdinalIgnoreCase));

                return BuildOrderedMainListForFeed(selectedFeed, selectedFeed?.Category);
            }

            if (!string.IsNullOrWhiteSpace(_selectedMainCategoryName))
            {
                var selectedCategory = CategoryGroups
                    .FirstOrDefault(category => string.Equals(category.Name, _selectedMainCategoryName, StringComparison.OrdinalIgnoreCase));

                if (selectedCategory == null)
                {
                    return new List<FeedItem>();
                }

                return BuildOrderedMainListForFeeds(
                    selectedCategory.Feeds ?? Enumerable.Empty<FeedGroupViewModel>(),
                    fallbackCategoryName: selectedCategory.Name);
            }

            var orderedItems = new List<FeedItem>();
            foreach (var category in CategoryGroups)
            {
                orderedItems.AddRange(BuildOrderedMainListForFeeds(
                    category.Feeds ?? Enumerable.Empty<FeedGroupViewModel>(),
                    fallbackCategoryName: category.Name));
            }

            return orderedItems;
        }

        private List<FeedItem> BuildOrderedMainListForFeeds(IEnumerable<FeedGroupViewModel> feeds, string fallbackCategoryName)
        {
            var orderedItems = new List<FeedItem>();
            foreach (var feed in feeds ?? Enumerable.Empty<FeedGroupViewModel>())
            {
                orderedItems.AddRange(BuildOrderedMainListForFeed(feed, fallbackCategoryName));
            }

            return orderedItems;
        }

        private List<FeedItem> BuildOrderedMainListForFeed(FeedGroupViewModel feed, string fallbackCategoryName)
        {
            if (feed?.Items == null)
            {
                return new List<FeedItem>();
            }

            var categoryName = string.IsNullOrWhiteSpace(feed.Category)
                ? fallbackCategoryName ?? "Default"
                : feed.Category;

            var orderedItems = feed.Items
                .OrderByDescending(item => item.PublicationDate)
                .ThenBy(item => item.Title)
                .ToList();

            foreach (var item in orderedItems)
            {
                item.CategoryName = categoryName;
            }

            return orderedItems;
        }

        private void UpdateNavigationSelectionState()
        {
            foreach (var category in CategoryGroups)
            {
                category.IsNavigationSelected =
                    !string.IsNullOrWhiteSpace(_selectedMainCategoryName) &&
                    string.Equals(category.Name, _selectedMainCategoryName, StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrWhiteSpace(_selectedMainFeedName);

                foreach (var feed in category.Feeds ?? Enumerable.Empty<FeedGroupViewModel>())
                {
                    feed.IsNavigationSelected =
                        !string.IsNullOrWhiteSpace(_selectedMainFeedName) &&
                        string.Equals(feed.Name, _selectedMainFeedName, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        private void UpdateCurrentArticleListTitle()
        {
            if (!string.IsNullOrWhiteSpace(_selectedMainFeedName))
            {
                CurrentArticleListTitle = _selectedMainFeedName;
            }
            else if (!string.IsNullOrWhiteSpace(_selectedMainCategoryName))
            {
                CurrentArticleListTitle = _selectedMainCategoryName;
            }
            else
            {
                CurrentArticleListTitle = "Latest Articles";
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

        private void RebuildFilterListsForSection(
            ArticleSectionViewModel section,
            bool resetSelections = false,
            bool scheduleFilterApply = true)
        {
            var source = GetSourceCategoriesForSection(section ?? _myFeedsSection);

            var sectionFeeds = (section == _pinnedSection || section == _readLaterSection || section == _archivedSection)
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

            if (scheduleFilterApply)
            {
                ScheduleFilterApply();
                MarkSectionDirty(section ?? _myFeedsSection);
            }
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

        private static void UpdateItemsInPlace(FeedGroupViewModel feedGroup, List<FeedItem> newItems, HashSet<string> readStates)
        {
            if (feedGroup?.Items == null || newItems == null)
            {
                return;
            }

            var existingItems = feedGroup.Items;

            var existingByKey = new Dictionary<string, FeedItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var existing in existingItems)
            {
                var existingKey = existing?.Link?.Trim();
                if (!string.IsNullOrWhiteSpace(existingKey) && !existingByKey.ContainsKey(existingKey))
                {
                    existingByKey[existingKey] = existing;
                }
            }

            var desiredItems = new List<FeedItem>(newItems.Count);
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var incoming in newItems)
            {
                if (incoming == null)
                {
                    continue;
                }

                var key = incoming.Link?.Trim();
                if (!string.IsNullOrWhiteSpace(key) && !seenKeys.Add(key))
                {
                    continue;
                }

                FeedItem target;
                if (!string.IsNullOrWhiteSpace(key) && existingByKey.TryGetValue(key, out var existingTarget))
                {
                    target = existingTarget;
                    target.Title = incoming.Title;
                    target.Description = incoming.Description;
                    target.PublicationDate = incoming.PublicationDate;
                    target.IsAdvertisement = incoming.IsAdvertisement;
                    target.FeedName = incoming.FeedName;
                    target.FeedUrl = incoming.FeedUrl;
                    target.IsPinned = incoming.IsPinned;
                    target.IsReadLater = incoming.IsReadLater;
                    target.IsArchived = incoming.IsArchived;
                    target.ArchivedAt = incoming.ArchivedAt;
                    target.IsRead = incoming.IsRead;
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(key) && readStates.Contains(key))
                    {
                        incoming.IsRead = true;
                    }

                    target = incoming;
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        existingByKey[key] = target;
                    }
                }

                desiredItems.Add(target);
            }

            using (feedGroup.BeginItemsBatchUpdate())
            {
                SyncFeedItemsInPlace(existingItems, desiredItems);
            }
        }

        private static void SyncFeedItemsInPlace(ObservableCollection<FeedItem> existingItems, IList<FeedItem> desiredItems)
        {
            if (existingItems == null || desiredItems == null)
            {
                return;
            }

            var desiredSet = new HashSet<FeedItem>(desiredItems);
            for (var index = existingItems.Count - 1; index >= 0; index--)
            {
                if (!desiredSet.Contains(existingItems[index]))
                {
                    existingItems.RemoveAt(index);
                }
            }

            for (var desiredIndex = 0; desiredIndex < desiredItems.Count; desiredIndex++)
            {
                var item = desiredItems[desiredIndex];
                if (desiredIndex < existingItems.Count && ReferenceEquals(existingItems[desiredIndex], item))
                {
                    continue;
                }

                var currentIndex = existingItems.IndexOf(item);
                if (currentIndex < 0)
                {
                    existingItems.Insert(Math.Min(desiredIndex, existingItems.Count), item);
                    continue;
                }

                // Keep existing relative order as stable as possible to avoid UI flicker.
                if (currentIndex > desiredIndex)
                {
                    existingItems.Move(currentIndex, desiredIndex);
                }
            }
        }

        private static void SyncCollectionInPlace<T>(ObservableCollection<T> existingItems, IList<T> desiredItems)
        {
            if (existingItems == null || desiredItems == null)
            {
                return;
            }

            for (var desiredIndex = 0; desiredIndex < desiredItems.Count; desiredIndex++)
            {
                var item = desiredItems[desiredIndex];
                var currentIndex = existingItems.IndexOf(item);
                if (currentIndex < 0)
                {
                    existingItems.Insert(Math.Min(desiredIndex, existingItems.Count), item);
                    continue;
                }

                if (currentIndex != desiredIndex)
                {
                    existingItems.Move(currentIndex, desiredIndex);
                }
            }

            for (var index = existingItems.Count - 1; index >= 0; index--)
            {
                if (!desiredItems.Contains(existingItems[index]))
                {
                    existingItems.RemoveAt(index);
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
                    SaveSettingsPreservingImportantNotifications();

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
                    SaveSettingsPreservingImportantNotifications();

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
                SaveSettingsPreservingImportantNotifications();

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
                _autoRefreshTimer.Interval = GetAutoRefreshInterval();
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
            var postponed = false;
            try
            {
                if (HasOpenContextMenu?.Invoke() == true)
                {
                    PostponeAutoRefresh(TimeSpan.FromSeconds(10));
                    postponed = true;
                    return;
                }

                if (_autoRefreshTimer != null)
                {
                    _autoRefreshTimer.Interval = GetAutoRefreshInterval();
                }

                var suppressSummaryBanner = _settings?.LiveRefresh == true;
                await RefreshFeedsAsync(suppressSummaryBanner: suppressSummaryBanner);
            }
            catch (Exception)
            {
            }
            finally
            {
                if (AutoRefresh && !postponed)
                {
                    SetNextAutoRefreshTime();
                }
            }
        }

        private void PostponeAutoRefresh(TimeSpan delay)
        {
            if (!AutoRefresh)
            {
                NextAutoRefreshDisplay = string.Empty;
                return;
            }

            _nextAutoRefreshTime = DateTime.Now.Add(delay);
            _autoRefreshTimer?.Stop();
            if (_autoRefreshTimer != null)
            {
                _autoRefreshTimer.Interval = delay;
                _autoRefreshTimer.Start();
            }

            UpdateNextAutoRefreshDisplay();
            StartAutoRefreshCountdown();
        }

        private void SetNextAutoRefreshTime()
        {
            if (!AutoRefresh)
            {
                NextAutoRefreshDisplay = string.Empty;
                return;
            }

            _nextAutoRefreshTime = DateTime.Now.Add(GetAutoRefreshInterval());
            UpdateNextAutoRefreshDisplay();
            StartAutoRefreshCountdown();
        }

        private TimeSpan GetAutoRefreshInterval()
        {
            if (_settings.LiveRefresh)
            {
                return TimeSpan.FromSeconds(NormalizeLiveRefreshIntervalSeconds(_settings.LiveRefreshIntervalSeconds));
            }

            var minutes = _settings.AutoRefreshIntervalMinutes > 0
                ? _settings.AutoRefreshIntervalMinutes
                : 10;

            return TimeSpan.FromMinutes(minutes);
        }

        private static int NormalizeLiveRefreshIntervalSeconds(int seconds)
        {
            if (AppSettings.AvailableLiveRefreshIntervals.Contains(seconds))
            {
                return seconds;
            }

            return 60;
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
                TerminalViewModel.ClearPersistentReaderCache();

                System.Windows.MessageBox.Show(
                    "Cache has been cleared successfully!\n\n" +
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

                if (_desktopNotificationService != null)
                {
                    _desktopNotificationService.RecentImportantItemCountChanged -= OnRecentImportantItemCountChanged;
                }
                _desktopNotificationService?.Dispose();
                _articleReaderService?.Dispose();

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

        private string GetOrCreateArticleSummaryHtml(FeedItem feedItem)
        {
            if (feedItem == null)
            {
                return string.Empty;
            }

            if (string.IsNullOrEmpty(feedItem.CachedArticleSummaryHtml))
            {
                feedItem.CachedArticleSummaryHtml = CreateArticleHtml(feedItem);
            }

            return feedItem.CachedArticleSummaryHtml;
        }

        private string GetOrCreateArticlePlainText(FeedItem feedItem)
        {
            if (feedItem == null)
            {
                return string.Empty;
            }

            if (string.IsNullOrEmpty(feedItem.CachedArticlePlainText))
            {
                feedItem.CachedArticlePlainText = BuildArticlePlainText(feedItem.Title, feedItem.Description);
            }

            return feedItem.CachedArticlePlainText;
        }

        private void InvalidateAllArticlePreviewCaches()
        {
            foreach (var item in CategoryGroups
                         .SelectMany(category => category.Feeds ?? Enumerable.Empty<FeedGroupViewModel>())
                         .SelectMany(feed => feed.Items ?? Enumerable.Empty<FeedItem>()))
            {
                item?.InvalidateArticlePreviewCache();
            }
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
                if (_isReaderModeActive)
                {
                    var normalizedLink = NormalizeExternalLink(_currentSelectedItem.Link);
                    if (!string.IsNullOrWhiteSpace(normalizedLink) &&
                        _readerContentCache.TryGetValue(normalizedLink, out var readerContent) &&
                        !string.IsNullOrWhiteSpace(readerContent?.HtmlContent))
                    {
                        var readerItem = new FeedItem
                        {
                            FeedName = _currentSelectedItem.FeedName,
                            FeedUrl = _currentSelectedItem.FeedUrl,
                            Title = string.IsNullOrWhiteSpace(readerContent.Title) ? _currentSelectedItem.Title : readerContent.Title,
                            Description = readerContent.HtmlContent,
                            Link = _currentSelectedItem.Link,
                            PublicationDate = _currentSelectedItem.PublicationDate
                        };
                        SetReaderModeActive(true);
                        SelectedArticleHtml = CreateArticleHtml(readerItem);
                        SelectedArticleText = string.IsNullOrWhiteSpace(readerContent.PlainText)
                            ? BuildArticlePlainText(readerItem.Title, readerItem.Description)
                            : readerContent.PlainText;
                        return;
                    }

                    // If cached reader content is gone, fall back to summary mode.
                    SetReaderModeActive(false);
                }

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

        public bool TryOpenArticleFromNotification(string link, bool openInSeparateWindow)
        {
            if (string.IsNullOrWhiteSpace(link) || ArticleSections == null)
            {
                return false;
            }

            var trimmed = link.Trim();
            FeedItem match = null;
            ArticleSectionViewModel matchSection = null;

            foreach (var section in ArticleSections)
            {
                var candidate = FindItemByLinkShallow(section, trimmed);
                if (candidate == null)
                {
                    continue;
                }

                match = candidate;
                matchSection = section;
                break;
            }

            if (match == null)
            {
                return false;
            }

            if (matchSection != null && !ReferenceEquals(SelectedSection, matchSection))
            {
                SelectedSection = matchSection;
            }

            if (openInSeparateWindow)
            {
                _ = OpenArticleInWindowAsync(match);
            }
            else
            {
                OnArticleSelected(match, enableAutoScroll: false, preserveOpenWebViews: true);
            }

            return true;
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
                OnArticleSelected(match, enableAutoScroll: false, preserveOpenWebViews: true);
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
