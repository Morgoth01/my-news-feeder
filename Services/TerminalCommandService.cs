using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MyNewsFeeder.Models;
using MyNewsFeeder.ViewModels;

namespace MyNewsFeeder.Services
{
    public class TerminalCommandService
    {
        private readonly MainViewModel _mainViewModel;
        private readonly ArticleReaderService _articleReaderService = new ArticleReaderService();
        private readonly Dictionary<string, ReaderArticleContent> _readerContentCache = new Dictionary<string, ReaderArticleContent>(StringComparer.OrdinalIgnoreCase);
        private List<FeedItem> _articleContext = new List<FeedItem>();
        private List<FeedGroupViewModel> _feedContext = new List<FeedGroupViewModel>();

        public TerminalCommandService(MainViewModel mainViewModel)
        {
            _mainViewModel = mainViewModel ?? throw new ArgumentNullException(nameof(mainViewModel));
            RefreshArticleContextFromMain();
        }

        public async Task<TerminalCommandResult> ExecuteAsync(string input)
        {
            var args = Tokenize(input);
            if (args.Count == 0)
            {
                return TerminalCommandResult.Text(string.Empty);
            }

            var command = args[0].ToLowerInvariant();
            if (command == "help" || command == "?")
            {
                return TerminalCommandResult.Text(BuildHelp());
            }

            if (command == "clear" || command == "cls")
            {
                return new TerminalCommandResult { ClearScreen = true };
            }

            if (command == "close" || command == "exit" || command == "quit")
            {
                return new TerminalCommandResult { CloseRequested = true, Output = "Closing terminal." };
            }

            if (command == "browse")
            {
                return new TerminalCommandResult { BrowseRequested = true, Output = "Opening browse mode." };
            }

            if (command == "startup")
            {
                return ExecuteStartup(args);
            }

            if (command == "refresh")
            {
                return ExecuteRefresh(args);
            }

            if (command == "list")
            {
                return ExecuteList(args);
            }

            if (command == "feed")
            {
                return ExecuteFeed(args);
            }

            if (command == "open")
            {
                return await ExecuteOpenAsync(args, markUnread: false, readerMode: false);
            }

            if (command == "read" || command == "reader")
            {
                return await ExecuteOpenAsync(args, markUnread: false, readerMode: true);
            }

            if (command == "unread")
            {
                return await ExecuteOpenAsync(args, markUnread: true, readerMode: false);
            }

            if (command == "search")
            {
                return ExecuteSearch(args);
            }

            return TerminalCommandResult.Text($"Unknown command: {args[0]}\nType 'help' for available commands.");
        }

        private TerminalCommandResult ExecuteStartup(IReadOnlyList<string> args)
        {
            if (args.Count < 2)
            {
                var currentMode = _mainViewModel.StartInTerminal ? "terminal" : "GUI";
                return TerminalCommandResult.Text(
                    $"Current startup mode: {currentMode}\nUsage: startup terminal | startup gui");
            }

            var mode = args[1].ToLowerInvariant();
            if (mode == "terminal")
            {
                _mainViewModel.SetStartInTerminal(true);
                return TerminalCommandResult.Text("Startup mode set to terminal. Next app start opens the terminal first.");
            }

            if (mode == "gui" || mode == "app" || mode == "window")
            {
                _mainViewModel.SetStartInTerminal(false);
                return TerminalCommandResult.Text("Startup mode set to GUI. Next app start opens the main window first.");
            }

            return TerminalCommandResult.Text("Usage: startup terminal | startup gui");
        }

