using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using MyNewsFeeder.Models;
using MyNewsFeeder.Services;
using MyNewsFeeder.ViewModels;

namespace MyNewsFeeder.Views
{
    public partial class ArchiveWindow : Window, INotifyPropertyChanged
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

        private readonly LibrarySectionMode _initialMode;
        private readonly ObservableCollection<ArchiveEntryViewModel> _archiveEntries = new ObservableCollection<ArchiveEntryViewModel>();
        private readonly ArticleReaderService _articleReaderService = new ArticleReaderService();
        private ICollectionView _archiveEntriesView;
        private MainViewModel _viewModel;
        private BrowserSession _browserSession;
        private bool _isUpdatingControls;
        private readonly DispatcherTimer _preferencesSaveTimer = new DispatcherTimer();
        private readonly DispatcherTimer _noteSaveTimer = new DispatcherTimer();
        private string _pendingCategorySelection = "All categories";
        private string _pendingFeedSelection = "All feeds";
        private string _pendingLabelSelection = "All labels";
        private ArchiveEntryViewModel _selectedArchiveEntry;
        private bool _isUpdatingNoteEditor;
        private bool _isColumnPopupDirty;
        private bool _isQuickUnreadActive;
        private bool _isQuickHasLabelsActive;
        private bool _isQuickLast7DaysActive;
        private bool _isQuickLast30DaysActive;
        private bool _isConfigurationPanelVisible = true;
        private bool _isPreviewVisible = true;
        private string _selectedExportScopeKey = "filtered";
        private bool _isApplyingWindowPlacement;
        private bool _hasInitialWindowPlacementApplied;
        private WindowState _lastNonMinimizedWindowState = WindowState.Maximized;
        private LibrarySectionMode _currentMode;
        private ReaderArticleContent _readerContent;
        private Task<bool> _previewWebViewInitializationTask;
        private bool _previewWebViewInitialized;
        private bool _showOriginalContent;
        private bool _isPreviewVideoPlaybackEnabled;
        private bool _isReaderModeActive;
        private bool _isReaderModeLoading;
        private bool _isPreviewPageLoading;
        private bool _hasRevealedInitialFrame;
        private bool _isPreviewFullscreen;
        private bool _archivedColumnRequestedVisible = true;
        private bool _isSynchronizingSelection;
        private GridLength _savedOverviewColumnWidth;
        private GridLength _savedOverviewSplitterColumnWidth;
        private GridLength _savedItemsColumnWidth;
        private GridLength _savedPreviewSplitterColumnWidth;
        private GridLength _savedPreviewColumnWidth;
        private WindowState _savedWindowState;

        public event PropertyChangedEventHandler PropertyChanged;
        public ICommand ContextOpenArticleCommand { get; }
        public ICommand ContextUnarchiveSelectionCommand { get; }
        public ICommand ContextPinSelectionCommand { get; }
        public ICommand ContextReadLaterSelectionCommand { get; }
        public ICommand ContextArchiveSelectionCommand { get; }
        public ICommand ContextMarkUnreadSelectionCommand { get; }
        public ICommand ContextOpenLinksSelectionCommand { get; }
        public ICommand ContextCopyLinksSelectionCommand { get; }
        public ICommand SelectGroupCommand { get; }

