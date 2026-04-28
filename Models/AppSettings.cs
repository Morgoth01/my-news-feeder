using System;
using System.Collections.Generic;
using System.Linq;

namespace MyNewsFeeder.Models
{
    public class AppSettings
    {
        public static readonly string[] DefaultAdvertisementKeywords = new[]
        {
            "advertisement",
            "anzeige",
            "sponsored",
            "werbung",
            "promotion",
            "download",
            "advertorial"
        };
        public bool DarkMode { get; set; } = false;
        public bool AutoRefresh { get; set; } = false;
        public bool LiveRefresh { get; set; } = false;
        public int MaxItems { get; set; } = 10;
        public int MaxFeeds { get; set; } = 10;
        public string KeywordFilter { get; set; } = string.Empty;
        public int FontSize { get; set; } = 12;
        public double TreeWidth { get; set; } = 250.0;
        public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(1);
        public bool AdBlockerEnabled { get; set; } = true;
        public bool AdvertisementFilterEnabled { get; set; } = false;
        public List<string> AdvertisementKeywords { get; set; } = new List<string>(DefaultAdvertisementKeywords);
        public bool GroupFeedsByCategory { get; set; } = false;
        public Dictionary<string, bool> TreeViewExpandedStates { get; set; } = new Dictionary<string, bool>();
        public Dictionary<string, bool> SectionExpandedStates { get; set; } = new Dictionary<string, bool>();
        public bool IsShowContentAlwaysOn { get; set; } = false;
        public double ArticleWindowHeight { get; set; } = 350;
        public double BrowserWindowHeight { get; set; } = 350;
        public bool HasCustomHeights { get; set; } = false;
        public string UserAgent { get; set; } = "MyNewsFeeder/1.0";
        public int ConnectionTimeout { get; set; } = 30;
        public bool EnableLogging { get; set; } = true;
        public string LogLevel { get; set; } = "Debug";
        public bool AutoSaveSettings { get; set; } = true;
        public string DefaultBrowser { get; set; } = "Integrated";
        public bool ShowThumbnails { get; set; } = false;
        public int ArticlePreviewHeight { get; set; } = 400;
        public bool EnableNotifications { get; set; } = false;
        public List<string> ImportantKeywords { get; set; } = new List<string>();
        public List<ImportantNotificationItem> RecentImportantNotifications { get; set; } = new List<ImportantNotificationItem>();
        public int ImportantNotificationsRetentionHours { get; set; } = 24;
        public string Language { get; set; } = "en-US";
        public int AutoRefreshIntervalMinutes { get; set; } = 10;
        public int LiveRefreshIntervalSeconds { get; set; } = 60;
        public bool UseCompactArticleCards { get; set; } = false;
        public bool AutoUpdateCheckEnabled { get; set; } = true;
        public bool AutoUpdatePromptShown { get; set; } = false;

        // New category-related properties
        public Dictionary<string, bool> CategoryExpandedStates { get; set; } = new Dictionary<string, bool>();
        public List<string> Categories { get; set; } = new List<string> { "Default" };

