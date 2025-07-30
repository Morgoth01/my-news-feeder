using System;
using System.Collections.Generic;

namespace MyNewsFeeder.Models
{
    public class AppSettings
    {
        public bool DarkMode { get; set; } = false;
        public bool AutoRefresh { get; set; } = false;
        public int MaxItems { get; set; } = 10;
        public int MaxFeeds { get; set; } = 10;
        public string KeywordFilter { get; set; } = string.Empty;
        public int FontSize { get; set; } = 12;
        public double TreeWidth { get; set; } = 250.0;
        public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(1);
        public bool AdBlockerEnabled { get; set; } = true;
        public Dictionary<string, bool> TreeViewExpandedStates { get; set; } = new Dictionary<string, bool>();
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
        public string Language { get; set; } = "en-US";
        public int AutoRefreshIntervalMinutes { get; set; } = 10;

        // New category-related properties
        public Dictionary<string, bool> CategoryExpandedStates { get; set; } = new Dictionary<string, bool>();
        public List<string> Categories { get; set; } = new List<string> { "Default" };

        public static readonly int[] AvailableRefreshIntervals = { 5, 10, 15, 20, 30, 45, 60 };

        public bool IsValid()
        {
            return MaxItems > 0 && MaxItems <= 100 &&
                   MaxFeeds > 0 && MaxFeeds <= 50 &&
                   FontSize >= 8 && FontSize <= 24 &&
                   TreeWidth >= 100 && TreeWidth <= 500 &&
                   ConnectionTimeout > 0 && ConnectionTimeout <= 300 &&
                   ArticleWindowHeight >= 200 && ArticleWindowHeight <= 2000 &&
                   BrowserWindowHeight >= 200 && BrowserWindowHeight <= 2000;
        }

        public void ResetToDefaults()
        {
            DarkMode = false;
            AutoRefresh = false;
            MaxItems = 10;
            MaxFeeds = 10;
            KeywordFilter = string.Empty;
            FontSize = 12;
            TreeWidth = 150;
            RefreshInterval = TimeSpan.FromMinutes(1);
            AdBlockerEnabled = true;
            TreeViewExpandedStates = new Dictionary<string, bool>();
            IsShowContentAlwaysOn = false;
            ArticleWindowHeight = 350;
            BrowserWindowHeight = 350;
            HasCustomHeights = false;
            ShowThumbnails = false;
            ArticlePreviewHeight = 400;
            EnableNotifications = false;
            Language = "en-US";

            // Reset category settings
            CategoryExpandedStates = new Dictionary<string, bool>();
            Categories = new List<string> { "Default" };

            System.Diagnostics.Debug.WriteLine("Settings reset to defaults");
        }

        public AppSettings Clone()
        {
            return new AppSettings
            {
                DarkMode = this.DarkMode,
                AutoRefresh = this.AutoRefresh,
                MaxItems = this.MaxItems,
                MaxFeeds = this.MaxFeeds,
                KeywordFilter = this.KeywordFilter,
                FontSize = this.FontSize,
                TreeWidth = this.TreeWidth,
                RefreshInterval = this.RefreshInterval,
                AdBlockerEnabled = this.AdBlockerEnabled,
                TreeViewExpandedStates = new Dictionary<string, bool>(this.TreeViewExpandedStates),
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
                Language = this.Language,
                AutoRefreshIntervalMinutes = this.AutoRefreshIntervalMinutes,
                // Clone category settings
                CategoryExpandedStates = new Dictionary<string, bool>(this.CategoryExpandedStates),
                Categories = new List<string>(this.Categories)
            };
        }
    }
}