        private static void WriteArchiveDiagnostic(string message, Exception ex = null)
        {
            try
            {
                var logDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MyNewsFeeder",
                    "logs");
                Directory.CreateDirectory(logDirectory);

                var logPath = Path.Combine(logDirectory, "archive-window-diagnostics.log");
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";
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

        private sealed class SortOption
        {
            public string Key { get; init; } = string.Empty;
            public string Label { get; init; } = string.Empty;
        }

        private sealed class SortDirectionOption
        {
            public ListSortDirection Direction { get; init; }
            public string Label { get; init; } = string.Empty;
        }

        private sealed class GroupOption
        {
            public string Key { get; init; } = string.Empty;
            public string Label { get; init; } = string.Empty;
        }

        private sealed class CleanupOption
        {
            public int Days { get; init; }
            public string Label { get; init; } = string.Empty;
        }

        private sealed class ArchiveExportEntry
        {
            public string Title { get; init; } = string.Empty;
            public string Feed { get; init; } = string.Empty;
            public string Category { get; init; } = string.Empty;
            public string Labels { get; init; } = string.Empty;
            public string Note { get; init; } = string.Empty;
            public DateTime? ArchivedAt { get; init; }
            public DateTime PublicationDate { get; init; }
            public bool IsRead { get; init; }
            public string Link { get; init; } = string.Empty;
            public string Description { get; init; } = string.Empty;
        }

        public ArchiveEntryViewModel SelectedArchiveEntry
        {
            get => _selectedArchiveEntry;
            private set
            {
                SaveSelectedNote();

                if (ReferenceEquals(_selectedArchiveEntry, value))
                {
                    return;
                }

                _selectedArchiveEntry = value;
                OnPropertyChanged(nameof(SelectedArchiveEntry));
                OnPropertyChanged(nameof(HasPreviewSelection));
            }
        }

        public bool HasPreviewSelection => SelectedArchiveEntry != null;

        public bool IsQuickUnreadActive
        {
            get => _isQuickUnreadActive;
            private set
            {
                if (_isQuickUnreadActive == value)
                {
                    return;
                }

                _isQuickUnreadActive = value;
                OnPropertyChanged(nameof(IsQuickUnreadActive));
            }
        }

        public bool IsQuickHasLabelsActive
        {
            get => _isQuickHasLabelsActive;
            private set
            {
                if (_isQuickHasLabelsActive == value)
                {
                    return;
                }

                _isQuickHasLabelsActive = value;
                OnPropertyChanged(nameof(IsQuickHasLabelsActive));
            }
        }

        public bool IsQuickLast7DaysActive
        {
            get => _isQuickLast7DaysActive;
            private set
            {
                if (_isQuickLast7DaysActive == value)
                {
                    return;
                }

                _isQuickLast7DaysActive = value;
                OnPropertyChanged(nameof(IsQuickLast7DaysActive));
            }
        }

        public bool IsQuickLast30DaysActive
        {
            get => _isQuickLast30DaysActive;
            private set
            {
                if (_isQuickLast30DaysActive == value)
                {
                    return;
                }

                _isQuickLast30DaysActive = value;
                OnPropertyChanged(nameof(IsQuickLast30DaysActive));
            }
        }

        public ArchiveWindow(LibrarySectionMode initialMode = LibrarySectionMode.Archive)
        {
            _initialMode = initialMode;
            _currentMode = initialMode;
            ContextOpenArticleCommand = new RelayCommand(
                param =>
                {
                    if (param is FeedItem item && _viewModel?.OpenArticleInWindowCommand?.CanExecute(item) == true)
                    {
                        _viewModel.OpenArticleInWindowCommand.Execute(item);
                    }
                },
                param => param is FeedItem item && ContextTargetsSingleItem(item));
            ContextUnarchiveSelectionCommand = new RelayCommand(
                param => ExecutePrimaryAction(GetContextTargetItems(param as FeedItem), "selected"),
                param => param is FeedItem);
            ContextPinSelectionCommand = new RelayCommand(
                param => ExecuteBatchCommand(_viewModel?.PinArticleCommand, GetContextTargetItems(param as FeedItem)),
                param => param is FeedItem);
            ContextReadLaterSelectionCommand = new RelayCommand(
                param => ExecuteBatchCommand(_viewModel?.ReadLaterArticleCommand, GetContextTargetItems(param as FeedItem)),
                param => param is FeedItem);
            ContextArchiveSelectionCommand = new RelayCommand(
                param => ExecuteBatchCommand(_viewModel?.ArchiveArticleCommand, GetContextTargetItems(param as FeedItem)),
                param => param is FeedItem);
            ContextMarkUnreadSelectionCommand = new RelayCommand(
                param => ExecuteBatchCommand(_viewModel?.MarkUnreadCommand, GetContextTargetItems(param as FeedItem)),
                param => param is FeedItem);
            ContextOpenLinksSelectionCommand = new RelayCommand(
                param => OpenSelectedLinksExternally(GetContextTargetItems(param as FeedItem)),
                param => param is FeedItem);
            ContextCopyLinksSelectionCommand = new RelayCommand(
                param => CopyLinksToClipboard(GetNormalizedLinks(GetContextTargetItems(param as FeedItem))),
                param => param is FeedItem item && ContextTargetsSingleItem(item));
            SelectGroupCommand = new RelayCommand(
                param => SelectGroup(param as CollectionViewGroup),
                param => param is CollectionViewGroup group && GetItemsForGroup(group).Count > 0);
            _isUpdatingControls = true;
            InitializeComponent();
            PreviewWebView.PreviewKeyDown += PreviewWebView_PreviewKeyDown;
            SourceInitialized += (_, __) => EnableDarkTitleBar();
            Loaded += ArchiveWindow_Loaded;
            Closed += ArchiveWindow_Closed;
            LocationChanged += ArchiveWindow_PlacementChanged;
            SizeChanged += ArchiveWindow_PlacementChanged;
            StateChanged += ArchiveWindow_StateChanged;

            _archiveEntriesView = CollectionViewSource.GetDefaultView(_archiveEntries);
            _archiveEntriesView.Filter = ArchiveEntryMatchesCurrentFilter;
            ArchiveDataGrid.ItemsSource = _archiveEntriesView;
            _preferencesSaveTimer.Interval = TimeSpan.FromMilliseconds(250);
            _preferencesSaveTimer.Tick += PreferencesSaveTimer_Tick;
            _noteSaveTimer.Interval = TimeSpan.FromMilliseconds(350);
            _noteSaveTimer.Tick += NoteSaveTimer_Tick;

            InitializeControls();
            UpdateLibraryModeUi();
            UpdateCounts();
            UpdateSelectionState();
            UpdateEmptyState();
            UpdatePreviewState();
        }

        private void RevealInitialFrame()
        {
            if (_hasRevealedInitialFrame)
            {
                return;
            }

            _hasRevealedInitialFrame = true;
            Opacity = 1;
        }

        public void SwitchToMode(LibrarySectionMode mode)
        {
            if (_currentMode == mode && IsLoaded)
            {
                Activate();
                return;
            }

            _currentMode = mode;
            ResetPreviewBrowserState(navigateToPlaceholder: true);
            UpdateLibraryModeUi();

            if (!IsLoaded)
            {
                return;
            }

            RebuildArchiveEntries();
        }

        private ArticleSectionViewModel GetCurrentSection()
        {
            return _currentMode switch
            {
                LibrarySectionMode.Pinned => _viewModel?.PinnedSection,
                LibrarySectionMode.ReadLater => _viewModel?.ReadLaterSection,
                _ => _viewModel?.ArchivedSection
            };
        }

        private string GetCurrentModeLabel()
        {
            return _currentMode switch
            {
                LibrarySectionMode.Pinned => "Pinned",
                LibrarySectionMode.ReadLater => "Read Later",
                _ => "Archive"
            };
        }

        private void UpdateLibraryModeUi()
        {
            var modeLabel = GetCurrentModeLabel();
            Title = $"Library - {modeLabel}";

            if (LibraryTitleTextBlock != null)
            {
                LibraryTitleTextBlock.Text = "Library";
            }

            if (LibrarySubtitleTextBlock != null)
            {
                LibrarySubtitleTextBlock.Text = _currentMode switch
                {
                    LibrarySectionMode.Pinned => "Review your pinned articles in one place.",
                    LibrarySectionMode.ReadLater => "Work through the articles you saved for later.",
                    _ => "Group, filter and work through archived articles."
                };
            }

            if (PinnedModeButton != null)
            {
                PinnedModeButton.IsChecked = _currentMode == LibrarySectionMode.Pinned;
            }

            if (ReadLaterModeButton != null)
            {
                ReadLaterModeButton.IsChecked = _currentMode == LibrarySectionMode.ReadLater;
            }

            if (ArchiveModeButton != null)
            {
                ArchiveModeButton.IsChecked = _currentMode == LibrarySectionMode.Archive;
            }

            if (ItemsGroupBox != null)
            {
                ItemsGroupBox.Header = $"{modeLabel} Articles";
            }

            if (PrimaryCountLabelTextBlock != null)
            {
                PrimaryCountLabelTextBlock.Text = $"{modeLabel} items";
            }

            if (SelectedNoteTextBox != null)
            {
                HintAssist.SetHint(SelectedNoteTextBox, _currentMode switch
                {
                    LibrarySectionMode.Pinned => "Add a private note for this pinned article",
                    LibrarySectionMode.ReadLater => "Add a private note for this saved article",
                    _ => "Add a private note for this archived article"
                });
            }

            if (PreviewEmptyTitleTextBlock != null)
            {
                PreviewEmptyTitleTextBlock.Text = _currentMode switch
                {
                    LibrarySectionMode.Pinned => "Select a pinned article",
                    LibrarySectionMode.ReadLater => "Select a saved article",
                    _ => "Select an archived article"
                };
            }

            if (PreviewEmptyBodyTextBlock != null)
            {
                PreviewEmptyBodyTextBlock.Text = _currentMode switch
                {
                    LibrarySectionMode.Pinned => "The right panel keeps a quick preview while you review your pinned items.",
                    LibrarySectionMode.ReadLater => "The right panel keeps a quick preview while you work through Read Later.",
                    _ => "The right panel keeps a quick preview while the list stays grouped and filterable."
                };
            }

            if (EmptyStateTitleTextBlock != null)
            {
                EmptyStateTitleTextBlock.Text = _currentMode switch
                {
                    LibrarySectionMode.Pinned => "No pinned articles match the current filters.",
                    LibrarySectionMode.ReadLater => "No Read Later articles match the current filters.",
                    _ => "No archived articles match the current filters."
                };
            }

            if (EmptyStateBodyTextBlock != null)
            {
                EmptyStateBodyTextBlock.Text = _currentMode switch
                {
                    LibrarySectionMode.Pinned => "Adjust the filters or pin more articles.",
                    LibrarySectionMode.ReadLater => "Adjust the filters or save more articles for later.",
                    _ => "Adjust the filters or archive more articles."
                };
            }

            if (SelectionSummaryActionsTextBlock != null)
            {
                SelectionSummaryActionsTextBlock.Text = _currentMode switch
                {
                    LibrarySectionMode.Pinned => "Use the context menu on a selected article to unpin, move it to Read Later, archive it, open links, or copy links for the whole selection.",
                    LibrarySectionMode.ReadLater => "Use the context menu on a selected article to remove it from Read Later, pin it, archive it, open links, or copy links for the whole selection.",
                    _ => "Use the context menu on a selected article to pin, move to Read Later, unarchive, open links, or copy links for the whole selection."
                };
            }

            var isArchiveMode = _currentMode == LibrarySectionMode.Archive;
            if (RetentionPanelBorder != null)
            {
                RetentionPanelBorder.Visibility = isArchiveMode ? Visibility.Visible : Visibility.Collapsed;
            }

            ApplyModeSpecificColumnVisibility();
            UpdateColumnVisibilityMenuChecks();

            if (MoreUnarchiveMenuItem != null)
            {
                MoreUnarchiveMenuItem.Header = GetPrimaryActionHeader();
            }
        }

        private string GetPrimaryActionHeader()
        {
            return _currentMode switch
            {
                LibrarySectionMode.Pinned => "Unpin Selected",
                LibrarySectionMode.ReadLater => "Remove from Read Later",
                _ => "Unarchive"
            };
        }

        private void InitializeControls()
        {
            _isUpdatingControls = true;

            ReadStateComboBox.ItemsSource = new[] { "All", "Unread", "Read" };
            ReadStateComboBox.SelectedIndex = 0;

            GroupByComboBox.ItemsSource = new[]
            {
                new GroupOption { Key = "none", Label = "No grouping" },
                new GroupOption { Key = "feed", Label = "Group by feed" },
                new GroupOption { Key = "category", Label = "Group by category" },
                new GroupOption { Key = "day", Label = "Group by archive day" },
                new GroupOption { Key = "month", Label = "Group by month" }
            };
            GroupByComboBox.SelectedIndex = 1;

            SortFieldComboBox.ItemsSource = new[]
            {
                new SortOption { Key = "archived", Label = "Archived date" },
                new SortOption { Key = "published", Label = "Published date" },
                new SortOption { Key = "category", Label = "Category" },
                new SortOption { Key = "feed", Label = "Feed" },
                new SortOption { Key = "labels", Label = "Labels" },
                new SortOption { Key = "title", Label = "Title" }
            };
            SortFieldComboBox.SelectedIndex = 0;

            SortDirectionComboBox.ItemsSource = new[]
            {
                new SortDirectionOption { Direction = ListSortDirection.Descending, Label = "Descending" },
                new SortDirectionOption { Direction = ListSortDirection.Ascending, Label = "Ascending" }
            };
            SortDirectionComboBox.SelectedIndex = 0;

            CategoryFilterComboBox.ItemsSource = new[] { "All categories" };
            CategoryFilterComboBox.SelectedIndex = 0;
            FeedFilterComboBox.ItemsSource = new[] { "All feeds" };
            FeedFilterComboBox.SelectedIndex = 0;
            LabelFilterComboBox.ItemsSource = new[] { "All labels" };
            LabelFilterComboBox.SelectedIndex = 0;

            CleanupDaysComboBox.ItemsSource = new[]
            {
                new CleanupOption { Days = 7, Label = "Keep 7 days" },
                new CleanupOption { Days = 30, Label = "Keep 30 days" },
                new CleanupOption { Days = 90, Label = "Keep 90 days" },
                new CleanupOption { Days = 180, Label = "Keep 180 days" },
                new CleanupOption { Days = 365, Label = "Keep 365 days" }
            };
            CleanupDaysComboBox.SelectedIndex = 2;
            AutoCleanupEnabledCheckBox.IsChecked = false;

            _isUpdatingControls = false;
        }

        private void ArchiveWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                WriteArchiveDiagnostic("ArchiveWindow_Loaded start.");
                _viewModel = DataContext as MainViewModel;
                if (_viewModel != null)
                {
                    _browserSession ??= _viewModel.CreateBrowserSession();
                    _viewModel.ArchiveItemsChanged += ViewModel_ArchiveItemsChanged;
                    _viewModel.ArticleLabelsChanged += ViewModel_ArticleLabelsChanged;
                    _viewModel.ArticleNotesChanged += ViewModel_ArticleNotesChanged;
                }

                RefreshSavedViewOptions();
                ApplySavedPreferences();
                SwitchToMode(_initialMode);
                RebuildArchiveEntries();
                Dispatcher.InvokeAsync(RevealInitialFrame, DispatcherPriority.Loaded);
                WriteArchiveDiagnostic("ArchiveWindow_Loaded completed.");
            }
            catch (Exception ex)
            {
                WriteArchiveDiagnostic("ArchiveWindow_Loaded failed.", ex);
                throw;
            }
        }

        private void ArchiveWindow_Closed(object sender, EventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.ArchiveItemsChanged -= ViewModel_ArchiveItemsChanged;
                _viewModel.ArticleLabelsChanged -= ViewModel_ArticleLabelsChanged;
                _viewModel.ArticleNotesChanged -= ViewModel_ArticleNotesChanged;
            }

            _preferencesSaveTimer.Stop();
            _noteSaveTimer.Stop();
            try
            {
                _browserSession?.Dispose();
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

            SavePreferencesNow();
            ClearArchiveEntries();
        }

        private void ViewModel_ArchiveItemsChanged()
        {
            Dispatcher.BeginInvoke(new Action(RebuildArchiveEntries));
        }

        private void ViewModel_ArticleLabelsChanged()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                RefreshLabelOptions(_pendingLabelSelection);
                RefreshArchiveView();
            }));
        }

        private void ViewModel_ArticleNotesChanged()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                RefreshArchiveView();
                UpdatePreviewState();
            }));
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

        private void ApplySavedPreferences()
        {
            var preferences = _viewModel?.GetArchiveViewPreferences() ?? new ArchiveViewPreferences();
            ApplyPreferences(preferences, preserveSavedViewName: true, applyWindowPlacement: !_hasInitialWindowPlacementApplied);
        }

        public void PrepareInitialWindowPlacement(ArchiveViewPreferences preferences)
        {
            preferences ??= new ArchiveViewPreferences();
            WindowStartupLocation = WindowStartupLocation.Manual;
            if (!IsLoaded)
            {
                Opacity = 0;
            }

            ApplyWindowPlacement(preferences);
            _hasInitialWindowPlacementApplied = true;
        }

        private void ApplyPreferences(ArchiveViewPreferences preferences, bool preserveSavedViewName, bool applyWindowPlacement = false)
        {
            preferences ??= new ArchiveViewPreferences();
            _isUpdatingControls = true;
            SearchTextBox.Text = preferences.SearchText ?? string.Empty;
            ArchivedFromDatePicker.SelectedDate = preferences.ArchivedFromDate;
            ArchivedToDatePicker.SelectedDate = preferences.ArchivedToDate;
            _pendingCategorySelection = string.IsNullOrWhiteSpace(preferences.SelectedCategory) ? "All categories" : preferences.SelectedCategory;
            _pendingFeedSelection = string.IsNullOrWhiteSpace(preferences.SelectedFeed) ? "All feeds" : preferences.SelectedFeed;
            _pendingLabelSelection = string.IsNullOrWhiteSpace(preferences.SelectedLabel) ? "All labels" : preferences.SelectedLabel;
            if (SavedViewComboBox != null)
            {
                SavedViewComboBox.Text = preserveSavedViewName
                    ? NormalizeSavedViewName(preferences.SelectedSavedViewName)
                    : string.Empty;
            }

            SetSelectionByValue(GroupByComboBox, preferences.GroupingMode, "feed");
            SetSelectionByValue(SortFieldComboBox, preferences.SortField, "archived");
            SetSelectionByDirection(SortDirectionComboBox, preferences.SortDirection, "desc");
            SetCleanupSelection(_viewModel?.GetArchiveAutoCleanupDays() ?? 90);
            AutoCleanupEnabledCheckBox.IsChecked = _viewModel?.GetArchiveAutoCleanupEnabled() == true;
            if (LabelFilterComboBox != null)
            {
                LabelFilterComboBox.SelectedItem = LabelFilterComboBox.Items
                    .OfType<string>()
                    .FirstOrDefault(item => string.Equals(item, _pendingLabelSelection, StringComparison.OrdinalIgnoreCase))
                    ?? "All labels";
            }
            if (CategoryFilterComboBox != null)
            {
                CategoryFilterComboBox.SelectedItem = CategoryFilterComboBox.Items
                    .OfType<string>()
                    .FirstOrDefault(item => string.Equals(item, _pendingCategorySelection, StringComparison.OrdinalIgnoreCase))
                    ?? CategoryFilterComboBox.SelectedItem;
            }
            if (FeedFilterComboBox != null)
            {
                FeedFilterComboBox.SelectedItem = FeedFilterComboBox.Items
                    .OfType<string>()
                    .FirstOrDefault(item => string.Equals(item, _pendingFeedSelection, StringComparison.OrdinalIgnoreCase))
                    ?? FeedFilterComboBox.SelectedItem;
            }
            ReadStateComboBox.SelectedItem = ReadStateComboBox.Items
                .OfType<string>()
                .FirstOrDefault(item => string.Equals(item, preferences.ReadState, StringComparison.OrdinalIgnoreCase))
                ?? "All";
            ApplyColumnVisibilityPreferences(preferences);
            ApplyColumnOrderPreferences(preferences);
            SetConfigurationPanelVisibility(preferences.ShowConfigurationPanel);
            SetPreviewVisibility(preferences.ShowPreviewPanel);
            _isUpdatingControls = false;

            if (applyWindowPlacement)
            {
                ApplyWindowPlacement(preferences);
            }
        }

        private void ApplyWindowPlacement(ArchiveViewPreferences preferences)
        {
            preferences ??= new ArchiveViewPreferences();
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

        private static WindowState NormalizePreferredWindowState(string value)
        {
            return string.Equals(value, "normal", StringComparison.OrdinalIgnoreCase)
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private string NormalizeSavedViewName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var normalizedName = name.Trim();
            var existing = _viewModel?.GetArchiveSavedViews()
                .FirstOrDefault(view => string.Equals(view.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
            return existing?.Name ?? normalizedName;
        }

        private void RefreshSavedViewOptions()
        {
            if (SavedViewComboBox == null)
            {
                return;
            }

            var currentText = SavedViewComboBox.Text;
            _isUpdatingControls = true;
            SavedViewComboBox.ItemsSource = (_viewModel?.GetArchiveSavedViews() ?? Array.Empty<ArchiveSavedView>())
                .Select(view => view.Name)
                .ToList();
            SavedViewComboBox.Text = NormalizeSavedViewName(currentText);
            _isUpdatingControls = false;
        }

        private void SetCleanupSelection(int days)
        {
            if (CleanupDaysComboBox == null)
            {
                return;
            }

            var match = CleanupDaysComboBox.Items
                .OfType<CleanupOption>()
                .FirstOrDefault(option => option.Days == days);
            CleanupDaysComboBox.SelectedItem = match ?? CleanupDaysComboBox.Items.OfType<CleanupOption>().FirstOrDefault();
        }

        private static void SetSelectionByValue(ComboBox comboBox, string value, string fallback)
        {
            if (comboBox == null)
            {
                return;
            }

            var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            var match = comboBox.Items
                .OfType<object>()
                .FirstOrDefault(item => string.Equals(GetOptionKey(item), normalized, StringComparison.OrdinalIgnoreCase));
            comboBox.SelectedItem = match ?? comboBox.Items
                .OfType<object>()
                .FirstOrDefault(item => string.Equals(GetOptionKey(item), fallback, StringComparison.OrdinalIgnoreCase));
        }

        private static void SetSelectionByDirection(ComboBox comboBox, string direction, string fallback)
        {
            if (comboBox == null)
            {
                return;
            }

            var normalized = string.Equals(direction, "asc", StringComparison.OrdinalIgnoreCase)
                ? ListSortDirection.Ascending
                : ListSortDirection.Descending;
            var fallbackDirection = string.Equals(fallback, "asc", StringComparison.OrdinalIgnoreCase)
                ? ListSortDirection.Ascending
                : ListSortDirection.Descending;
            var match = comboBox.Items
                .OfType<SortDirectionOption>()
                .FirstOrDefault(item => item.Direction == normalized);
            comboBox.SelectedItem = match ?? comboBox.Items
                .OfType<SortDirectionOption>()
                .FirstOrDefault(item => item.Direction == fallbackDirection);
        }

        private static string GetOptionKey(object option)
        {
            return option switch
            {
                GroupOption group => group.Key,
                SortOption sort => sort.Key,
                _ => string.Empty
            };
        }

        private void PreferencesSaveTimer_Tick(object sender, EventArgs e)
        {
            _preferencesSaveTimer.Stop();
            SavePreferencesNow();
        }

        private void NoteSaveTimer_Tick(object sender, EventArgs e)
        {
            _noteSaveTimer.Stop();
            SaveSelectedNote();
        }

        private void ApplyColumnVisibilityPreferences(ArchiveViewPreferences preferences)
        {
            _archivedColumnRequestedVisible = preferences?.ShowArchivedColumn != false;
            SetColumnVisibility(ArchivedColumn, _archivedColumnRequestedVisible);
            SetColumnVisibility(PublishedColumn, preferences?.ShowPublishedColumn != false);
            SetColumnVisibility(CategoryColumn, preferences?.ShowCategoryColumn != false);
            SetColumnVisibility(FeedColumn, preferences?.ShowFeedColumn != false);
            SetColumnVisibility(LabelsColumn, preferences?.ShowLabelsColumn != false);
            SetColumnVisibility(TitleColumn, true);
            ApplyModeSpecificColumnVisibility();
            UpdateColumnVisibilityMenuChecks();
        }

        private void ApplyColumnOrderPreferences(ArchiveViewPreferences preferences)
        {
            var orderedColumns = new List<(DataGridColumn Column, int Index)>
            {
                (ArchivedColumn, preferences?.ArchivedColumnIndex ?? 1),
                (PublishedColumn, preferences?.PublishedColumnIndex ?? 2),
                (CategoryColumn, preferences?.CategoryColumnIndex ?? 3),
                (FeedColumn, preferences?.FeedColumnIndex ?? 4),
                (LabelsColumn, preferences?.LabelsColumnIndex ?? 5),
                (TitleColumn, preferences?.TitleColumnIndex ?? 6)
            }
            .Where(entry => entry.Column != null)
            .OrderBy(entry => entry.Index)
            .ThenBy(entry => entry.Column.DisplayIndex)
            .ToList();

            if (SelectColumn != null)
            {
                SelectColumn.DisplayIndex = 0;
            }

            if (ReadColumn != null)
            {
                ReadColumn.DisplayIndex = 1;
            }

            for (var i = 0; i < orderedColumns.Count; i++)
            {
                orderedColumns[i].Column.DisplayIndex = i + 2;
            }
        }

        private void SetColumnVisibility(DataGridColumn column, bool isVisible)
        {
            if (column != null)
            {
                column.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private static bool IsColumnVisible(DataGridColumn column)
        {
            return column?.Visibility != Visibility.Collapsed;
        }

        private void UpdateColumnVisibilityMenuChecks()
        {
            if (ArchivedColumnCheckBox != null)
            {
                ArchivedColumnCheckBox.IsChecked = _archivedColumnRequestedVisible;
                ArchivedColumnCheckBox.Visibility = _currentMode == LibrarySectionMode.Archive ? Visibility.Visible : Visibility.Collapsed;
            }

            if (PublishedColumnCheckBox != null)
            {
                PublishedColumnCheckBox.IsChecked = IsColumnVisible(PublishedColumn);
            }

            if (CategoryColumnCheckBox != null)
            {
                CategoryColumnCheckBox.IsChecked = IsColumnVisible(CategoryColumn);
            }

            if (FeedColumnCheckBox != null)
            {
                FeedColumnCheckBox.IsChecked = IsColumnVisible(FeedColumn);
            }

            if (LabelsColumnCheckBox != null)
            {
                LabelsColumnCheckBox.IsChecked = IsColumnVisible(LabelsColumn);
            }

            if (TitleColumnCheckBox != null)
            {
                TitleColumnCheckBox.IsChecked = IsColumnVisible(TitleColumn);
            }
        }

        private void ApplyPendingColumnVisibility()
        {
            _archivedColumnRequestedVisible = ArchivedColumnCheckBox?.IsChecked == true;
            SetColumnVisibility(ArchivedColumn, _currentMode == LibrarySectionMode.Archive && _archivedColumnRequestedVisible);
            SetColumnVisibility(PublishedColumn, PublishedColumnCheckBox?.IsChecked == true);
            SetColumnVisibility(CategoryColumn, CategoryColumnCheckBox?.IsChecked == true);
            SetColumnVisibility(FeedColumn, FeedColumnCheckBox?.IsChecked == true);
            SetColumnVisibility(LabelsColumn, LabelsColumnCheckBox?.IsChecked == true);
            SetColumnVisibility(TitleColumn, TitleColumnCheckBox?.IsChecked != false);
        }

        private void ApplyModeSpecificColumnVisibility()
        {
            if (_currentMode != LibrarySectionMode.Archive)
            {
                SetColumnVisibility(ArchivedColumn, false);
                return;
            }

            SetColumnVisibility(ArchivedColumn, _archivedColumnRequestedVisible);
        }

        private void SetPreviewVisibility(bool isVisible)
        {
            _isPreviewVisible = isVisible;

            if (PreviewGroupBox != null)
            {
                PreviewGroupBox.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            }

            if (PreviewGridSplitter != null)
            {
                PreviewGridSplitter.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            }

            if (PreviewColumnDefinition != null)
            {
                PreviewColumnDefinition.Width = isVisible
                    ? new GridLength(380)
                    : new GridLength(0);
            }

            if (PreviewSplitterColumnDefinition != null)
            {
                PreviewSplitterColumnDefinition.Width = isVisible
                    ? new GridLength(10)
                    : new GridLength(0);
            }

            if (TogglePreviewButton != null)
            {
                TogglePreviewButton.Content = isVisible ? "Hide Preview" : "Show Preview";
            }
        }

        private void SetConfigurationPanelVisibility(bool isVisible)
        {
            _isConfigurationPanelVisible = isVisible;

            if (ConfigurationPanelBorder != null)
            {
                ConfigurationPanelBorder.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            }

            if (ToggleConfigurationPanelButton != null)
            {
                ToggleConfigurationPanelButton.Content = isVisible ? "Hide Controls" : "Show Controls";
            }
        }

        private void SaveSelectedNote()
        {
            if (_isUpdatingNoteEditor || _viewModel == null || SelectedNoteTextBox == null || _selectedArchiveEntry?.Item == null)
            {
                return;
            }

            var note = SelectedNoteTextBox.Text?.Trim() ?? string.Empty;
            var existing = _viewModel.GetArticleNote(_selectedArchiveEntry.Item)?.Trim() ?? string.Empty;
            if (string.Equals(note, existing, StringComparison.Ordinal))
            {
                return;
            }

            _isUpdatingNoteEditor = true;
            try
            {
                _viewModel.SetArticleNote(_selectedArchiveEntry.Item, note);
            }
            finally
            {
                _isUpdatingNoteEditor = false;
            }
        }

        private void SchedulePreferencesSave()
        {
            if (_isUpdatingControls || _isApplyingWindowPlacement)
            {
                return;
            }

            _preferencesSaveTimer.Stop();
            _preferencesSaveTimer.Start();
        }

        private void SavePreferencesNow()
        {
            if (_viewModel == null)
            {
                return;
            }

            _viewModel.SaveArchiveViewPreferences(CaptureCurrentPreferences());
        }

        private void RebuildArchiveEntries()
        {
            try
            {
                WriteArchiveDiagnostic("RebuildArchiveEntries start.");
                var selectedLinks = ArchiveDataGrid.SelectedItems
                    .OfType<ArchiveEntryViewModel>()
                    .Select(entry => entry.Link?.Trim())
                    .Where(link => !string.IsNullOrWhiteSpace(link))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var currentCategory = _pendingCategorySelection;
                var currentFeed = _pendingFeedSelection;
                var currentLabel = _pendingLabelSelection;

                ClearArchiveEntries();

                foreach (var entry in FlattenArchiveEntries(GetCurrentSection()))
                {
                    entry.PropertyChanged += ArchiveEntryOnPropertyChanged;
                    _archiveEntries.Add(entry);
                }

                RefreshFilterOptions(currentCategory, currentFeed, currentLabel);
                RefreshArchiveView();
                RestoreSelection(selectedLinks);
                UpdatePreviewState();
                WriteArchiveDiagnostic($"RebuildArchiveEntries completed. Entries={_archiveEntries.Count}.");
            }
            catch (Exception ex)
            {
                WriteArchiveDiagnostic("RebuildArchiveEntries failed.", ex);
                throw;
            }
        }

        private void ClearArchiveEntries()
        {
            foreach (var entry in _archiveEntries)
            {
                entry.PropertyChanged -= ArchiveEntryOnPropertyChanged;
                entry.Dispose();
            }

            _archiveEntries.Clear();
        }

        private static IEnumerable<ArchiveEntryViewModel> FlattenArchiveEntries(ArticleSectionViewModel section)
        {
            if (section?.Items == null)
            {
                yield break;
            }

            var seenLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var child in section.Items)
            {
                if (child is CategoryGroupViewModel category)
                {
                    foreach (var feed in category.Feeds ?? Enumerable.Empty<FeedGroupViewModel>())
                    {
                        foreach (var item in feed.Items ?? Enumerable.Empty<Models.FeedItem>())
                        {
                            var key = item?.Link?.Trim();
                            if (item == null || string.IsNullOrWhiteSpace(key) || !seenLinks.Add(key))
                            {
                                continue;
                            }

                            yield return new ArchiveEntryViewModel(item, category.Name);
                        }
                    }
                }
                else if (child is FeedGroupViewModel feedGroup)
                {
                    var categoryName = string.IsNullOrWhiteSpace(feedGroup.Category) ? "Default" : feedGroup.Category;
                    foreach (var item in feedGroup.Items ?? Enumerable.Empty<Models.FeedItem>())
                    {
                        var key = item?.Link?.Trim();
                        if (item == null || string.IsNullOrWhiteSpace(key) || !seenLinks.Add(key))
                        {
                            continue;
                        }

                        yield return new ArchiveEntryViewModel(item, categoryName);
                    }
                }
            }
        }

        private void RefreshFilterOptions(string selectedCategory, string selectedFeed, string selectedLabel)
        {
            _isUpdatingControls = true;

            var categories = new List<string> { "All categories" };
            categories.AddRange(_archiveEntries
                .Select(entry => entry.Category)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));

            var feeds = new List<string> { "All feeds" };
            feeds.AddRange(_archiveEntries
                .Select(entry => entry.FeedName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));

            var labels = new List<string> { "All labels", "Has labels" };
            labels.AddRange((_viewModel?.GetArticleLabels() ?? Array.Empty<ArticleLabelDefinition>())
                .Select(label => label.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));

            CategoryFilterComboBox.ItemsSource = categories;
            FeedFilterComboBox.ItemsSource = feeds;
            LabelFilterComboBox.ItemsSource = labels;
            CategoryFilterComboBox.SelectedItem = categories.Any(name => string.Equals(name, selectedCategory, StringComparison.OrdinalIgnoreCase))
                ? categories.First(name => string.Equals(name, selectedCategory, StringComparison.OrdinalIgnoreCase))
                : categories.FirstOrDefault();
            FeedFilterComboBox.SelectedItem = feeds.Any(name => string.Equals(name, selectedFeed, StringComparison.OrdinalIgnoreCase))
                ? feeds.First(name => string.Equals(name, selectedFeed, StringComparison.OrdinalIgnoreCase))
                : feeds.FirstOrDefault();
            LabelFilterComboBox.SelectedItem = labels.Any(name => string.Equals(name, selectedLabel, StringComparison.OrdinalIgnoreCase))
                ? labels.First(name => string.Equals(name, selectedLabel, StringComparison.OrdinalIgnoreCase))
                : labels.FirstOrDefault();
            _pendingCategorySelection = CategoryFilterComboBox.SelectedItem as string ?? "All categories";
            _pendingFeedSelection = FeedFilterComboBox.SelectedItem as string ?? "All feeds";
            _pendingLabelSelection = LabelFilterComboBox.SelectedItem as string ?? "All labels";

            _isUpdatingControls = false;
        }

        private void RefreshLabelOptions(string selectedLabel)
        {
            RefreshFilterOptions(
                CategoryFilterComboBox?.SelectedItem as string ?? _pendingCategorySelection,
                FeedFilterComboBox?.SelectedItem as string ?? _pendingFeedSelection,
                selectedLabel);
        }

        private void RefreshArchiveView()
        {
            ApplyGroupingAndSort();
            _archiveEntriesView?.Refresh();
            UpdateCounts();
            UpdateSelectionState();
            UpdateEmptyState();
            UpdatePreviewState();
            UpdateQuickFilterState();
            SchedulePreferencesSave();
        }

        private void UpdateQuickFilterState()
        {
            var today = DateTime.Today;
            var currentReadState = ReadStateComboBox?.SelectedItem as string ?? "All";
            var currentLabelState = LabelFilterComboBox?.SelectedItem as string ?? "All labels";
            var fromDate = ArchivedFromDatePicker?.SelectedDate?.Date;
            var toDate = ArchivedToDatePicker?.SelectedDate?.Date;

            IsQuickUnreadActive = string.Equals(currentReadState, "Unread", StringComparison.OrdinalIgnoreCase);
            IsQuickHasLabelsActive = string.Equals(currentLabelState, "Has labels", StringComparison.OrdinalIgnoreCase);
            IsQuickLast7DaysActive = fromDate == today.AddDays(-6) && toDate == today;
            IsQuickLast30DaysActive = fromDate == today.AddDays(-29) && toDate == today;
        }

        private void ApplyGroupingAndSort()
        {
            if (_archiveEntriesView == null ||
                GroupByComboBox == null ||
                SortFieldComboBox == null ||
                SortDirectionComboBox == null)
            {
                return;
            }

            _archiveEntriesView.GroupDescriptions.Clear();
            _archiveEntriesView.SortDescriptions.Clear();

            var selectedGroup = GroupByComboBox.SelectedItem as GroupOption;
            var selectedSort = SortFieldComboBox.SelectedItem as SortOption;
            var selectedDirection = SortDirectionComboBox.SelectedItem as SortDirectionOption;
            var direction = selectedDirection?.Direction ?? ListSortDirection.Descending;
            var propertyName = selectedSort?.Key switch
            {
                "published" => nameof(ArchiveEntryViewModel.PublicationDate),
                "category" => nameof(ArchiveEntryViewModel.Category),
                "feed" => nameof(ArchiveEntryViewModel.FeedName),
                "labels" => nameof(ArchiveEntryViewModel.LabelsText),
                "title" => nameof(ArchiveEntryViewModel.Title),
                _ => nameof(ArchiveEntryViewModel.ArchivedAt)
            };
            var groupPropertyName = selectedGroup?.Key switch
            {
                "category" => nameof(ArchiveEntryViewModel.Category),
                "day" => nameof(ArchiveEntryViewModel.ArchiveDayGroup),
                "month" => nameof(ArchiveEntryViewModel.ArchiveMonthGroup),
                "feed" => nameof(ArchiveEntryViewModel.FeedName),
                _ => null
            };
            if (!string.IsNullOrWhiteSpace(groupPropertyName))
            {
                _archiveEntriesView.GroupDescriptions.Add(new PropertyGroupDescription(groupPropertyName));
                var groupDirection = string.Equals(propertyName, groupPropertyName, StringComparison.Ordinal)
                    ? direction
                    : string.Equals(selectedGroup?.Key, "month", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(selectedGroup?.Key, "day", StringComparison.OrdinalIgnoreCase)
                        ? ListSortDirection.Descending
                        : ListSortDirection.Ascending;
                _archiveEntriesView.SortDescriptions.Add(new SortDescription(groupPropertyName, groupDirection));
            }

            if (!string.Equals(propertyName, groupPropertyName, StringComparison.Ordinal))
            {
                _archiveEntriesView.SortDescriptions.Add(new SortDescription(propertyName, direction));
            }

            if (!string.Equals(propertyName, nameof(ArchiveEntryViewModel.ArchivedAt), StringComparison.Ordinal))
            {
                _archiveEntriesView.SortDescriptions.Add(new SortDescription(nameof(ArchiveEntryViewModel.ArchivedAt), ListSortDirection.Descending));
            }

            UpdateDataGridSortIndicators(selectedSort?.Key, direction);
        }

        private void UpdateDataGridSortIndicators(string sortKey, ListSortDirection direction)
        {
            if (ArchiveDataGrid == null)
            {
                return;
            }

            foreach (var column in ArchiveDataGrid.Columns)
            {
                column.SortDirection = null;
            }

            var activeColumn = GetColumnForSortKey(sortKey);
            if (activeColumn != null)
            {
                activeColumn.SortDirection = direction;
            }
        }

        private DataGridColumn GetColumnForSortKey(string sortKey)
        {
            return sortKey?.ToLowerInvariant() switch
            {
                "archived" => ArchivedColumn,
                "published" => PublishedColumn,
                "category" => CategoryColumn,
                "feed" => FeedColumn,
                "labels" => LabelsColumn,
                "title" => TitleColumn,
                _ => null
            };
        }

        private string GetSortKeyForColumn(DataGridColumn column)
        {
            if (ReferenceEquals(column, ArchivedColumn))
            {
                return "archived";
            }

            if (ReferenceEquals(column, PublishedColumn))
            {
                return "published";
            }

            if (ReferenceEquals(column, CategoryColumn))
            {
                return "category";
            }

            if (ReferenceEquals(column, FeedColumn))
            {
                return "feed";
            }

            if (ReferenceEquals(column, LabelsColumn))
            {
                return "labels";
            }

            if (ReferenceEquals(column, TitleColumn))
            {
                return "title";
            }

            return null;
        }

        private bool ArchiveEntryMatchesCurrentFilter(object obj)
        {
            if (obj is not ArchiveEntryViewModel entry)
            {
                return false;
            }

            if (SearchTextBox == null ||
                CategoryFilterComboBox == null ||
                FeedFilterComboBox == null ||
                LabelFilterComboBox == null ||
                ReadStateComboBox == null ||
                ArchivedFromDatePicker == null ||
                ArchivedToDatePicker == null)
            {
                return true;
            }

            var search = SearchTextBox.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var comparison = StringComparison.OrdinalIgnoreCase;
                var matchesSearch =
                    (entry.Title?.IndexOf(search, comparison) ?? -1) >= 0 ||
                    (entry.Description?.IndexOf(search, comparison) ?? -1) >= 0 ||
                    (entry.Note?.IndexOf(search, comparison) ?? -1) >= 0 ||
                    (entry.FeedName?.IndexOf(search, comparison) ?? -1) >= 0 ||
                    (entry.Category?.IndexOf(search, comparison) ?? -1) >= 0 ||
                    (entry.LabelsText?.IndexOf(search, comparison) ?? -1) >= 0 ||
                    (entry.Link?.IndexOf(search, comparison) ?? -1) >= 0;

                if (!matchesSearch)
                {
                    return false;
                }
            }

            var selectedCategory = CategoryFilterComboBox.SelectedItem as string;
            if (!string.IsNullOrWhiteSpace(selectedCategory) &&
                !string.Equals(selectedCategory, "All categories", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(entry.Category, selectedCategory, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var selectedFeed = FeedFilterComboBox.SelectedItem as string;
            if (!string.IsNullOrWhiteSpace(selectedFeed) &&
                !string.Equals(selectedFeed, "All feeds", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(entry.FeedName, selectedFeed, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var selectedLabel = LabelFilterComboBox.SelectedItem as string;
            if (!string.IsNullOrWhiteSpace(selectedLabel) &&
                !string.Equals(selectedLabel, "All labels", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(selectedLabel, "Has labels", StringComparison.OrdinalIgnoreCase))
                {
                    if (!(entry.Labels?.Any() ?? false))
                    {
                        return false;
                    }
                }
                else if (!(entry.Labels?.Any(label => string.Equals(label.Name, selectedLabel, StringComparison.OrdinalIgnoreCase)) ?? false))
                {
                    return false;
                }
            }

            var readState = ReadStateComboBox.SelectedItem as string;
            if (string.Equals(readState, "Unread", StringComparison.OrdinalIgnoreCase) && !entry.IsUnread)
            {
                return false;
            }

            if (string.Equals(readState, "Read", StringComparison.OrdinalIgnoreCase) && !entry.IsRead)
            {
                return false;
            }

            var fromDate = ArchivedFromDatePicker.SelectedDate?.Date;
            if (fromDate.HasValue)
            {
                var archivedDate = entry.ArchivedAt?.Date;
                if (!archivedDate.HasValue || archivedDate.Value < fromDate.Value)
                {
                    return false;
                }
            }

            var toDate = ArchivedToDatePicker.SelectedDate?.Date;
            if (toDate.HasValue)
            {
                var archivedDate = entry.ArchivedAt?.Date;
                if (!archivedDate.HasValue || archivedDate.Value > toDate.Value)
                {
                    return false;
                }
            }

            return true;
        }

        private void ArchiveEntryOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ArchiveEntryViewModel.IsRead) ||
                e.PropertyName == nameof(ArchiveEntryViewModel.IsUnread))
            {
                var readState = ReadStateComboBox?.SelectedItem as string ?? "All";
                if (!string.Equals(readState, "All", StringComparison.OrdinalIgnoreCase))
                {
                    RefreshArchiveView();
                    return;
                }

                UpdateCounts();
                UpdateSelectionState();
                UpdatePreviewState();
                CommandManager.InvalidateRequerySuggested();
                return;
            }

            if (e.PropertyName == nameof(ArchiveEntryViewModel.ArchivedAt) ||
                e.PropertyName == nameof(ArchiveEntryViewModel.Title) ||
                e.PropertyName == nameof(ArchiveEntryViewModel.FeedName) ||
                e.PropertyName == nameof(ArchiveEntryViewModel.Labels) ||
                e.PropertyName == nameof(ArchiveEntryViewModel.LabelsText) ||
                e.PropertyName == nameof(ArchiveEntryViewModel.Note))
            {
                RefreshArchiveView();
            }
        }

        private void RestoreSelection(IEnumerable<string> selectedLinks)
        {
            if (selectedLinks == null)
            {
                UpdateSelectionState();
                UpdatePreviewState();
                return;
            }

            var links = new HashSet<string>(selectedLinks.Where(link => !string.IsNullOrWhiteSpace(link)), StringComparer.OrdinalIgnoreCase);
            ArchiveDataGrid.SelectedItems.Clear();
            foreach (var entry in _archiveEntries.Where(entry => links.Contains(entry.Link?.Trim() ?? string.Empty)))
            {
                ArchiveDataGrid.SelectedItems.Add(entry);
            }

            UpdateSelectionState();
            UpdatePreviewState();
        }

        private void UpdateCounts()
        {
            if (ArchivedCountTextBlock != null)
            {
                ArchivedCountTextBlock.Text = _archiveEntries.Count.ToString();
            }

            if (VisibleCountTextBlock != null)
            {
                VisibleCountTextBlock.Text = _archiveEntriesView?.Cast<object>().Count().ToString() ?? "0";
            }

            if (SelectedCountTextBlock != null)
            {
                SelectedCountTextBlock.Text = ArchiveDataGrid?.SelectedItems?.Count.ToString() ?? "0";
            }
        }

        private void UpdateSelectionState()
        {
            var selectionCount = ArchiveDataGrid?.SelectedItems?.Count ?? 0;
            var hasSelection = selectionCount > 0;
            var hasSingleSelection = selectionCount == 1;
            var hasVisibleItems = _archiveEntriesView != null && _archiveEntriesView.Cast<object>().Any();
            var hasAnyItems = _archiveEntries.Count > 0;

            if (MoreClearSelectionMenuItem != null)
            {
                MoreClearSelectionMenuItem.IsEnabled = hasSelection;
            }

            if (MoreSelectAllMenuItem != null)
            {
                MoreSelectAllMenuItem.IsEnabled = hasVisibleItems;
            }

            if (MoreUnarchiveMenuItem != null)
            {
                MoreUnarchiveMenuItem.IsEnabled = hasAnyItems;
                MoreUnarchiveMenuItem.Header = GetPrimaryActionHeader();
            }

            if (MoreExportCsvMenuItem != null)
            {
                MoreExportCsvMenuItem.IsEnabled = hasVisibleItems;
            }

            if (MoreExportJsonMenuItem != null)
            {
                MoreExportJsonMenuItem.IsEnabled = hasVisibleItems;
            }

            if (ApplyCleanupButton != null)
            {
                ApplyCleanupButton.IsEnabled = _currentMode == LibrarySectionMode.Archive && hasAnyItems;
            }

            if (PreviewOpenLinkButton != null)
            {
                PreviewOpenLinkButton.IsEnabled = hasSingleSelection;
            }

            if (PreviewCopyLinkButton != null)
            {
                PreviewCopyLinkButton.IsEnabled = hasSingleSelection;
            }

            UpdatePreviewActionButtons();
        }

        private void SyncEntrySelectionFlagsFromGrid()
        {
            if (_isSynchronizingSelection || ArchiveDataGrid == null)
            {
                return;
            }

            _isSynchronizingSelection = true;
            try
            {
                var selectedEntries = new HashSet<ArchiveEntryViewModel>(
                    ArchiveDataGrid.SelectedItems.OfType<ArchiveEntryViewModel>());

                foreach (var entry in _archiveEntries)
                {
                    var shouldBeSelected = selectedEntries.Contains(entry);
                    if (entry.IsSelected != shouldBeSelected)
                    {
                        entry.IsSelected = shouldBeSelected;
                    }
                }
            }
            finally
            {
                _isSynchronizingSelection = false;
            }
        }

        private void SelectionCheckBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is CheckBox checkBox)
            {
                checkBox.IsChecked = !(checkBox.IsChecked ?? false);
                e.Handled = true;
            }
        }

        private void SelectionCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isSynchronizingSelection ||
                ArchiveDataGrid == null ||
                sender is not FrameworkElement element ||
                element.DataContext is not ArchiveEntryViewModel entry)
            {
                return;
            }

            _isSynchronizingSelection = true;
            try
            {
                if (entry.IsSelected)
                {
                    if (!ArchiveDataGrid.SelectedItems.Contains(entry))
                    {
                        ArchiveDataGrid.SelectedItems.Add(entry);
                    }
                }
                else if (ArchiveDataGrid.SelectedItems.Contains(entry))
                {
                    ArchiveDataGrid.SelectedItems.Remove(entry);
                }
            }
            finally
            {
                _isSynchronizingSelection = false;
            }

            UpdateCounts();
            UpdateSelectionState();
            UpdatePreviewState();
            CommandManager.InvalidateRequerySuggested();
        }

        private void UpdateEmptyState()
        {
            if (EmptyStateBorder == null)
            {
                return;
            }

            EmptyStateBorder.Visibility = (_archiveEntriesView == null || _archiveEntriesView.IsEmpty)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void UpdatePreviewState()
        {
            var selectedEntries = ArchiveDataGrid?.SelectedItems?.OfType<ArchiveEntryViewModel>().ToList()
                ?? new List<ArchiveEntryViewModel>();
            SelectedArchiveEntry = selectedEntries.Count == 1 ? selectedEntries[0] : null;

            if (PreviewContentGrid == null || PreviewEmptyStateBorder == null || SelectionSummaryGrid == null)
            {
                return;
            }

            var hasSingleSelection = SelectedArchiveEntry != null;
            var hasMultiSelection = selectedEntries.Count > 1;
            PreviewContentGrid.Visibility = hasSingleSelection ? Visibility.Visible : Visibility.Collapsed;
            SelectionSummaryGrid.Visibility = hasMultiSelection ? Visibility.Visible : Visibility.Collapsed;
            PreviewEmptyStateBorder.Visibility = !hasSingleSelection && !hasMultiSelection
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (PreviewBrowserContainer != null)
            {
                PreviewBrowserContainer.Visibility = hasSingleSelection && (_showOriginalContent || _isReaderModeActive)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (PreviewDetailsScrollViewer != null)
            {
                PreviewDetailsScrollViewer.Visibility = hasSingleSelection && (_showOriginalContent || _isReaderModeActive)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }

            if (PreviewOpenArticleButton != null)
            {
                PreviewOpenArticleButton.IsEnabled = hasSingleSelection;
            }

            if (PreviewPrimaryMetaLabelTextBlock != null)
            {
                PreviewPrimaryMetaLabelTextBlock.Text = _currentMode == LibrarySectionMode.Archive ? "Archived" : "Status";
            }

            if (PreviewPrimaryMetaValueTextBlock != null)
            {
                PreviewPrimaryMetaValueTextBlock.Text = hasSingleSelection
                    ? (_currentMode == LibrarySectionMode.Archive
                        ? (SelectedArchiveEntry.ArchivedAt?.ToString("yyyy-MM-dd HH:mm") ?? "-")
                        : GetCurrentModeLabel())
                    : string.Empty;
            }

            if (hasMultiSelection)
            {
                UpdateSelectionSummary(selectedEntries);
            }

            UpdatePreviewActionButtons();
        }

        private void UpdateSelectionSummary(IReadOnlyCollection<ArchiveEntryViewModel> selectedEntries)
        {
            if (selectedEntries == null || selectedEntries.Count == 0)
            {
                return;
            }

            var entries = selectedEntries
                .Where(entry => entry != null)
                .ToList();
            var entryCount = entries.Count;
            var feedNames = entries
                .Select(entry => entry.FeedName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var categories = entries
                .Select(entry => entry.Category)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var labels = entries
                .SelectMany(entry => entry.Labels ?? Array.Empty<ArticleLabelDefinition>())
                .Select(label => label.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var archivedDates = entries
                .Select(entry => entry.ArchivedAt)
                .Where(date => date.HasValue)
                .Select(date => date.Value)
                .OrderBy(date => date)
                .ToList();

            if (SelectionSummaryTitleTextBlock != null)
            {
                var noun = _currentMode switch
                {
                    LibrarySectionMode.Pinned => "pinned article",
                    LibrarySectionMode.ReadLater => "saved article",
                    _ => "archived article"
                };
                SelectionSummaryTitleTextBlock.Text = $"{entryCount} {noun}{(entryCount == 1 ? string.Empty : "s")} selected";
            }

            if (SelectionSummarySubtitleTextBlock != null)
            {
                SelectionSummarySubtitleTextBlock.Text = $"{feedNames.Count} feed{(feedNames.Count == 1 ? string.Empty : "s")}  •  {categories.Count} categor{(categories.Count == 1 ? "y" : "ies")}";
            }

            if (SelectionSummaryRangeTextBlock != null)
            {
                SelectionSummaryRangeTextBlock.Text = archivedDates.Count == 0
                    ? "No archived date available"
                    : archivedDates.First() == archivedDates.Last()
                        ? archivedDates.First().ToString("yyyy-MM-dd HH:mm")
                        : $"{archivedDates.First():yyyy-MM-dd HH:mm} to {archivedDates.Last():yyyy-MM-dd HH:mm}";
            }

            if (SelectionSummaryFeedsCountTextBlock != null)
            {
                SelectionSummaryFeedsCountTextBlock.Text = feedNames.Count == 0
                    ? "Unknown feeds"
                    : $"{feedNames.Count} distinct feed{(feedNames.Count == 1 ? string.Empty : "s")}";
            }

            if (SelectionSummaryCategoriesTextBlock != null)
            {
                SelectionSummaryCategoriesTextBlock.Text = categories.Count == 0
                    ? "No category data available"
                    : string.Join(", ", categories);
            }

            if (SelectionSummaryFeedsTextBlock != null)
            {
                SelectionSummaryFeedsTextBlock.Text = feedNames.Count == 0
                    ? "No feed data available"
                    : string.Join(", ", feedNames);
            }

            if (SelectionSummaryLabelsTextBlock != null)
            {
                SelectionSummaryLabelsTextBlock.Text = labels.Count == 0
                    ? "No labels assigned in this selection"
                    : string.Join(", ", labels);
            }
        }

        private void UpdatePreviewActionButtons()
        {
            var hasSingleSelection = SelectedArchiveEntry?.Item != null;
            var hasLink = hasSingleSelection && !string.IsNullOrWhiteSpace(SelectedArchiveEntry.Item.Link);
            var canNavigate = hasLink && !_isReaderModeLoading && !_isPreviewPageLoading;
            var hasBrowserPreview = hasSingleSelection && (_showOriginalContent || _isReaderModeActive);
            var canEnableVideo = hasLink && _showOriginalContent && !_isReaderModeActive && !_isPreviewPageLoading;

            if (PreviewShowContentButton != null)
            {
                PreviewShowContentButton.IsEnabled = canNavigate;
                PreviewShowContentButton.Content = _showOriginalContent && !_isReaderModeActive
                    ? "Article Summary"
                    : "Show Content";
            }

            if (PreviewReaderModeButton != null)
            {
                PreviewReaderModeButton.IsEnabled = hasLink && !_isPreviewPageLoading;
                PreviewReaderModeButton.Content = _isReaderModeLoading
                    ? "Loading Reader..."
                    : (_isReaderModeActive ? "Article Summary" : "Reader Mode");
            }

            if (PreviewEnableVideoButton != null)
            {
                PreviewEnableVideoButton.Visibility = canEnableVideo ? Visibility.Visible : Visibility.Collapsed;
                PreviewEnableVideoButton.IsEnabled = canEnableVideo && !_isPreviewVideoPlaybackEnabled;
                PreviewEnableVideoButton.Content = _isPreviewVideoPlaybackEnabled ? "Media Allowed" : "Allow Media";
            }

            if (PreviewFullscreenOverlayButton != null)
            {
                PreviewFullscreenOverlayButton.IsEnabled = true;
            }

            if (PreviewFullscreenToolbar != null)
            {
                PreviewFullscreenToolbar.Visibility = _isPreviewFullscreen && hasBrowserPreview ? Visibility.Visible : Visibility.Collapsed;
            }

            if (PreviewFullscreenRefreshButton != null)
            {
                PreviewFullscreenRefreshButton.IsEnabled = hasLink && !_isPreviewPageLoading;
            }

            if (PreviewFullscreenEnableVideoButton != null)
            {
                PreviewFullscreenEnableVideoButton.Visibility = canEnableVideo ? Visibility.Visible : Visibility.Collapsed;
                PreviewFullscreenEnableVideoButton.IsEnabled = canEnableVideo && !_isPreviewVideoPlaybackEnabled;
                PreviewFullscreenEnableVideoButton.Content = _isPreviewVideoPlaybackEnabled ? "Media Allowed" : "Allow Media";
            }

            if (PreviewFullscreenVideoSeparator != null)
            {
                PreviewFullscreenVideoSeparator.Visibility = canEnableVideo ? Visibility.Visible : Visibility.Collapsed;
            }

            if (PreviewFullscreenLinkTextBlock != null)
            {
                PreviewFullscreenLinkTextBlock.Text = hasLink ? SelectedArchiveEntry.Item.Link : string.Empty;
            }

        }

        private void ResetPreviewBrowserState(bool navigateToPlaceholder)
        {
            if (_isPreviewFullscreen)
            {
                SetPreviewFullscreen(false);
            }

            _showOriginalContent = false;
            _isReaderModeActive = false;
            _isReaderModeLoading = false;
            _readerContent = null;
            ResetPreviewVideoPlaybackState();
            SetPreviewPageLoading(false);

            if (navigateToPlaceholder && _previewWebViewInitialized)
            {
                try
                {
                    _browserSession?.NavigateToPlaceholder();
                }
                catch
                {
                }
            }

            UpdatePreviewActionButtons();
        }

        private void FilterControl_Changed(object sender, EventArgs e)
        {
            if (_isUpdatingControls ||
                GroupByComboBox == null ||
                SearchTextBox == null ||
                CategoryFilterComboBox == null ||
                FeedFilterComboBox == null ||
                LabelFilterComboBox == null ||
                ReadStateComboBox == null ||
                ArchivedFromDatePicker == null ||
                ArchivedToDatePicker == null ||
                SortFieldComboBox == null ||
                SortDirectionComboBox == null)
            {
                return;
            }

            RefreshArchiveView();
        }

        private void SelectedNoteTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingControls || _isUpdatingNoteEditor || SelectedArchiveEntry?.Item == null || SelectedNoteTextBox == null)
            {
                _noteSaveTimer.Stop();
                return;
            }

            var current = SelectedNoteTextBox.Text?.Trim() ?? string.Empty;
            var existing = _viewModel?.GetArticleNote(SelectedArchiveEntry.Item)?.Trim() ?? string.Empty;
            if (string.Equals(current, existing, StringComparison.Ordinal))
            {
                _noteSaveTimer.Stop();
                return;
            }

            _noteSaveTimer.Stop();
            _noteSaveTimer.Start();
        }

        private void SelectedNoteTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            _noteSaveTimer.Stop();
            SaveSelectedNote();
        }

        private ArchiveViewPreferences CaptureCurrentPreferences()
        {
            var selectedGroup = GroupByComboBox?.SelectedItem as GroupOption;
            var selectedSort = SortFieldComboBox?.SelectedItem as SortOption;
            var selectedDirection = SortDirectionComboBox?.SelectedItem as SortDirectionOption;
            var placementBounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            var preferredWindowState = WindowState == WindowState.Minimized ? _lastNonMinimizedWindowState : WindowState;

            return new ArchiveViewPreferences
            {
                GroupingMode = selectedGroup?.Key ?? "feed",
                SortField = selectedSort?.Key ?? "archived",
                SortDirection = selectedDirection?.Direction == ListSortDirection.Ascending ? "asc" : "desc",
                SelectedSavedViewName = NormalizeSavedViewName(SavedViewComboBox?.Text),
                SearchText = SearchTextBox?.Text?.Trim() ?? string.Empty,
                SelectedCategory = CategoryFilterComboBox?.SelectedItem as string ?? "All categories",
                SelectedFeed = FeedFilterComboBox?.SelectedItem as string ?? "All feeds",
                SelectedLabel = LabelFilterComboBox?.SelectedItem as string ?? "All labels",
                ReadState = ReadStateComboBox?.SelectedItem as string ?? "All",
                ArchivedFromDate = ArchivedFromDatePicker?.SelectedDate,
                ArchivedToDate = ArchivedToDatePicker?.SelectedDate,
                ShowArchivedColumn = _archivedColumnRequestedVisible,
                ShowPublishedColumn = IsColumnVisible(PublishedColumn),
                ShowCategoryColumn = IsColumnVisible(CategoryColumn),
                ShowFeedColumn = IsColumnVisible(FeedColumn),
                ShowLabelsColumn = IsColumnVisible(LabelsColumn),
                ShowConfigurationPanel = _isConfigurationPanelVisible,
                ShowPreviewPanel = _isPreviewVisible,
                WindowState = preferredWindowState == WindowState.Normal ? "normal" : "maximized",
                WindowWidth = placementBounds.Width > 0 ? placementBounds.Width : null,
                WindowHeight = placementBounds.Height > 0 ? placementBounds.Height : null,
                WindowLeft = placementBounds.Width > 0 ? placementBounds.Left : null,
                WindowTop = placementBounds.Height > 0 ? placementBounds.Top : null,
                ReadColumnIndex = ReadColumn?.DisplayIndex ?? 0,
                ArchivedColumnIndex = ArchivedColumn?.DisplayIndex ?? 1,
                PublishedColumnIndex = PublishedColumn?.DisplayIndex ?? 2,
                CategoryColumnIndex = CategoryColumn?.DisplayIndex ?? 3,
                FeedColumnIndex = FeedColumn?.DisplayIndex ?? 4,
                LabelsColumnIndex = LabelsColumn?.DisplayIndex ?? 5,
                TitleColumnIndex = TitleColumn?.DisplayIndex ?? 6
            };
        }

        private void ColumnVisibilityPopup_Opened(object sender, RoutedEventArgs e)
        {
            _isColumnPopupDirty = false;
            UpdateColumnVisibilityMenuChecks();
        }

        private void ColumnVisibilityPopup_Closed(object sender, RoutedEventArgs e)
        {
            if (_isColumnPopupDirty)
            {
                UpdateColumnVisibilityMenuChecks();
                _isColumnPopupDirty = false;
            }
        }

        private void SaveColumnVisibilityButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyPendingColumnVisibility();
            SchedulePreferencesSave();
            _isColumnPopupDirty = false;
            if (ColumnVisibilityPopup != null)
            {
                ColumnVisibilityPopup.IsPopupOpen = false;
            }
        }

        private void CancelColumnVisibilityButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateColumnVisibilityMenuChecks();
            _isColumnPopupDirty = false;
            if (ColumnVisibilityPopup != null)
            {
                ColumnVisibilityPopup.IsPopupOpen = false;
            }
        }

        private void ArchivedColumnCheckBox_Click(object sender, RoutedEventArgs e)
        {
            _isColumnPopupDirty = true;
        }

        private void TogglePreviewButton_Click(object sender, RoutedEventArgs e)
        {
            SetPreviewVisibility(!_isPreviewVisible);
            SchedulePreferencesSave();
        }

        private void ToggleConfigurationPanelButton_Click(object sender, RoutedEventArgs e)
        {
            SetConfigurationPanelVisibility(!_isConfigurationPanelVisible);
            SchedulePreferencesSave();
        }

        private void SavedViewComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingControls || SavedViewComboBox == null || _viewModel == null)
            {
                return;
            }

            var selectedName = NormalizeSavedViewName(SavedViewComboBox.SelectedItem as string ?? SavedViewComboBox.Text);
            if (string.IsNullOrWhiteSpace(selectedName))
            {
                return;
            }

            var savedView = _viewModel.GetArchiveSavedView(selectedName);
            if (savedView == null)
            {
                return;
            }

            ApplyPreferences(savedView.Preferences, preserveSavedViewName: false, applyWindowPlacement: false);
            SavedViewComboBox.Text = savedView.Name;
            RefreshArchiveView();
            SavePreferencesNow();
        }

        private void SaveCurrentViewButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null || SavedViewComboBox == null)
            {
                return;
            }

            var name = NormalizeSavedViewName(SavedViewComboBox.Text);
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(this,
                    "Enter a name for the saved archive view first.",
                    "Save Archive View",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                SavedViewComboBox.Focus();
                return;
            }

            var existing = _viewModel.GetArchiveSavedView(name);
            if (existing != null)
            {
                var overwrite = MessageBox.Show(
                    this,
                    $"The saved view \"{existing.Name}\" already exists.{Environment.NewLine}{Environment.NewLine}Do you want to overwrite it?",
                    "Overwrite Saved View",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (overwrite != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            var preferences = CaptureCurrentPreferences();
            preferences.SelectedSavedViewName = name;
            _viewModel.SaveArchiveSavedView(name, preferences);
            RefreshSavedViewOptions();
            SavedViewComboBox.Text = name;
            SavePreferencesNow();
        }

        private void ArchiveWindow_PlacementChanged(object sender, EventArgs e)
        {
            if (!IsLoaded || _viewModel == null || _isApplyingWindowPlacement)
            {
                return;
            }

            SchedulePreferencesSave();
        }

        private void ArchiveWindow_StateChanged(object sender, EventArgs e)
        {
            if (WindowState != WindowState.Minimized)
            {
                _lastNonMinimizedWindowState = WindowState;
            }

            ArchiveWindow_PlacementChanged(sender, e);
        }

        private void DeleteSavedViewButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null || SavedViewComboBox == null)
            {
                return;
            }

            var name = NormalizeSavedViewName(SavedViewComboBox.Text);
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(this,
                    "Select a saved archive view to delete.",
                    "Delete Saved View",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var savedView = _viewModel.GetArchiveSavedView(name);
            if (savedView == null)
            {
                MessageBox.Show(this,
                    $"The saved view \"{name}\" no longer exists.",
                    "Delete Saved View",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                RefreshSavedViewOptions();
                return;
            }

            var result = MessageBox.Show(
                this,
                $"Delete the saved view \"{savedView.Name}\"?{Environment.NewLine}{Environment.NewLine}Your current archive filters stay unchanged.",
                "Delete Saved View",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            if (_viewModel.DeleteArchiveSavedView(savedView.Name))
            {
                RefreshSavedViewOptions();
                SavedViewComboBox.Text = string.Empty;
                SavePreferencesNow();
            }
        }

        private void CleanupControl_Changed(object sender, EventArgs e)
        {
            if (_isUpdatingControls)
            {
                return;
            }

            SaveCleanupSettings();
        }

        private void ClearFiltersButton_Click(object sender, RoutedEventArgs e)
        {
            _isUpdatingControls = true;
            SearchTextBox.Text = string.Empty;
            CategoryFilterComboBox.SelectedIndex = 0;
            FeedFilterComboBox.SelectedIndex = 0;
            LabelFilterComboBox.SelectedIndex = 0;
            ReadStateComboBox.SelectedIndex = 0;
            ArchivedFromDatePicker.SelectedDate = null;
            ArchivedToDatePicker.SelectedDate = null;
            SortFieldComboBox.SelectedIndex = 0;
            SortDirectionComboBox.SelectedIndex = 0;
            _isUpdatingControls = false;

            RefreshArchiveView();
        }

        private void LibraryModeButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender == PinnedModeButton)
            {
                SwitchToMode(LibrarySectionMode.Pinned);
            }
            else if (sender == ReadLaterModeButton)
            {
                SwitchToMode(LibrarySectionMode.ReadLater);
            }
            else if (sender == ArchiveModeButton)
            {
                SwitchToMode(LibrarySectionMode.Archive);
            }
        }

        private void ManageLabelsButton_Click(object sender, RoutedEventArgs e)
        {
            OpenLabelManager();
        }

        private void QuickUnreadToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Primitives.ToggleButton toggleButton || ReadStateComboBox == null)
            {
                return;
            }

            _isUpdatingControls = true;
            ReadStateComboBox.SelectedItem = toggleButton.IsChecked == true ? "Unread" : "All";
            _isUpdatingControls = false;
            RefreshArchiveView();
        }

        private void QuickHasLabelsToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Primitives.ToggleButton toggleButton || LabelFilterComboBox == null)
            {
                return;
            }

            _isUpdatingControls = true;
            LabelFilterComboBox.SelectedItem = toggleButton.IsChecked == true ? "Has labels" : "All labels";
            _isUpdatingControls = false;
            RefreshArchiveView();
        }

        private void QuickLast7DaysToggleButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyQuickDateRange((sender as System.Windows.Controls.Primitives.ToggleButton)?.IsChecked == true, 7);
        }

        private void QuickLast30DaysToggleButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyQuickDateRange((sender as System.Windows.Controls.Primitives.ToggleButton)?.IsChecked == true, 30);
        }

        private void ApplyQuickDateRange(bool enabled, int days)
        {
            if (ArchivedFromDatePicker == null || ArchivedToDatePicker == null)
            {
                return;
            }

            _isUpdatingControls = true;
            if (enabled)
            {
                ArchivedFromDatePicker.SelectedDate = DateTime.Today.AddDays(-(days - 1));
                ArchivedToDatePicker.SelectedDate = DateTime.Today;
            }
            else
            {
                ArchivedFromDatePicker.SelectedDate = null;
                ArchivedToDatePicker.SelectedDate = null;
            }
            _isUpdatingControls = false;
            RefreshArchiveView();
        }

        private void SelectVisibleButton_Click(object sender, RoutedEventArgs e)
        {
            ArchiveDataGrid.SelectedItems.Clear();
            if (_archiveEntriesView == null)
            {
                SyncEntrySelectionFlagsFromGrid();
                UpdateSelectionState();
                return;
            }

            foreach (var entry in _archiveEntriesView.Cast<ArchiveEntryViewModel>())
            {
                ArchiveDataGrid.SelectedItems.Add(entry);
            }

            SyncEntrySelectionFlagsFromGrid();
            UpdateCounts();
            UpdateSelectionState();
        }

        private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            ArchiveDataGrid.SelectedItems.Clear();
            SyncEntrySelectionFlagsFromGrid();
            UpdateCounts();
            UpdateSelectionState();
            UpdatePreviewState();
        }

        private void ApplyCleanupButton_Click(object sender, RoutedEventArgs e)
        {
            SaveCleanupSettings();
            var days = (CleanupDaysComboBox?.SelectedItem as CleanupOption)?.Days ?? 90;
            var confirmResult = MessageBox.Show(
                this,
                $"This will permanently delete archived articles older than {days} day{(days == 1 ? string.Empty : "s")} from Archive.{Environment.NewLine}{Environment.NewLine}Do you want to continue?",
                "Confirm Archive Cleanup",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (confirmResult != MessageBoxResult.OK)
            {
                return;
            }

            var removedCount = _viewModel?.ApplyArchiveAutoCleanupPolicy() ?? 0;
            if (removedCount == 0)
            {
                MessageBox.Show(this,
                    "No archived articles matched the current retention policy.",
                    "Archive Cleanup",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void ArchiveDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var previousSelectedLink = SelectedArchiveEntry?.Item?.Link;
            SyncEntrySelectionFlagsFromGrid();
            UpdateCounts();
            UpdateSelectionState();
            UpdatePreviewState();
            var currentSelectedLink = SelectedArchiveEntry?.Item?.Link;
            if (!string.Equals(previousSelectedLink, currentSelectedLink, StringComparison.OrdinalIgnoreCase))
            {
                ResetPreviewBrowserState(navigateToPlaceholder: true);
                UpdatePreviewState();
            }
            CommandManager.InvalidateRequerySuggested();
        }

        private void ArchiveDataGrid_ColumnReordered(object sender, DataGridColumnEventArgs e)
        {
            if (_isUpdatingControls)
            {
                return;
            }

            SchedulePreferencesSave();
        }

        private void ArchiveDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            var sortKey = GetSortKeyForColumn(e.Column);
            if (string.IsNullOrWhiteSpace(sortKey) || SortFieldComboBox == null || SortDirectionComboBox == null)
            {
                return;
            }

            e.Handled = true;

            var currentSort = (SortFieldComboBox.SelectedItem as SortOption)?.Key ?? "archived";
            var currentDirection = (SortDirectionComboBox.SelectedItem as SortDirectionOption)?.Direction ?? ListSortDirection.Descending;
            var nextDirection = string.Equals(currentSort, sortKey, StringComparison.OrdinalIgnoreCase) && currentDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;

            _isUpdatingControls = true;
            try
            {
                SetSelectionByValue(SortFieldComboBox, sortKey, "archived");
                SetSelectionByDirection(SortDirectionComboBox, nextDirection == ListSortDirection.Ascending ? "asc" : "desc", "desc");
            }
            finally
            {
                _isUpdatingControls = false;
            }

            ApplyGroupingAndSort();
            SchedulePreferencesSave();
        }

        private void ArchiveDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            OpenSelectedArticle();
        }

        private void OpenSelectedArticle()
        {
            if (_viewModel == null)
            {
                return;
            }

            var entry = ArchiveDataGrid.SelectedItem as ArchiveEntryViewModel;
            if (entry?.Item == null)
            {
                return;
            }

            var command = _viewModel.OpenArticleInWindowCommand;
            if (command?.CanExecute(entry.Item) == true)
            {
                command.Execute(entry.Item);
            }
        }

        private List<FeedItem> GetSelectedItems()
        {
            if (ArchiveDataGrid?.SelectedItems == null)
            {
                return new List<FeedItem>();
            }

            return ArchiveDataGrid.SelectedItems
                .OfType<ArchiveEntryViewModel>()
                .Select(entry => entry.Item)
                .Where(item => item != null)
                .Distinct()
                .ToList();
        }

        private List<FeedItem> GetContextTargetItems(FeedItem contextItem)
        {
            var selectedItems = GetSelectedItems();
            var key = contextItem?.Link?.Trim();
            if (!string.IsNullOrWhiteSpace(key) &&
                selectedItems.Any(item => string.Equals(item.Link?.Trim(), key, StringComparison.OrdinalIgnoreCase)))
            {
                return selectedItems;
            }

            return contextItem == null
                ? selectedItems
                : new List<FeedItem> { contextItem };
        }

        private bool ContextTargetsSingleItem(FeedItem contextItem)
        {
            return GetContextTargetItems(contextItem).Count == 1;
        }

        private void OpenLabelManager()
        {
            if (_viewModel == null)
            {
                return;
            }

            var window = new LabelManagerWindow(_viewModel)
            {
                Owner = this
            };
            window.ShowDialog();
            RefreshLabelOptions(LabelFilterComboBox?.SelectedItem as string ?? _pendingLabelSelection);
            RefreshArchiveView();
            CommandManager.InvalidateRequerySuggested();
        }

        private List<ArticleLabelDefinition> GetAvailableLabels()
        {
            return (_viewModel?.GetArticleLabels() ?? Array.Empty<ArticleLabelDefinition>()).ToList();
        }

        private List<string> GetCommonLabels(IEnumerable<FeedItem> items)
        {
            var materializedItems = items?.Where(item => item != null).ToList() ?? new List<FeedItem>();
            if (materializedItems.Count == 0)
            {
                return new List<string>();
            }

            HashSet<string> common = null;
            foreach (var item in materializedItems)
            {
                var names = new HashSet<string>(_viewModel?.GetArticleLabelNames(item) ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                common = common == null ? names : new HashSet<string>(common.Intersect(names, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
            }

            return common?
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToList()
                ?? new List<string>();
        }

        private void ToggleLabelForItems(IEnumerable<FeedItem> items, string labelName)
        {
            if (_viewModel == null || string.IsNullOrWhiteSpace(labelName))
            {
                return;
            }

            var materializedItems = items?.Where(item => item != null).Distinct().ToList() ?? new List<FeedItem>();
            if (materializedItems.Count == 0)
            {
                return;
            }

            _viewModel.ToggleArticleLabel(materializedItems, labelName);
        }

        private void PopulateLabelsMenu(ContextMenu contextMenu, IReadOnlyList<FeedItem> targetItems)
        {
            var labelsRootItem = contextMenu.Items
                .OfType<MenuItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, "labels-root", StringComparison.OrdinalIgnoreCase));
            if (labelsRootItem == null)
            {
                return;
            }

            labelsRootItem.Items.Clear();

            foreach (var label in GetAvailableLabels())
            {
                var labelMenuItem = new MenuItem
                {
                    Header = label.Name,
                    IsCheckable = true,
                    IsChecked = targetItems.Count > 0 && targetItems.All(item =>
                        (_viewModel?.GetArticleLabelNames(item) ?? Array.Empty<string>())
                            .Any(existing => string.Equals(existing, label.Name, StringComparison.OrdinalIgnoreCase))),
                    StaysOpenOnClick = true,
                    Style = (Style)FindResource("ArticleMenuItemStyle"),
                    Icon = new Border
                    {
                        Width = 12,
                        Height = 12,
                        CornerRadius = new CornerRadius(6),
                        Background = (Brush)new BrushConverter().ConvertFromString(label.ColorHex),
                        BorderBrush = (Brush)FindResource("MaterialDesignDivider"),
                        BorderThickness = new Thickness(1)
                    }
                };
                labelMenuItem.CommandParameter = new Tuple<List<FeedItem>, string>(targetItems.ToList(), label.Name);
                labelMenuItem.Click += LabelMenuItem_Click;
                labelsRootItem.Items.Add(labelMenuItem);
            }

            var manageLabelsItem = new MenuItem
            {
                Header = "Manage Labels...",
                Style = (Style)FindResource("ArticleMenuItemStyle")
            };
            manageLabelsItem.Click += (_, __) => OpenLabelManager();
            if (labelsRootItem.Items.Count > 0)
            {
                labelsRootItem.Items.Add(new Separator());
            }
            else
            {
                var emptyItem = new MenuItem
                {
                    Header = "No labels yet",
                    IsEnabled = false,
                    Style = (Style)FindResource("ArticleMenuItemStyle")
                };
                labelsRootItem.Items.Add(emptyItem);
                labelsRootItem.Items.Add(new Separator());
            }

            labelsRootItem.Items.Add(manageLabelsItem);
        }

        private void LabelMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem ||
                menuItem.CommandParameter is not Tuple<List<FeedItem>, string> payload)
            {
                return;
            }

            ToggleLabelForItems(payload.Item1, payload.Item2);
        }

        private void ArticleRowContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is not ContextMenu contextMenu ||
                contextMenu.PlacementTarget is not FrameworkElement placementTarget ||
                placementTarget.DataContext is not ArchiveEntryViewModel entry)
            {
                return;
            }

            var targetItems = GetContextTargetItems(entry.Item);
            PopulateLabelsMenu(contextMenu, targetItems);

            foreach (var menuItem in contextMenu.Items.OfType<MenuItem>())
            {
                if (menuItem.Command == ContextUnarchiveSelectionCommand)
                {
                    menuItem.Header = _currentMode switch
                    {
                        LibrarySectionMode.Pinned => targetItems.Count > 1 ? "Unpin Selected" : "Unpin",
                        LibrarySectionMode.ReadLater => targetItems.Count > 1 ? "Remove from Read Later" : "Remove from Read Later",
                        _ => targetItems.Count > 1 ? "Unarchive Selected" : "Unarchive"
                    };
                    continue;
                }

                if (menuItem.Command == ContextArchiveSelectionCommand)
                {
                    menuItem.Header = targetItems.Count > 1 ? "Archive Selected" : "Archive";
                    menuItem.Visibility = _currentMode == LibrarySectionMode.Archive
                        ? Visibility.Collapsed
                        : Visibility.Visible;
                    continue;
                }

                if (menuItem.Command == ContextMarkUnreadSelectionCommand)
                {
                    menuItem.Header = targetItems.Count > 1 ? "Mark Selected as Unread" : "Mark as Unread";
                    menuItem.IsEnabled = targetItems.Any(item => item?.IsRead == true);
                    continue;
                }

                if (menuItem.Command == ContextPinSelectionCommand)
                {
                    menuItem.Visibility = _currentMode == LibrarySectionMode.Pinned
                        ? Visibility.Collapsed
                        : Visibility.Visible;
                    continue;
                }

                if (menuItem.Command == ContextReadLaterSelectionCommand)
                {
                    menuItem.Visibility = _currentMode == LibrarySectionMode.ReadLater
                        ? Visibility.Collapsed
                        : Visibility.Visible;
                }
            }
        }

        private List<FeedItem> GetItemsForGroup(CollectionViewGroup group)
        {
            var result = new List<FeedItem>();
            if (group == null)
            {
                return result;
            }

            foreach (var item in group.Items)
            {
                switch (item)
                {
                    case ArchiveEntryViewModel entry when entry.Item != null:
                        result.Add(entry.Item);
                        break;
                    case CollectionViewGroup nestedGroup:
                        result.AddRange(GetItemsForGroup(nestedGroup));
                        break;
                }
            }

            return result
                .Where(item => item != null)
                .Distinct()
                .ToList();
        }

        private bool IsGroupFullySelected(CollectionViewGroup group)
        {
            var groupItems = GetItemsForGroup(group);
            if (groupItems.Count == 0)
            {
                return false;
            }

            var selectedKeys = new HashSet<string>(
                GetSelectedItems()
                    .Select(item => item.Link?.Trim())
                    .Where(link => !string.IsNullOrWhiteSpace(link)),
                StringComparer.OrdinalIgnoreCase);
            var groupKeys = groupItems
                .Select(item => item.Link?.Trim())
                .Where(link => !string.IsNullOrWhiteSpace(link))
                .ToList();

            return groupKeys.Count > 0 && groupKeys.All(selectedKeys.Contains);
        }

        private void SelectGroup(CollectionViewGroup group)
        {
            var items = GetItemsForGroup(group);
            if (items.Count == 0)
            {
                return;
            }

            var keys = new HashSet<string>(
                items.Select(item => item.Link?.Trim()).Where(link => !string.IsNullOrWhiteSpace(link)),
                StringComparer.OrdinalIgnoreCase);

            var shouldUnselect = IsGroupFullySelected(group);
            if (!shouldUnselect)
            {
                foreach (var entry in _archiveEntries.Where(entry => keys.Contains(entry.Link?.Trim() ?? string.Empty)))
                {
                    if (!ArchiveDataGrid.SelectedItems.Contains(entry))
                    {
                        ArchiveDataGrid.SelectedItems.Add(entry);
                    }
                }
            }
            else
            {
                foreach (var entry in _archiveEntries.Where(entry => keys.Contains(entry.Link?.Trim() ?? string.Empty)).ToList())
                {
                    ArchiveDataGrid.SelectedItems.Remove(entry);
                }
            }

            if (!shouldUnselect && ArchiveDataGrid.SelectedItems.Count > 0)
            {
                ArchiveDataGrid.ScrollIntoView(ArchiveDataGrid.SelectedItems[0]);
            }

            SyncEntrySelectionFlagsFromGrid();
            UpdateCounts();
            UpdateSelectionState();
            UpdatePreviewState();
            CommandManager.InvalidateRequerySuggested();
        }

        private void GroupContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is not ContextMenu contextMenu ||
                contextMenu.PlacementTarget is not FrameworkElement placementTarget ||
                placementTarget.DataContext is not CollectionViewGroup group ||
                contextMenu.Items.Count == 0 ||
                contextMenu.Items[0] is not MenuItem menuItem)
            {
                return;
            }

            menuItem.Header = IsGroupFullySelected(group) ? "Unselect group" : "Select group";
        }

        private List<FeedItem> GetVisibleItems()
        {
            if (_archiveEntriesView == null)
            {
                return new List<FeedItem>();
            }

            return _archiveEntriesView
                .Cast<ArchiveEntryViewModel>()
                .Select(entry => entry.Item)
                .Where(item => item != null)
                .Distinct()
                .ToList();
        }

        private List<FeedItem> GetAllArchivedItems()
        {
            return _archiveEntries
                .Select(entry => entry.Item)
                .Where(item => item != null)
                .Distinct()
                .ToList();
        }

        private void ExecuteBatchCommand(ICommand command)
        {
            ExecuteBatchCommand(command, GetSelectedItems());
        }

        private void ExecuteBatchCommand(ICommand command, IEnumerable<FeedItem> items)
        {
            var materializedItems = items?
                .Where(item => item != null)
                .Distinct()
                .ToList() ?? new List<FeedItem>();
            if (materializedItems.Count == 0)
            {
                return;
            }

            foreach (var item in materializedItems)
            {
                if (command?.CanExecute(item) == true)
                {
                    command.Execute(item);
                }
            }
        }

        private void SaveCleanupSettings()
        {
            if (_viewModel == null || CleanupDaysComboBox == null || AutoCleanupEnabledCheckBox == null)
            {
                return;
            }

            var days = (CleanupDaysComboBox.SelectedItem as CleanupOption)?.Days ?? 90;
            _viewModel.SaveArchiveAutoCleanupSettings(AutoCleanupEnabledCheckBox.IsChecked == true, days);
        }

        private bool ConfirmUnarchive(string scopeLabel, int count)
        {
            if (count <= 0)
            {
                return false;
            }

            var targetLabel = count == 1
                ? "this archived article"
                : $"{count} archived articles ({scopeLabel})";
            var result = MessageBox.Show(
                this,
                $"This will remove {targetLabel} from Archive and place {(count == 1 ? "it" : "them")} back into the regular feed lists.{Environment.NewLine}{Environment.NewLine}Do you want to continue?",
                "Confirm Unarchive",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            return result == MessageBoxResult.Yes;
        }

        private void ExecuteConfirmedUnarchive(IEnumerable<FeedItem> items, string scopeLabel)
        {
            var materializedItems = items?
                .Where(item => item != null)
                .Distinct()
                .ToList() ?? new List<FeedItem>();
            if (materializedItems.Count == 0 || !ConfirmUnarchive(scopeLabel, materializedItems.Count))
            {
                return;
            }

            ExecuteBatchCommand(_viewModel?.ArchiveArticleCommand, materializedItems);
        }

        private List<ArchiveEntryViewModel> GetFilteredEntries()
        {
            if (_archiveEntriesView == null)
            {
                return new List<ArchiveEntryViewModel>();
            }

            return _archiveEntriesView
                .Cast<ArchiveEntryViewModel>()
                .ToList();
        }

        private List<ArchiveEntryViewModel> GetSelectedEntries()
        {
            if (ArchiveDataGrid?.SelectedItems == null)
            {
                return new List<ArchiveEntryViewModel>();
            }

            return ArchiveDataGrid.SelectedItems
                .OfType<ArchiveEntryViewModel>()
                .ToList();
        }

        private List<ArchiveExportEntry> BuildExportEntries(IEnumerable<ArchiveEntryViewModel> entries)
        {
            return entries?
                .Where(entry => entry != null)
                .Select(entry => new ArchiveExportEntry
                {
                    Title = entry.Title ?? string.Empty,
                    Feed = entry.FeedName ?? string.Empty,
                    Category = entry.Category ?? string.Empty,
                    Labels = entry.LabelsText ?? string.Empty,
                    Note = entry.Note ?? string.Empty,
                    ArchivedAt = entry.ArchivedAt,
                    PublicationDate = entry.PublicationDate,
                    IsRead = entry.IsRead,
                    Link = entry.Link ?? string.Empty,
                    Description = entry.Description ?? string.Empty
                })
                .ToList()
                ?? new List<ArchiveExportEntry>();
        }

        private static string EscapeCsv(string value)
        {
            var normalized = value ?? string.Empty;
            if (normalized.Contains('"'))
            {
                normalized = normalized.Replace("\"", "\"\"");
            }

            return normalized.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
                ? $"\"{normalized}\""
                : normalized;
        }

        private List<ArchiveExportEntry> GetExportEntriesForCurrentScope(out string scopeLabel)
        {
            var scopeKey = string.IsNullOrWhiteSpace(_selectedExportScopeKey) ? "filtered" : _selectedExportScopeKey;
            scopeLabel = scopeKey switch
            {
                "all" => "All archived",
                "selected" => "Selected",
                _ => "Filtered"
            };

            return scopeKey switch
            {
                "all" => BuildExportEntries(_archiveEntries),
                "selected" => BuildExportEntries(GetSelectedEntries()),
                _ => BuildExportEntries(GetFilteredEntries())
            };
        }

        private void ExportArchiveEntries(string format)
        {
            var entries = GetExportEntriesForCurrentScope(out var scopeLabel);
            if (entries.Count == 0)
            {
                MessageBox.Show(this,
                    $"There are no archive entries in the current export scope: {scopeLabel}.",
                    "Archive Export",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var isJson = string.Equals(format, "json", StringComparison.OrdinalIgnoreCase);
            var dialog = new SaveFileDialog
            {
                Title = isJson ? $"Export {scopeLabel} archive entries to JSON" : $"Export {scopeLabel} archive entries to CSV",
                Filter = isJson ? "JSON files (*.json)|*.json" : "CSV files (*.csv)|*.csv",
                DefaultExt = isJson ? ".json" : ".csv",
                FileName = $"archive-{scopeLabel.ToLowerInvariant().Replace(' ', '-')}-export-{DateTime.Now:yyyyMMdd-HHmmss}.{(isJson ? "json" : "csv")}"
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                if (isJson)
                {
                    var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(dialog.FileName, json, Encoding.UTF8);
                }
                else
                {
                    var builder = new StringBuilder();
                    builder.AppendLine("ArchivedAt,PublishedAt,Category,Feed,Labels,Note,Title,IsRead,Link,Description");
                    foreach (var entry in entries)
                    {
                        builder.Append(EscapeCsv(entry.ArchivedAt?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty)).Append(',')
                            .Append(EscapeCsv(entry.PublicationDate.ToString("yyyy-MM-dd HH:mm"))).Append(',')
                            .Append(EscapeCsv(entry.Category)).Append(',')
                            .Append(EscapeCsv(entry.Feed)).Append(',')
                            .Append(EscapeCsv(entry.Labels)).Append(',')
                            .Append(EscapeCsv(entry.Note)).Append(',')
                            .Append(EscapeCsv(entry.Title)).Append(',')
                            .Append(EscapeCsv(entry.IsRead ? "Read" : "Unread")).Append(',')
                            .Append(EscapeCsv(entry.Link)).Append(',')
                            .Append(EscapeCsv(entry.Description))
                            .AppendLine();
                    }

                    File.WriteAllText(dialog.FileName, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"Could not export archive entries.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                    "Archive Export",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private List<string> GetSelectedNormalizedLinks()
        {
            return GetNormalizedLinks(GetSelectedItems());
        }

        private List<string> GetNormalizedLinks(IEnumerable<FeedItem> items)
        {
            return items
                ?.Select(item => _viewModel?.GetNormalizedArticleLink(item))
                .Where(link => !string.IsNullOrWhiteSpace(link))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? new List<string>();
        }

        private void CopyLinksToClipboard(IEnumerable<string> links)
        {
            var distinctLinks = links?
                .Where(link => !string.IsNullOrWhiteSpace(link))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (distinctLinks == null || distinctLinks.Count == 0)
            {
                return;
            }

            try
            {
                Clipboard.SetText(string.Join(Environment.NewLine, distinctLinks));
            }
            catch
            {
                // Ignore clipboard failures.
            }
        }

        private void OpenSelectedLinksExternally(IEnumerable<FeedItem> items)
        {
            if (_viewModel == null)
            {
                return;
            }

            foreach (var item in items?.Where(item => item != null).Distinct() ?? Enumerable.Empty<FeedItem>())
            {
                _viewModel.TryOpenArticleExternally(item);
            }
        }

        private async void PreviewShowContentButton_Click(object sender, RoutedEventArgs e)
        {
            if (_showOriginalContent && !_isReaderModeActive)
            {
                ResetPreviewBrowserState(navigateToPlaceholder: true);
                UpdatePreviewState();
                return;
            }

            await ShowOriginalContentAsync();
        }

        private async void PreviewEnableVideoButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedArchiveEntry?.Item == null ||
                string.IsNullOrWhiteSpace(SelectedArchiveEntry.Item.Link) ||
                _isPreviewVideoPlaybackEnabled)
            {
                return;
            }

            if (!await EnsurePreviewWebViewAsync())
            {
                return;
            }

            _isPreviewVideoPlaybackEnabled = true;
            _browserSession.SetMediaPlaybackEnabled(true);
            await ShowOriginalContentAsync();
        }

        private async void PreviewReaderModeButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedArchiveEntry?.Item == null || _isReaderModeLoading)
            {
                return;
            }

            if (_isReaderModeActive)
            {
                ResetPreviewBrowserState(navigateToPlaceholder: true);
                UpdatePreviewState();
                return;
            }

            if (string.IsNullOrWhiteSpace(SelectedArchiveEntry.Item.Link))
            {
                return;
            }

            await LoadReaderModeAsync(forceReaderRefresh: false);
        }

        private async Task ShowOriginalContentAsync()
        {
            if (SelectedArchiveEntry?.Item == null || string.IsNullOrWhiteSpace(SelectedArchiveEntry.Item.Link))
            {
                return;
            }

            if (!await EnsurePreviewWebViewAsync())
            {
                OpenSelectedLinksExternally(new[] { SelectedArchiveEntry.Item });
                return;
            }

            _browserSession.SetDarkMode(_viewModel?.DarkMode == true);
            _browserSession.SetAdBlockerEnabled(_viewModel?.AdBlockerEnabled == true);
            _browserSession.SetMediaPlaybackEnabled(_isPreviewVideoPlaybackEnabled);
            _viewModel?.MarkArticleAsReadFromExternalView(SelectedArchiveEntry.Item);
            SetPreviewPageLoading(true);
            _showOriginalContent = true;
            _isReaderModeActive = false;
            _readerContent = null;
            UpdatePreviewState();

            var navigated = await _browserSession.NavigateFastAsync(SelectedArchiveEntry.Item.Link);
            if (!navigated)
            {
                SetPreviewPageLoading(false);
                OpenSelectedLinksExternally(new[] { SelectedArchiveEntry.Item });
            }

            UpdatePreviewActionButtons();
        }

        private async Task LoadReaderModeAsync(bool forceReaderRefresh)
        {
            if (SelectedArchiveEntry?.Item == null || string.IsNullOrWhiteSpace(SelectedArchiveEntry.Item.Link))
            {
                return;
            }

            _isReaderModeLoading = true;
            UpdatePreviewActionButtons();

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

                _browserSession.SetDarkMode(_viewModel?.DarkMode == true);
                _browserSession.SetAdBlockerEnabled(_viewModel?.AdBlockerEnabled == true);
                ResetPreviewVideoPlaybackState();
                if (forceReaderRefresh)
                {
                    _readerContent = null;
                }

                _viewModel?.MarkArticleAsReadFromExternalView(SelectedArchiveEntry.Item);
                _readerContent ??= await _articleReaderService.ExtractAsync(SelectedArchiveEntry.Item.Link);
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
                UpdatePreviewActionButtons();
            }
        }

        private void ResetPreviewVideoPlaybackState()
        {
            if (!_isPreviewVideoPlaybackEnabled && !_browserSession.IsMediaPlaybackEnabled)
            {
                return;
            }

            _isPreviewVideoPlaybackEnabled = false;
            _browserSession.SetMediaPlaybackEnabled(false);
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
                if (_viewModel == null)
                {
                    _previewWebViewInitializationTask = null;
                    return false;
                }

                _browserSession ??= _viewModel.CreateBrowserSession();
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
            if (string.IsNullOrWhiteSpace(e.Uri) || e.Uri.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase))
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
            if (PreviewLoadingOverlay != null)
            {
                PreviewLoadingOverlay.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            }

            UpdatePreviewActionButtons();
        }

        private string BuildReaderModeHtml(ReaderArticleContent content)
        {
            var backgroundColor = _viewModel?.DarkMode == true ? "#121212" : "#ffffff";
            var textColor = _viewModel?.DarkMode == true ? "#e0e0e0" : "#333333";
            var headerColor = _viewModel?.DarkMode == true ? "#90caf9" : "#673ab7";
            var metaColor = _viewModel?.DarkMode == true ? "#b0b0b0" : "#666666";
            var borderColor = _viewModel?.DarkMode == true ? "#90caf9" : "#673ab7";
            var title = string.IsNullOrWhiteSpace(content?.Title) ? SelectedArchiveEntry?.Title : content.Title;
            var encodedTitle = WebUtility.HtmlEncode(title ?? "Article");
            var encodedFeedName = WebUtility.HtmlEncode(SelectedArchiveEntry?.FeedName ?? "Unknown feed");
            var encodedSourceLink = WebUtility.HtmlEncode(SelectedArchiveEntry?.Item?.Link ?? string.Empty);
            var metaText = WebUtility.HtmlEncode($"{SelectedArchiveEntry?.PublicationDate:yyyy-MM-dd HH:mm}");

            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1'>
    <meta name='color-scheme' content='{(_viewModel?.DarkMode == true ? "dark light" : "light dark")}'>
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
            margin-bottom: 4px;
            word-break: break-word;
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
        <div class='article-meta'>{metaText} • {encodedFeedName}</div>
        <div class='article-meta'>{encodedSourceLink}</div>
    </div>
    <div class='article-content'>
        {content?.HtmlContent ?? string.Empty}
    </div>
</body>
</html>";
        }

        private void PreviewOpenArticleButton_Click(object sender, RoutedEventArgs e)
        {
            OpenSelectedArticle();
        }

        private void PreviewFullscreenButton_Click(object sender, RoutedEventArgs e)
        {
            TogglePreviewFullscreen();
        }

        private void PreviewFullscreenRefreshButton_Click(object sender, RoutedEventArgs e)
        {
            _ = RefreshPreviewCurrentPageAsync();
        }

        private async Task RefreshPreviewCurrentPageAsync()
        {
            if (SelectedArchiveEntry?.Item == null || string.IsNullOrWhiteSpace(SelectedArchiveEntry.Item.Link))
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

        private void ArchiveWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            HandleFullscreenShortcut(e);
        }

        private void PreviewWebView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            HandleFullscreenShortcut(e);
        }

        private void HandleFullscreenShortcut(KeyEventArgs e)
        {
            if (e.Key == Key.F11)
            {
                if (_showOriginalContent || _isReaderModeActive || _isPreviewFullscreen)
                {
                    TogglePreviewFullscreen();
                    e.Handled = true;
                }
                return;
            }

            if (e.Key == Key.Escape && _isPreviewFullscreen)
            {
                SetPreviewFullscreen(false);
                e.Handled = true;
            }
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

            var parentGrid = OverviewGroupBox?.Parent as Grid;
            if (parentGrid == null)
            {
                return;
            }

            if (isFullscreen)
            {
                _savedOverviewColumnWidth = parentGrid.ColumnDefinitions[0].Width;
                _savedOverviewSplitterColumnWidth = parentGrid.ColumnDefinitions[1].Width;
                _savedItemsColumnWidth = parentGrid.ColumnDefinitions[2].Width;
                _savedPreviewSplitterColumnWidth = PreviewSplitterColumnDefinition.Width;
                _savedPreviewColumnWidth = PreviewColumnDefinition.Width;
                _savedWindowState = WindowState;

                HeaderPanel.Visibility = Visibility.Collapsed;
                FooterActionsPanel.Visibility = Visibility.Collapsed;
                OverviewGroupBox.Visibility = Visibility.Collapsed;
                ItemsGroupBox.Visibility = Visibility.Collapsed;
                PreviewGridSplitter.Visibility = Visibility.Collapsed;
                parentGrid.ColumnDefinitions[0].Width = new GridLength(0);
                parentGrid.ColumnDefinitions[1].Width = new GridLength(0);
                parentGrid.ColumnDefinitions[2].Width = new GridLength(0);
                PreviewSplitterColumnDefinition.Width = new GridLength(0);
                Grid.SetColumn(PreviewGroupBox, 0);
                Grid.SetColumnSpan(PreviewGroupBox, 5);
                WindowState = WindowState.Maximized;
            }
            else
            {
                HeaderPanel.Visibility = Visibility.Visible;
                FooterActionsPanel.Visibility = Visibility.Visible;
                OverviewGroupBox.Visibility = Visibility.Visible;
                ItemsGroupBox.Visibility = Visibility.Visible;
                PreviewGridSplitter.Visibility = Visibility.Visible;
                parentGrid.ColumnDefinitions[0].Width = _savedOverviewColumnWidth;
                parentGrid.ColumnDefinitions[1].Width = _savedOverviewSplitterColumnWidth;
                parentGrid.ColumnDefinitions[2].Width = _savedItemsColumnWidth;
                PreviewSplitterColumnDefinition.Width = _savedPreviewSplitterColumnWidth;
                PreviewColumnDefinition.Width = _savedPreviewColumnWidth;
                Grid.SetColumn(PreviewGroupBox, 4);
                Grid.SetColumnSpan(PreviewGroupBox, 1);
                WindowState = _savedWindowState;
            }

            _isPreviewFullscreen = isFullscreen;
            UpdatePreviewActionButtons();
        }

        private void PreviewOpenLinkButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedArchiveEntry?.Item == null)
            {
                return;
            }

            _viewModel?.PromptOpenExternalLink(SelectedArchiveEntry.Item.Link);
        }

        private void PreviewCopyLinkButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedArchiveEntry?.Item == null)
            {
                return;
            }

            CopyLinksToClipboard(GetNormalizedLinks(new[] { SelectedArchiveEntry.Item }));
        }

        private void DotsMenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.ContextMenu != null)
            {
                button.ContextMenu.PlacementTarget = button;
                button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                button.ContextMenu.IsOpen = true;
                e.Handled = true;
            }
        }

        private void MoreActionsContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            UpdateSelectionState();

            if (ExportScopeAllMenuItem != null)
            {
                ExportScopeAllMenuItem.IsChecked = string.Equals(_selectedExportScopeKey, "all", StringComparison.OrdinalIgnoreCase);
            }

            if (ExportScopeFilteredMenuItem != null)
            {
                ExportScopeFilteredMenuItem.IsChecked = string.Equals(_selectedExportScopeKey, "filtered", StringComparison.OrdinalIgnoreCase);
            }

            if (ExportScopeSelectedMenuItem != null)
            {
                ExportScopeSelectedMenuItem.IsChecked = string.Equals(_selectedExportScopeKey, "selected", StringComparison.OrdinalIgnoreCase);
            }

            if (ExportScopeAllMenuItem != null)
            {
                ExportScopeAllMenuItem.Header = _currentMode switch
                {
                    LibrarySectionMode.Pinned => "All pinned",
                    LibrarySectionMode.ReadLater => "All Read Later",
                    _ => "All archived"
                };
            }
        }

        private void ExportScopeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem)
            {
                return;
            }

            _selectedExportScopeKey = menuItem.Tag as string ?? "filtered";
            MoreActionsContextMenu_Opened(sender, e);
        }

        private void UnarchiveAllButton_Click(object sender, RoutedEventArgs e)
        {
            ExecutePrimaryAction(GetAllArchivedItems(), "all");
        }

        private void ExportCsvButton_Click(object sender, RoutedEventArgs e)
        {
            ExportArchiveEntries("csv");
        }

        private void ExportJsonButton_Click(object sender, RoutedEventArgs e)
        {
            ExportArchiveEntries("json");
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void ExecutePrimaryAction(IEnumerable<FeedItem> items, string scopeLabel)
        {
            var materializedItems = items?
                .Where(item => item != null)
                .Distinct()
                .ToList() ?? new List<FeedItem>();
            if (materializedItems.Count == 0)
            {
                return;
            }

            switch (_currentMode)
            {
                case LibrarySectionMode.Pinned:
                    ExecuteBatchCommand(_viewModel?.PinArticleCommand, materializedItems);
                    break;
                case LibrarySectionMode.ReadLater:
                    ExecuteBatchCommand(_viewModel?.ReadLaterArticleCommand, materializedItems);
                    break;
                default:
                    ExecuteConfirmedUnarchive(materializedItems, scopeLabel);
                    break;
            }
        }
    }
}