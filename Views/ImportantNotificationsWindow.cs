using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HtmlAgilityPack;
using System.Windows.Interop;
using MyNewsFeeder.Models;
using MyNewsFeeder.ViewModels;

namespace MyNewsFeeder.Views
{
    public partial class ImportantNotificationsWindow : Window
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

        private sealed class RetentionOption
        {
            public int Hours { get; init; }
            public string Label { get; init; } = string.Empty;
        }

        private static readonly IReadOnlyList<RetentionOption> RetentionOptions = new List<RetentionOption>
        {
            new RetentionOption { Hours = 24, Label = "24 hours" },
            new RetentionOption { Hours = 72, Label = "3 days" },
            new RetentionOption { Hours = 168, Label = "7 days" },
            new RetentionOption { Hours = 0, Label = "Never" }
        };

        private readonly ObservableCollection<ImportantNotificationItem> _items = new ObservableCollection<ImportantNotificationItem>();
        private readonly Action _clearAllAction;
        private readonly Action<ImportantNotificationItem> _removeItemAction;
        private readonly Action<int> _updateRetentionHoursAction;
        private readonly int _maxStoredItems;
        private bool _isUpdatingRetentionSelection;

        public ImportantNotificationsWindow(
            IEnumerable<ImportantNotificationItem> items,
            Action clearAllAction = null,
            Action<ImportantNotificationItem> removeItemAction = null,
            int currentRetentionHours = 24,
            Action<int> updateRetentionHoursAction = null,
            int maxStoredItems = 200)
        {
            InitializeComponent();
            SourceInitialized += (_, __) => EnableDarkTitleBar();

            _clearAllAction = clearAllAction;
            _removeItemAction = removeItemAction;
            _updateRetentionHoursAction = updateRetentionHoursAction;
            _maxStoredItems = Math.Max(1, maxStoredItems);

            NotificationsListView.ItemsSource = _items;
            SizeChanged += ImportantNotificationsWindow_SizeChanged;
            NotificationsListView.SizeChanged += NotificationsListView_SizeChanged;
            InitializeRetentionControls(currentRetentionHours);
            SetItems(items);
            AdjustTitleColumnWidth();
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

        public void SetRetentionSelection(int retentionHours)
        {
            if (RetentionComboBox == null)
            {
                return;
            }

            var normalized = NormalizeRetentionHours(retentionHours);
            var option = RetentionOptions.FirstOrDefault(x => x.Hours == normalized) ??
                         RetentionOptions.FirstOrDefault(x => x.Hours == 24);
            if (option == null)
            {
                return;
            }

            _isUpdatingRetentionSelection = true;
            RetentionComboBox.SelectedItem = option;
            _isUpdatingRetentionSelection = false;
        }

        public void SetItems(IEnumerable<ImportantNotificationItem> items)
        {
            _items.Clear();
            if (items != null)
            {
                foreach (var item in items)
                {
                    _items.Add(item);
                }
            }

            UpdateEmptyState();
            if (_items.Count > 0)
            {
                NotificationsListView.SelectedIndex = 0;
            }

            AdjustTitleColumnWidth();
        }

        private async void NotificationsListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (IsInRemoveArea(e))
            {
                e.Handled = true;
                return;
            }

            await OpenSelectedItemInWindowAsync();
        }

        private async void OpenArticleButton_Click(object sender, RoutedEventArgs e)
        {
            await OpenSelectedItemInWindowAsync();
        }

        private void CopyLinkButton_Click(object sender, RoutedEventArgs e)
        {
            if (NotificationsListView.SelectedItem is not ImportantNotificationItem selected ||
                string.IsNullOrWhiteSpace(selected.Link))
            {
                return;
            }

            try
            {
                Clipboard.SetText(selected.Link);
            }
            catch
            {
                // Ignore clipboard access failures.
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ClearAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_items.Count == 0)
            {
                return;
            }

            _items.Clear();
            _clearAllAction?.Invoke();
            UpdateEmptyState();
        }