        private TerminalCommandResult ExecuteRefresh(IReadOnlyList<string> args)
        {
            if (args.Count < 2)
            {
                if (_mainViewModel.RefreshCommand?.CanExecute(null) == true)
                {
                    _mainViewModel.RefreshCommand.Execute(null);
                    return TerminalCommandResult.Text("Refresh started.");
                }

                return TerminalCommandResult.Text("Refresh is not available right now.");
            }

            var action = args[1].ToLowerInvariant();
            if (action == "status")
            {
                return TerminalCommandResult.Text(BuildRefreshStatus());
            }

            if (action == "help" || action == "?")
            {
                return TerminalCommandResult.Text(BuildRefreshHelp());
            }

            if (action == "auto")
            {
                if (args.Count < 3)
                {
                    return TerminalCommandResult.Text($"Auto refresh is {FormatOnOff(_mainViewModel.PendingAutoRefresh)}.\nUsage: refresh auto on | refresh auto off");
                }

                if (!TryParseOnOff(args[2], out var enabled))
                {
                    return TerminalCommandResult.Text("Usage: refresh auto on | refresh auto off", isError: true);
                }

                _mainViewModel.PendingAutoRefresh = enabled;
                SaveSettingsFromTerminal();
                return TerminalCommandResult.Text($"Auto refresh {FormatEnabled(enabled)}.");
            }

            if (action == "interval")
            {
                if (args.Count < 3 || !int.TryParse(args[2], out var minutes) || minutes < 1)
                {
                    return TerminalCommandResult.Text("Usage: refresh interval <minutes>\nExample: refresh interval 10", isError: true);
                }

                _mainViewModel.PendingLiveRefresh = false;
                _mainViewModel.PendingAutoRefreshIntervalMinutes = minutes;
                SaveSettingsFromTerminal();
                return TerminalCommandResult.Text($"Auto refresh interval set to {minutes} minute{(minutes == 1 ? string.Empty : "s")}.");
            }

            if (action == "live")
            {
                return ExecuteRefreshLive(args);
            }

            return TerminalCommandResult.Text(BuildRefreshHelp(), isError: true);
        }

        private TerminalCommandResult ExecuteRefreshLive(IReadOnlyList<string> args)
        {
            if (args.Count < 3)
            {
                return TerminalCommandResult.Text($"Live refresh is {FormatOnOff(_mainViewModel.PendingLiveRefresh)}.\nUsage: refresh live on | refresh live off | refresh live interval <seconds>");
            }

            var action = args[2].ToLowerInvariant();
            if (action == "interval")
            {
                if (args.Count < 4 || !int.TryParse(args[3], out var seconds) || seconds < 1)
                {
                    return TerminalCommandResult.Text("Usage: refresh live interval <seconds>\nExample: refresh live interval 60", isError: true);
                }

                _mainViewModel.PendingLiveRefresh = true;
                _mainViewModel.PendingLiveRefreshIntervalSeconds = seconds;
                SaveSettingsFromTerminal();
                return TerminalCommandResult.Text($"Live refresh interval set to {_mainViewModel.PendingLiveRefreshIntervalSeconds} second{(_mainViewModel.PendingLiveRefreshIntervalSeconds == 1 ? string.Empty : "s")}.");
            }

            if (!TryParseOnOff(action, out var enabled))
            {
                return TerminalCommandResult.Text("Usage: refresh live on | refresh live off | refresh live interval <seconds>", isError: true);
            }

            _mainViewModel.PendingLiveRefresh = enabled;
            SaveSettingsFromTerminal();
            return TerminalCommandResult.Text($"Live refresh {FormatEnabled(enabled)}.");
        }

        private void SaveSettingsFromTerminal()
        {
            if (_mainViewModel.SaveSettingsCommand?.CanExecute(null) == true)
            {
                _mainViewModel.SaveSettingsCommand.Execute(null);
            }
        }

        private string BuildRefreshStatus()
        {
            var mode = _mainViewModel.PendingLiveRefresh ? "live" : "interval";
            var interval = _mainViewModel.PendingLiveRefresh
                ? $"{_mainViewModel.PendingLiveRefreshIntervalSeconds} second{(_mainViewModel.PendingLiveRefreshIntervalSeconds == 1 ? string.Empty : "s")}"
                : $"{_mainViewModel.PendingAutoRefreshIntervalMinutes} minute{(_mainViewModel.PendingAutoRefreshIntervalMinutes == 1 ? string.Empty : "s")}";

            return $"Auto refresh: {FormatOnOff(_mainViewModel.PendingAutoRefresh)}\nMode: {mode}\nInterval: {interval}";
        }