        // Persistent read-state across sessions (link-based)
        public HashSet<string> ReadArticleLinks { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> PinnedArticleLinks { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ReadLaterArticleLinks { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ArchivedArticleLinks { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public List<FeedItem> PinnedArticleSnapshots { get; set; } = new List<FeedItem>();
        public List<FeedItem> ReadLaterArticleSnapshots { get; set; } = new List<FeedItem>();
        public List<FeedItem> ArchivedArticleSnapshots { get; set; } = new List<FeedItem>();
        public List<ArticleLabelDefinition> ArticleLabels { get; set; } = new List<ArticleLabelDefinition>();
        public List<SavedLabelColorDefinition> SavedLabelColorDefinitions { get; set; } = new List<SavedLabelColorDefinition>();
        public List<string> SavedLabelColors { get; set; } = new List<string>();
        public Dictionary<string, List<string>> ArticleLabelAssignments { get; set; } = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> ArticleNoteAssignments { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public ArchiveViewPreferences ArchiveViewPreferences { get; set; } = new ArchiveViewPreferences();
        public FeedAllWindowPreferences FeedAllWindowPreferences { get; set; } = new FeedAllWindowPreferences();
        public FeedManagerWindowPreferences FeedManagerWindowPreferences { get; set; } = new FeedManagerWindowPreferences();
        public List<ArchiveSavedView> ArchiveSavedViews { get; set; } = new List<ArchiveSavedView>();
        public bool ArchiveAutoCleanupEnabled { get; set; } = false;
        public int ArchiveAutoCleanupDays { get; set; } = 90;

        public static readonly int[] AvailableRefreshIntervals = { 5, 10, 15, 20, 30, 45, 60 };
        public static readonly int[] AvailableLiveRefreshIntervals = { 60, 30, 10 };

        public bool IsValid()
        {
            return MaxItems > 0 && MaxItems <= 100 &&
                   MaxFeeds > 0 && MaxFeeds <= 50 &&
                   FontSize >= 8 && FontSize <= 24 &&
                   TreeWidth >= 100 && TreeWidth <= 500 &&
                   ConnectionTimeout > 0 && ConnectionTimeout <= 300 &&
                   ImportantNotificationsRetentionHours >= 0 && ImportantNotificationsRetentionHours <= 24 * 30 &&
                   AvailableLiveRefreshIntervals.Contains(LiveRefreshIntervalSeconds) &&
                   ArticleWindowHeight >= 200 && ArticleWindowHeight <= 2000 &&
                   BrowserWindowHeight >= 200 && BrowserWindowHeight <= 2000 &&
                   ArchiveAutoCleanupDays >= 1 && ArchiveAutoCleanupDays <= 3650 &&
                   (!(FeedAllWindowPreferences?.WindowWidth.HasValue ?? false) || (FeedAllWindowPreferences.WindowWidth.Value >= 620 && FeedAllWindowPreferences.WindowWidth.Value <= 4000)) &&
                   (!(FeedAllWindowPreferences?.WindowHeight.HasValue ?? false) || (FeedAllWindowPreferences.WindowHeight.Value >= 480 && FeedAllWindowPreferences.WindowHeight.Value <= 4000)) &&
                   (!(FeedManagerWindowPreferences?.WindowWidth.HasValue ?? false) || (FeedManagerWindowPreferences.WindowWidth.Value >= 620 && FeedManagerWindowPreferences.WindowWidth.Value <= 4000)) &&
                   (!(FeedManagerWindowPreferences?.WindowHeight.HasValue ?? false) || (FeedManagerWindowPreferences.WindowHeight.Value >= 480 && FeedManagerWindowPreferences.WindowHeight.Value <= 4000));
        }

        public void ResetToDefaults()
        {
            DarkMode = false;
            AutoRefresh = false;
            LiveRefresh = false;
            MaxItems = 10;
            MaxFeeds = 10;
            KeywordFilter = string.Empty;
            FontSize = 12;
            TreeWidth = 150;
            RefreshInterval = TimeSpan.FromMinutes(1);
            AdBlockerEnabled = true;
            AdvertisementFilterEnabled = false;
            AdvertisementKeywords = new List<string>(DefaultAdvertisementKeywords);
            GroupFeedsByCategory = false;
            TreeViewExpandedStates = new Dictionary<string, bool>();
            SectionExpandedStates = new Dictionary<string, bool>();
            IsShowContentAlwaysOn = false;
            ArticleWindowHeight = 350;
            BrowserWindowHeight = 350;
            HasCustomHeights = false;
            ShowThumbnails = false;
            ArticlePreviewHeight = 400;
            EnableNotifications = false;
            ImportantKeywords = new List<string>();
            RecentImportantNotifications = new List<ImportantNotificationItem>();
            ImportantNotificationsRetentionHours = 24;
            Language = "en-US";
            LiveRefreshIntervalSeconds = 60;
            UseCompactArticleCards = false;
            AutoUpdateCheckEnabled = true;
            AutoUpdatePromptShown = false;

            // Reset category settings
            CategoryExpandedStates = new Dictionary<string, bool>();
            Categories = new List<string> { "Default" };
            ReadArticleLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            PinnedArticleLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ReadLaterArticleLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ArchivedArticleLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            PinnedArticleSnapshots = new List<FeedItem>();
            ReadLaterArticleSnapshots = new List<FeedItem>();
            ArchivedArticleSnapshots = new List<FeedItem>();
            ArticleLabels = new List<ArticleLabelDefinition>();
            SavedLabelColorDefinitions = new List<SavedLabelColorDefinition>();
            SavedLabelColors = new List<string>();
            ArticleLabelAssignments = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            ArticleNoteAssignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ArchiveViewPreferences = new ArchiveViewPreferences();
            FeedAllWindowPreferences = new FeedAllWindowPreferences();
            FeedManagerWindowPreferences = new FeedManagerWindowPreferences();
            ArchiveSavedViews = new List<ArchiveSavedView>();
            ArchiveAutoCleanupEnabled = false;
            ArchiveAutoCleanupDays = 90;

        }

        public AppSettings Clone()
        {
            return new AppSettings
            {
                DarkMode = this.DarkMode,
                AutoRefresh = this.AutoRefresh,
                LiveRefresh = this.LiveRefresh,
                MaxItems = this.MaxItems,
                MaxFeeds = this.MaxFeeds,
                KeywordFilter = this.KeywordFilter,
                FontSize = this.FontSize,
                TreeWidth = this.TreeWidth,
                RefreshInterval = this.RefreshInterval,
                AdBlockerEnabled = this.AdBlockerEnabled,
                AdvertisementFilterEnabled = this.AdvertisementFilterEnabled,
                AdvertisementKeywords = new List<string>(this.AdvertisementKeywords ?? new List<string>()),
                GroupFeedsByCategory = this.GroupFeedsByCategory,
                TreeViewExpandedStates = new Dictionary<string, bool>(this.TreeViewExpandedStates),
                SectionExpandedStates = new Dictionary<string, bool>(this.SectionExpandedStates),
                IsShowContentAlwaysOn = this.IsShowContentAlwaysOn,
                ArticleWindowHeight = this.ArticleWindowHeight,
                BrowserWindowHeight = this.BrowserWindowHeight,
                HasCustomHeights = this.HasCustomHeights,
                UserAgent = this.UserAgent,
                ConnectionTimeout = this.ConnectionTimeout,
                EnableLogging = this.EnableLogging,
                LogLevel = this.LogLevel,
                AutoSaveSettings = this.AutoSaveSettings,
                DefaultBrowser = this.DefaultBrowser,
                ShowThumbnails = this.ShowThumbnails,
                ArticlePreviewHeight = this.ArticlePreviewHeight,
                EnableNotifications = this.EnableNotifications,
                ImportantKeywords = new List<string>(this.ImportantKeywords ?? new List<string>()),
                RecentImportantNotifications = new List<ImportantNotificationItem>(this.RecentImportantNotifications ?? new List<ImportantNotificationItem>()),
                ImportantNotificationsRetentionHours = this.ImportantNotificationsRetentionHours,
                Language = this.Language,
                AutoRefreshIntervalMinutes = this.AutoRefreshIntervalMinutes,
                LiveRefreshIntervalSeconds = this.LiveRefreshIntervalSeconds,
                UseCompactArticleCards = this.UseCompactArticleCards,
                AutoUpdateCheckEnabled = this.AutoUpdateCheckEnabled,
                AutoUpdatePromptShown = this.AutoUpdatePromptShown,
                // Clone category settings
                CategoryExpandedStates = new Dictionary<string, bool>(this.CategoryExpandedStates),
                Categories = new List<string>(this.Categories),
                ReadArticleLinks = new HashSet<string>(this.ReadArticleLinks ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase),
                PinnedArticleLinks = new HashSet<string>(this.PinnedArticleLinks ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase),
                ReadLaterArticleLinks = new HashSet<string>(this.ReadLaterArticleLinks ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase),
                ArchivedArticleLinks = new HashSet<string>(this.ArchivedArticleLinks ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase),
                PinnedArticleSnapshots = new List<FeedItem>(this.PinnedArticleSnapshots ?? new List<FeedItem>()),
                ReadLaterArticleSnapshots = new List<FeedItem>(this.ReadLaterArticleSnapshots ?? new List<FeedItem>()),
                ArchivedArticleSnapshots = new List<FeedItem>(this.ArchivedArticleSnapshots ?? new List<FeedItem>()),
                ArticleLabels = (this.ArticleLabels ?? new List<ArticleLabelDefinition>()).Select(label => label?.Clone() ?? new ArticleLabelDefinition()).ToList(),
                SavedLabelColorDefinitions = (this.SavedLabelColorDefinitions ?? new List<SavedLabelColorDefinition>())
                    .Select(color => color?.Clone() ?? new SavedLabelColorDefinition())
                    .Where(color => !string.IsNullOrWhiteSpace(color.Name) && !string.IsNullOrWhiteSpace(color.ColorHex))
                    .ToList(),
                SavedLabelColors = (this.SavedLabelColors ?? new List<string>())
                    .Where(color => !string.IsNullOrWhiteSpace(color))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                ArticleLabelAssignments = (this.ArticleLabelAssignments ?? new Dictionary<string, List<string>>())
                    .ToDictionary(
                        entry => entry.Key,
                        entry => (entry.Value ?? new List<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                        StringComparer.OrdinalIgnoreCase),
                ArticleNoteAssignments = (this.ArticleNoteAssignments ?? new Dictionary<string, string>())
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
                    .ToDictionary(
                        entry => entry.Key,
                        entry => entry.Value ?? string.Empty,
                        StringComparer.OrdinalIgnoreCase),
                ArchiveViewPreferences = this.ArchiveViewPreferences?.Clone() ?? new ArchiveViewPreferences(),
                FeedAllWindowPreferences = this.FeedAllWindowPreferences?.Clone() ?? new FeedAllWindowPreferences(),
                FeedManagerWindowPreferences = this.FeedManagerWindowPreferences?.Clone() ?? new FeedManagerWindowPreferences(),
                ArchiveSavedViews = (this.ArchiveSavedViews ?? new List<ArchiveSavedView>()).Select(view => view?.Clone() ?? new ArchiveSavedView()).ToList(),
                ArchiveAutoCleanupEnabled = this.ArchiveAutoCleanupEnabled,
                ArchiveAutoCleanupDays = this.ArchiveAutoCleanupDays
            };
        }
    }
}