        private void RemoveItemButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element ||
                element.DataContext is not ImportantNotificationItem item)
            {
                return;
            }

            RemoveNotificationItem(item);
        }

        private bool IsInRemoveArea(MouseButtonEventArgs e)
        {
            if (e?.OriginalSource is not DependencyObject source)
            {
                return false;
            }

            // Direct clicks on the remove button must never open an article window.
            if (FindAncestor<Button>(source) != null)
            {
                return true;
            }

            var row = FindAncestor<ListViewItem>(source);
            if (row == null)
            {
                return false;
            }

            var point = e.GetPosition(row);
            var removeWidth = Math.Max(0, RemoveColumn?.ActualWidth ?? 48.0);
            const double guardPadding = 8.0;
            return point.X <= (removeWidth + guardPadding);
        }

        private void RetentionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingRetentionSelection)
            {
                return;
            }

            if (RetentionComboBox?.SelectedItem is not RetentionOption option)
            {
                return;
            }

            _updateRetentionHoursAction?.Invoke(option.Hours);
        }

        private void ReadLaterMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem ||
                menuItem.CommandParameter is not ImportantNotificationItem item)
            {
                return;
            }

            ExecuteMainCommandForNotification(item, vm => vm.ReadLaterArticleCommand);
        }

        private void PinMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem ||
                menuItem.CommandParameter is not ImportantNotificationItem item)
            {
                return;
            }

            ExecuteMainCommandForNotification(item, vm => vm.PinArticleCommand);
        }

        private void ArchiveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem ||
                menuItem.CommandParameter is not ImportantNotificationItem item)
            {
                return;
            }

            ExecuteMainCommandForNotification(item, vm => vm.ArchiveArticleCommand);
        }

        private void ClearMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem ||
                menuItem.CommandParameter is not ImportantNotificationItem item)
            {
                return;
            }

            RemoveNotificationItem(item);
        }

        private Task OpenSelectedItemInWindowAsync()
        {
            if (NotificationsListView.SelectedItem is not ImportantNotificationItem selected ||
                string.IsNullOrWhiteSpace(selected.Link))
            {
                return Task.CompletedTask;
            }

            try
            {
                var mainViewModel = ResolveMainViewModel();
                var darkMode = mainViewModel?.DarkMode ?? false;
                var articleTitle = string.IsNullOrWhiteSpace(selected.Title) ? "Article" : selected.Title.Trim();
                var articleLink = selected.Link.Trim();
                var articleHtml = BuildNotificationArticleHtml(selected, darkMode);
                if (mainViewModel != null)
                {
                    var feedItem = FindFeedItemByLink(mainViewModel, articleLink);
                    if (feedItem != null)
                    {
                        articleTitle = string.IsNullOrWhiteSpace(feedItem.Title) ? articleTitle : feedItem.Title;
                        articleHtml = BuildFeedItemSummaryHtml(feedItem, darkMode);
                        articleLink = string.IsNullOrWhiteSpace(feedItem.Link) ? articleLink : feedItem.Link.Trim();
                    }
                }

                var articleWindow = new ArticleWindow(
                    articleTitle,
                    articleHtml,
                    articleLink,
                    darkMode,
                    mainViewModel?.CreateBrowserSession(),
                    mainViewModel?.AdBlockerEnabled == true,
                    mainViewModel != null ? new Action<string>(mainViewModel.PromptOpenExternalLink) : null)
                {
                    Owner = Application.Current?.MainWindow
                };
                articleWindow.Show();
            }
            catch
            {
                // Ignore article window launch errors.
            }

            return Task.CompletedTask;
        }

        private static MainViewModel ResolveMainViewModel()
        {
            if (Application.Current?.MainWindow?.DataContext is MainViewModel vm)
            {
                return vm;
            }

            return null;
        }

        private static FeedItem FindFeedItemByLink(MainViewModel vm, string link)
        {
            if (vm?.ArticleSections == null || string.IsNullOrWhiteSpace(link))
            {
                return null;
            }

            var normalized = link.Trim();
            foreach (var section in vm.ArticleSections)
            {
                if (section?.Items == null)
                {
                    continue;
                }

                foreach (var child in section.Items)
                {
                    if (child is CategoryGroupViewModel category)
                    {
                        foreach (var feed in category.Feeds ?? Enumerable.Empty<FeedGroupViewModel>())
                        {
                            var match = feed.Items?.FirstOrDefault(item =>
                                !string.IsNullOrWhiteSpace(item?.Link) &&
                                string.Equals(item.Link.Trim(), normalized, StringComparison.OrdinalIgnoreCase));
                            if (match != null)
                            {
                                return match;
                            }
                        }
                    }
                    else if (child is FeedGroupViewModel feedGroup)
                    {
                        var match = feedGroup.Items?.FirstOrDefault(item =>
                            !string.IsNullOrWhiteSpace(item?.Link) &&
                            string.Equals(item.Link.Trim(), normalized, StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                        {
                            return match;
                        }
                    }
                }
            }

            return null;
        }

        private void ExecuteMainCommandForNotification(
            ImportantNotificationItem item,
            Func<MainViewModel, System.Windows.Input.ICommand> commandSelector)
        {
            if (item == null || commandSelector == null)
            {
                return;
            }

            var vm = ResolveMainViewModel();
            var command = commandSelector(vm);
            if (vm == null || command == null)
            {
                return;
            }

            var feedItem = FindFeedItemByLink(vm, item.Link) ?? BuildFeedItemFromNotification(item);
            if (feedItem == null)
            {
                return;
            }

            if (command.CanExecute(feedItem))
            {
                command.Execute(feedItem);
            }
        }

        private static FeedItem BuildFeedItemFromNotification(ImportantNotificationItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Link))
            {
                return null;
            }

            return new FeedItem
            {
                Link = item.Link.Trim(),
                Title = item.Title?.Trim() ?? "Article",
                FeedName = string.IsNullOrWhiteSpace(item.FeedName) ? "Feed" : item.FeedName.Trim(),
                PublicationDate = item.PublicationDate,
                Description = string.Empty
            };
        }

        private static T FindAncestor<T>(DependencyObject child) where T : DependencyObject
        {
            var current = child;
            while (current != null)
            {
                if (current is T match)
                {
                    return match;
                }

                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private void RemoveNotificationItem(ImportantNotificationItem item)
        {
            if (item == null)
            {
                return;
            }

            var removed = _items.Remove(item);
            if (!removed)
            {
                return;
            }

            _removeItemAction?.Invoke(item);
            if (_items.Count > 0 && NotificationsListView.SelectedIndex < 0)
            {
                NotificationsListView.SelectedIndex = 0;
            }
            UpdateEmptyState();
        }

        private static string BuildNotificationArticleHtml(ImportantNotificationItem item, bool darkMode)
        {
            var fallbackFeedItem = new FeedItem
            {
                Title = item?.Title ?? "Article",
                Link = item?.Link ?? string.Empty,
                FeedName = item?.FeedName ?? "Feed",
                PublicationDate = item?.PublicationDate ?? DateTime.MinValue,
                Description = "Open Reader Mode for full text extraction."
            };

            return BuildFeedItemSummaryHtml(fallbackFeedItem, darkMode);
        }

        private static string BuildFeedItemSummaryHtml(FeedItem item, bool darkMode)
        {
            var backgroundColor = darkMode ? "#121212" : "#ffffff";
            var textColor = darkMode ? "#e0e0e0" : "#333333";
            var titleColor = darkMode ? "#90caf9" : "#673ab7";
            var metaColor = darkMode ? "#b0b0b0" : "#666666";
            var borderColor = darkMode ? "#90caf9" : "#673ab7";

            var title = System.Web.HttpUtility.HtmlEncode(item?.Title ?? "Article");
            var link = System.Web.HttpUtility.HtmlEncode(item?.Link ?? string.Empty);
            var feed = System.Web.HttpUtility.HtmlEncode(item?.FeedName ?? "Feed");
            var published = item?.PublicationDate == default
                ? string.Empty
                : item.PublicationDate.ToString("yyyy-MM-dd HH:mm");
            var encodedPublished = System.Web.HttpUtility.HtmlEncode(published);
            var description = item?.Description ?? string.Empty;

            var bodyContent = LooksLikeHtml(description)
                ? NormalizeSummaryBodyHtml(description)
                : $"<p>{System.Web.HttpUtility.HtmlEncode(description)}</p>";

            if (string.IsNullOrWhiteSpace(description))
            {
                bodyContent = "<p>Open Reader Mode for full text extraction.</p>";
            }

            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1'>
    <style>
        body {{
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            margin: 14px;
            line-height: 1.5;
            color: {textColor};
            background: {backgroundColor};
        }}
        .article-header {{
            border-bottom: 2px solid {borderColor};
            margin-bottom: 12px;
            padding-bottom: 8px;
        }}
        .article-title {{
            font-size: 22px;
            font-weight: 700;
            color: {titleColor};
            margin-bottom: 8px;
        }}
        .article-meta {{
            color: {metaColor};
            font-size: 12px;
        }}
        .article-content {{
            font-size: 15px;
            line-height: 1.5;
            word-wrap: break-word;
            overflow-wrap: anywhere;
        }}
        .article-content img {{
            max-width: min(100%, 520px) !important;
            width: auto !important;
            height: auto !important;
            max-height: 320px !important;
            margin: 12px auto;
            display: block;
            border-radius: 6px;
            object-fit: contain;
            float: none !important;
            clear: both !important;
        }}
        .article-content p,
        .article-content div,
        .article-content figure {{
            clear: both;
        }}
    </style>
</head>
<body>
    <div class='article-header'>
        <div class='article-title'>{title}</div>
        <div class='article-meta'>{feed}</div>
        <div class='article-meta'>{encodedPublished}</div>
        <div class='article-meta'>{link}</div>
    </div>
    <div class='article-content'>{bodyContent}</div>
</body>
</html>";
        }

        private static bool LooksLikeHtml(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return value.IndexOf('<') >= 0 && value.IndexOf('>') >= 0;
        }

        private static string NormalizeSummaryBodyHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return html;
            }

            try
            {
                var document = new HtmlDocument();
                document.LoadHtml(html);

                var images = document.DocumentNode.SelectNodes("//img");
                if (images != null)
                {
                    foreach (var image in images)
                    {
                        image.Attributes.Remove("style");
                        image.Attributes.Remove("width");
                        image.Attributes.Remove("height");
                        image.Attributes.Remove("align");
                    }
                }

                return document.DocumentNode.InnerHtml;
            }
            catch
            {
                return html;
            }
        }

        private void ImportantNotificationsWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            AdjustTitleColumnWidth();
        }

        private void NotificationsListView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            AdjustTitleColumnWidth();
        }

        private void AdjustTitleColumnWidth()
        {
            if (NotificationsListView == null || TitleColumn == null)
            {
                return;
            }

            const double reservedWidth = 150 + 150 + 180 + 48 + 70;
            var available = NotificationsListView.ActualWidth - reservedWidth;
            TitleColumn.Width = Math.Max(260, available);
        }

        private void UpdateEmptyState()
        {
            EmptyStateText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void InitializeRetentionControls(int currentRetentionHours)
        {
            if (RetentionComboBox != null)
            {
                _isUpdatingRetentionSelection = true;
                RetentionComboBox.ItemsSource = RetentionOptions;
                _isUpdatingRetentionSelection = false;
                SetRetentionSelection(currentRetentionHours);
            }

            if (StorageHintText != null)
            {
                StorageHintText.Text = $"List stores up to {_maxStoredItems} entries.";
            }
        }

        private static int NormalizeRetentionHours(int hours)
        {
            if (hours == 0 || hours == 24 || hours == 72 || hours == 168)
            {
                return hours;
            }

            if (hours < 0)
            {
                return 24;
            }

            if (hours <= 24)
            {
                return 24;
            }

            if (hours <= 72)
            {
                return 72;
            }

            return 168;
        }
    }
}