        private static string BuildRefreshHelp()
        {
            return @"Refresh commands:
  refresh                         start feed refresh now
  refresh status                  show auto-refresh settings
  refresh auto on|off             enable or disable auto refresh
  refresh interval <minutes>      use interval mode and set minutes
  refresh live on|off             enable or disable live refresh mode
  refresh live interval <seconds> use live mode and set seconds";
        }

        private static bool TryParseOnOff(string value, out bool enabled)
        {
            enabled = false;
            if (string.Equals(value, "on", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "enable", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "enabled", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
            {
                enabled = true;
                return true;
            }

            if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "disable", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "disabled", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static string FormatOnOff(bool value)
        {
            return value ? "on" : "off";
        }

        private static string FormatEnabled(bool value)
        {
            return value ? "enabled" : "disabled";
        }

        private TerminalCommandResult ExecuteList(IReadOnlyList<string> args)
        {
            if (args.Count < 2)
            {
                return TerminalCommandResult.Text("Usage: list feeds | list articles | list unread");
            }

            var target = args[1].ToLowerInvariant();
            if (target == "feeds")
            {
                return TerminalCommandResult.Text(BuildFeedList());
            }

            if (target == "articles")
            {
                RefreshArticleContextFromMain();
                return TerminalCommandResult.Text(BuildArticleList(_articleContext, "Articles"));
            }

            if (target == "unread")
            {
                RefreshArticleContextFromMain();
                var unread = _articleContext.Where(item => item?.IsUnread == true).ToList();
                _articleContext = unread;
                return TerminalCommandResult.Text(BuildArticleList(_articleContext, "Unread articles"));
            }

            return TerminalCommandResult.Text("Usage: list feeds | list articles | list unread");
        }

        private TerminalCommandResult ExecuteFeed(IReadOnlyList<string> args)
        {
            if (args.Count < 2)
            {
                return TerminalCommandResult.Text("Usage: feed <number|name>");
            }

            var query = string.Join(" ", args.Skip(1));
            var feeds = EnsureFeedContext();
            FeedGroupViewModel feed = null;
            if (int.TryParse(args[1], out var feedIndex))
            {
                if (feedIndex >= 1 && feedIndex <= feeds.Count)
                {
                    feed = feeds[feedIndex - 1];
                }
                else
                {
                    return TerminalCommandResult.Text($"Feed number out of range. Current range: 1-{feeds.Count}.");
                }
            }

            feed ??= feeds.FirstOrDefault(item => string.Equals(item.Name, query, StringComparison.OrdinalIgnoreCase))
                ?? feeds.FirstOrDefault(item => item.Name?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);

            if (feed == null)
            {
                return TerminalCommandResult.Text($"Feed not found: {query}\nUse 'list feeds' to see numbered feeds.");
            }

            if (_mainViewModel.SelectMainFeedCommand?.CanExecute(feed) == true)
            {
                _mainViewModel.SelectMainFeedCommand.Execute(feed);
            }

            RefreshArticleContextFromMain();
            return TerminalCommandResult.Text($"Feed selected: {feed.Name}\n\n{BuildArticleList(_articleContext, "Articles")}");
        }

        private async Task<TerminalCommandResult> ExecuteOpenAsync(IReadOnlyList<string> args, bool markUnread, bool readerMode)
        {
            if (args.Count < 2 || !int.TryParse(args[1], out var index))
            {
                if (markUnread)
                {
                    return TerminalCommandResult.Text("Usage: unread <number>");
                }

                return TerminalCommandResult.Text(readerMode ? "Usage: read <number>" : "Usage: open <number>");
            }

            if (index < 1 || index > _articleContext.Count)
            {
                return TerminalCommandResult.Text($"Article number out of range. Current range: 1-{_articleContext.Count}.");
            }

            var item = _articleContext[index - 1];
            if (item == null)
            {
                return TerminalCommandResult.Text("Article is not available.");
            }

            if (markUnread)
            {
                if (_mainViewModel.MarkUnreadCommand?.CanExecute(item) == true)
                {
                    _mainViewModel.MarkUnreadCommand.Execute(item);
                }

                return TerminalCommandResult.Text($"Marked unread: {item.Title}");
            }

            _mainViewModel.OnArticleSelected(item, enableAutoScroll: false, preserveOpenWebViews: true);
            var body = readerMode
                ? await LoadReaderTextAsync(item)
                : _mainViewModel.SelectedArticleText;

            if (string.IsNullOrWhiteSpace(body))
            {
                body = StripAndCollapse(item.Description);
            }

            return TerminalCommandResult.Text(BuildArticleOutput(index, item, body, readerMode));
        }

        private TerminalCommandResult ExecuteSearch(IReadOnlyList<string> args)
        {
            if (args.Count < 2)
            {
                return TerminalCommandResult.Text("Usage: search <text>");
            }

            var query = string.Join(" ", args.Skip(1));
            var matches = GetAllArticles()
                .Where(item =>
                    Contains(item.Title, query) ||
                    Contains(item.Description, query) ||
                    Contains(item.FeedName, query))
                .Take(50)
                .ToList();

            _articleContext = matches;
            return TerminalCommandResult.Text(BuildArticleList(_articleContext, $"Search: {query}"));
        }

        private string BuildHelp()
        {
            return @"My News Feeder Terminal

Terminal commands:
  General:
    help                 show this help
    clear                clear terminal output
    close                close this terminal window
    theme [name]         change color theme (default, crt, amber, dos, matrix, solarized, dracula, paper)
    bootanim [mode]      boot animation on, off, or preview
    startup terminal     start future app launches in terminal
    startup gui          start future app launches in GUI
    refresh status       show auto-refresh settings
    refresh auto on/off  enable or disable auto refresh
    refresh interval <m> set interval refresh minutes
    refresh live on/off  enable or disable live refresh mode

  Feeds:
    list feeds           list available feeds with numbers
    list articles        list articles in the current app selection
    list unread          list unread articles in the current selection
    feed <number|name>   select a feed and list its articles
    refresh              start feed refresh
    browse               open the 3-pane browse mode

  Articles:
    open <number>        open an article summary from the last article list
    read <number>        load Reader Mode text for an article
    reader <number>      same as read
    unread <number>      mark an article unread
    search <text>        search loaded articles

Browse mode shortcuts:
  Navigation:
    Up / Down            previous / next article
    PageUp / PageDown    jump through articles
    Home / End           first / last article
    Left / Right         previous / next feed
    Ctrl+Home / Ctrl+End first / last feed

  Article:
    Enter                load article in Reader
    O                    open article in browser
    Space                toggle read/unread
    M                    mark article read
    U                    mark article unread
    Ctrl+L               copy article link

  Other:
    R                    refresh current feed
    ? or F1              show browse help
    Esc or q             back to command mode

Examples:
  list feeds
  feed 3
  feed ""ExampleRSS""
  theme matrix
  bootanim preview
  browse";
        }

        private string BuildFeedList()
        {
            var feeds = EnsureFeedContext();
            if (feeds.Count == 0)
            {
                return "No feeds loaded yet. Try 'refresh'.";
            }

            var builder = new StringBuilder();
            builder.AppendLine("Feeds");
            builder.AppendLine("-----");
            for (var i = 0; i < feeds.Count; i++)
            {
                var feed = feeds[i];
                builder.AppendLine($"{i + 1,3}. {Truncate(feed.Name, 36),-36} {feed.ItemCount,3} items  {feed.UnreadCount,3} unread  [{feed.Category}]");
            }

            builder.AppendLine();
            builder.AppendLine("Select feed with: feed <number>");
            return builder.ToString().TrimEnd();
        }

        private string BuildArticleList(IReadOnlyList<FeedItem> items, string title)
        {
            if (items == null || items.Count == 0)
            {
                return $"{title}\n{new string('-', title.Length)}\nNo articles loaded.";
            }

            var builder = new StringBuilder();
            builder.AppendLine(title);
            builder.AppendLine(new string('-', title.Length));
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var state = item.IsUnread ? "*" : " ";
                builder.AppendLine($"{i + 1,3}. [{state}] {Truncate(item.Title, 92)}");
                builder.AppendLine($"     {item.PublicationDate:yyyy-MM-dd HH:mm}  {item.FeedName}");
            }

            return builder.ToString().TrimEnd();
        }

        private async Task<string> LoadReaderTextAsync(FeedItem item)
        {
            if (string.IsNullOrWhiteSpace(item?.Link))
            {
                return string.Empty;
            }

            try
            {
                if (!_readerContentCache.TryGetValue(item.Link, out var readerContent))
                {
                    readerContent = await _articleReaderService.ExtractAsync(item.Link);
                    if (readerContent != null)
                    {
                        _readerContentCache[item.Link] = readerContent;
                    }
                }

                if (readerContent == null)
                {
                    return string.Empty;
                }

                if (!string.IsNullOrWhiteSpace(readerContent.PlainText))
                {
                    return readerContent.PlainText;
                }

                return StripAndCollapse(readerContent.HtmlContent);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static string BuildArticleOutput(int index, FeedItem item, string body, bool readerMode)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"[{index}] {item.Title}");
            builder.AppendLine($"Feed: {item.FeedName}");
            builder.AppendLine($"Date: {item.PublicationDate:yyyy-MM-dd HH:mm}");
            builder.AppendLine(readerMode ? "Mode: Reader" : "Mode: Feed summary");
            if (!string.IsNullOrWhiteSpace(item.Link))
            {
                builder.AppendLine($"Link: {item.Link}");
            }

            builder.AppendLine();
            builder.AppendLine(string.IsNullOrWhiteSpace(body) ? "(No article text available.)" : body);
            return builder.ToString().TrimEnd();
        }

        private void RefreshArticleContextFromMain()
        {
            _articleContext = (_mainViewModel.CurrentArticleItems ?? Enumerable.Empty<FeedItem>())
                .Where(item => item != null)
                .ToList();
        }

        private IEnumerable<FeedGroupViewModel> GetFeeds()
        {
            return (_mainViewModel.CategoryGroups ?? Enumerable.Empty<CategoryGroupViewModel>())
                .SelectMany(category => category.Feeds ?? Enumerable.Empty<FeedGroupViewModel>())
                .Where(feed => feed != null)
                .OrderBy(feed => feed.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(feed => feed.Name, StringComparer.OrdinalIgnoreCase);
        }

        private List<FeedGroupViewModel> EnsureFeedContext()
        {
            _feedContext = GetFeeds().ToList();
            return _feedContext;
        }

        private IEnumerable<FeedItem> GetAllArticles()
        {
            return GetFeeds()
                .SelectMany(feed => feed.Items ?? Enumerable.Empty<FeedItem>())
                .Where(item => item != null)
                .GroupBy(item => item.Link ?? item.Title ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderByDescending(item => item.PublicationDate);
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   !string.IsNullOrWhiteSpace(query) &&
                   value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string StripAndCollapse(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var withoutTags = Regex.Replace(value, "<.*?>", " ");
            var decoded = WebUtility.HtmlDecode(withoutTags);
            return Regex.Replace(decoded, "\\s+", " ").Trim();
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "(untitled)";
            }

            var normalized = Regex.Replace(value, "\\s+", " ").Trim();
            return normalized.Length <= maxLength ? normalized : normalized.Substring(0, maxLength - 3) + "...";
        }

        private static List<string> Tokenize(string input)
        {
            var matches = Regex.Matches(input ?? string.Empty, "\"([^\"]*)\"|\\S+");
            return matches
                .Cast<Match>()
                .Select(match => match.Groups[1].Success ? match.Groups[1].Value : match.Value)
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .ToList();
        }
    }
}