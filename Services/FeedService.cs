using System.Linq;
using System.Net;
using System.Net.Http;
using System.ServiceModel.Syndication;
using System.Threading.Tasks;
using System.Xml;
using MyNewsFeeder.Models;
using System.Net.Sockets;
using System.Collections.Generic;
using System;
using System.Threading;

namespace MyNewsFeeder.Services
{
    public class FeedService
    {
        private readonly HttpClient _httpClient;
        private readonly List<string> _lastBlockedFeeds = new List<string>();
        private static readonly string[] AllowedSchemes =
        {
            Uri.UriSchemeHttps
        };
        private const int MaxConcurrentFeedRequests = 12;

        private sealed class FeedFetchResult
        {
            public int Index { get; init; }
            public List<FeedItem> Items { get; init; } = new List<FeedItem>();
            public string BlockedFeedName { get; init; }
        }

        public IReadOnlyList<string> LastBlockedFeeds => _lastBlockedFeeds.AsReadOnly();

        public FeedService()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,
                CheckCertificateRevocationList = true
            };

            _httpClient = new HttpClient(handler, disposeHandler: true);
            _httpClient.Timeout = TimeSpan.FromSeconds(12);
            var version = System.Reflection.Assembly
            .GetEntryAssembly()?
            .GetName()?
            .Version?
            .ToString() ?? "1.0";
            _httpClient.DefaultRequestHeaders
                       .UserAgent.ParseAdd($"MyNewsFeeder/{version}");
        }

        public async Task<string> TryDetectFeedTitleAsync(string url)
        {
            try
            {
                if (!TryNormalizeFeedUrl(url, out var normalizedUrl))
                {
                    return null;
                }

                using var response = await _httpClient.GetAsync(normalizedUrl);
                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync();
                using var xmlReader = XmlReader.Create(stream);
                var syndicationFeed = SyndicationFeed.Load(xmlReader);
                var title = syndicationFeed?.Title?.Text;
                return string.IsNullOrWhiteSpace(title) ? null : title.Trim();
            }
            catch
            {
                return null;
            }
        }

        public async Task<List<FeedItem>> FetchArticlesAsync(List<Feed> feeds, string keywordFilter, int maxItems, IReadOnlyCollection<string> advertisementKeywords = null)
        {
            var articles = new List<FeedItem>();
            _lastBlockedFeeds.Clear();
            var normalizedAdvertisementKeywords = NormalizeAdvertisementKeywords(advertisementKeywords);
            var hasAdvertisementKeywords = normalizedAdvertisementKeywords.Count > 0;
            var enabledFeeds = (feeds ?? new List<Feed>())
                .Where(f => f?.IsEnabled == true)
                .ToList();

            if (enabledFeeds.Count == 0)
            {
                return articles;
            }

            var concurrency = Math.Min(MaxConcurrentFeedRequests, enabledFeeds.Count);
            using var semaphore = new SemaphoreSlim(Math.Max(1, concurrency));
            var tasks = enabledFeeds
                .Select((feed, index) => FetchFeedWithConcurrencyAsync(
                    index,
                    feed,
                    keywordFilter,
                    maxItems,
                    hasAdvertisementKeywords,
                    normalizedAdvertisementKeywords,
                    semaphore))
                .ToList();

            var results = await Task.WhenAll(tasks);

            foreach (var result in results.OrderBy(r => r.Index))
            {
                if (!string.IsNullOrWhiteSpace(result.BlockedFeedName))
                {
                    _lastBlockedFeeds.Add(result.BlockedFeedName);
                }

                articles.AddRange(result.Items);
            }

            return articles;
        }

        private async Task<FeedFetchResult> FetchFeedWithConcurrencyAsync(
            int index,
            Feed feed,
            string keywordFilter,
            int maxItems,
            bool hasAdvertisementKeywords,
            IReadOnlyList<string> normalizedAdvertisementKeywords,
            SemaphoreSlim semaphore)
        {
            await semaphore.WaitAsync();
            try
            {
                return await FetchSingleFeedAsync(
                    index,
                    feed,
                    keywordFilter,
                    maxItems,
                    hasAdvertisementKeywords,
                    normalizedAdvertisementKeywords);
            }
            finally
            {
                semaphore.Release();
            }
        }

        private async Task<FeedFetchResult> FetchSingleFeedAsync(
            int index,
            Feed feed,
            string keywordFilter,
            int maxItems,
            bool hasAdvertisementKeywords,
            IReadOnlyList<string> normalizedAdvertisementKeywords)
        {
            if (!TryNormalizeFeedUrl(feed.Url, out var normalizedUrl))
            {
                return new FeedFetchResult
                {
                    Index = index,
                    BlockedFeedName = feed.Name,
                    Items = new List<FeedItem>
                    {
                        new FeedItem
                        {
                            FeedName = feed.Name,
                            FeedUrl = feed.Url,
                            Title = "[BLOCKED] Feed URL rejected by security policy",
                            Description = string.Empty,
                            Link = string.Empty,
                            PublicationDate = DateTime.MinValue
                        }
                    }
                };
            }

            try
            {
                using var response = await _httpClient.GetAsync(normalizedUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync();
                using var xmlReader = XmlReader.Create(stream);
                var syndicationFeed = SyndicationFeed.Load(xmlReader);

                var feedItems = new List<FeedItem>();

                // Pull extra items when ad filtering is enabled to still fill the requested count.
                var maxFetch = hasAdvertisementKeywords ? maxItems * 4 : maxItems;
                var candidates = syndicationFeed.Items
                    .Where(item =>
                        string.IsNullOrEmpty(keywordFilter) ||
                        item.Title.Text.Contains(keywordFilter, StringComparison.OrdinalIgnoreCase) ||
                        (item.Summary?.Text?.Contains(keywordFilter, StringComparison.OrdinalIgnoreCase) ?? false))
                    .Take(maxFetch);

                int addedNonAds = 0;

                foreach (var item in candidates)
                {
                    var feedItem = new FeedItem
                    {
                        FeedName = feed.Name,
                        FeedUrl = feed.Url,
                        Title = item.Title.Text,
                        Description = item.Summary?.Text ?? string.Empty,
                        Link = item.Links.FirstOrDefault()?.Uri.ToString() ?? string.Empty,
                        PublicationDate = item.PublishDate.DateTime
                    };

                    if (hasAdvertisementKeywords)
                    {
                        feedItem.IsAdvertisement = IsAdvertisement(feedItem, item, normalizedAdvertisementKeywords);
                        if (feedItem.IsAdvertisement)
                        {
                            continue; // skip ads when filtering
                        }
                    }

                    feedItems.Add(feedItem);
                    if (hasAdvertisementKeywords)
                    {
                        addedNonAds++;
                        if (addedNonAds >= maxItems)
                        {
                            break;
                        }
                    }
                }

                return new FeedFetchResult
                {
                    Index = index,
                    Items = feedItems
                };
            }
            catch (Exception ex)
            {
                return new FeedFetchResult
                {
                    Index = index,
                    Items = new List<FeedItem>
                    {
                        new FeedItem
                        {
                            FeedName = feed.Name,
                            FeedUrl = feed.Url,
                            Title = $"[ERROR] {ex.Message}",
                            Description = string.Empty,
                            Link = string.Empty,
                            PublicationDate = DateTime.MinValue
                        }
                    }
                };
            }
        }
        private static IReadOnlyList<string> NormalizeAdvertisementKeywords(IReadOnlyCollection<string> keywords)
        {
            if (keywords == null || keywords.Count == 0)
            {
                return Array.Empty<string>();
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

        private static bool IsAdvertisement(FeedItem feedItem, SyndicationItem syndicationItem, IReadOnlyList<string> keywords)
        {
            if (keywords == null || keywords.Count == 0)
            {
                return false;
            }

            var searchTargets = new List<string>();

            if (!string.IsNullOrWhiteSpace(feedItem?.Title))
            {
                searchTargets.Add(feedItem.Title);
            }

            if (!string.IsNullOrWhiteSpace(feedItem?.Description))
            {
                searchTargets.Add(feedItem.Description);
                var decoded = WebUtility.HtmlDecode(feedItem.Description);
                if (!string.IsNullOrWhiteSpace(decoded))
                {
                    searchTargets.Add(decoded);
                }
            }

            if (!string.IsNullOrWhiteSpace(feedItem?.Link))
            {
                searchTargets.Add(feedItem.Link);
            }

            if (!string.IsNullOrWhiteSpace(feedItem?.FeedName))
            {
                searchTargets.Add(feedItem.FeedName);
            }

            if (syndicationItem != null)
            {
                foreach (var category in syndicationItem.Categories ?? Enumerable.Empty<SyndicationCategory>())
                {
                    if (!string.IsNullOrWhiteSpace(category?.Name))
                    {
                        searchTargets.Add(category.Name);
                    }
                }

                if (syndicationItem.Content is TextSyndicationContent textContent && !string.IsNullOrWhiteSpace(textContent.Text))
                {
                    searchTargets.Add(textContent.Text);
                    var decodedContent = WebUtility.HtmlDecode(textContent.Text);
                    if (!string.IsNullOrWhiteSpace(decodedContent))
                    {
                        searchTargets.Add(decodedContent);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(feedItem?.Link) && Uri.TryCreate(feedItem.Link, UriKind.Absolute, out var uri))
            {
                if (!string.IsNullOrWhiteSpace(uri.Host))
                {
                    searchTargets.Add(uri.Host);
                }
            }

            foreach (var target in searchTargets)
            {
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

        public void Dispose()
        {
            _httpClient?.Dispose();
        }

        public static bool TryNormalizeFeedUrl(string url, out string normalizedUrl)
        {
            normalizedUrl = string.Empty;

            if (!TryGetAllowedUri(url, out var allowedUri))
            {
                return false;
            }

            normalizedUrl = allowedUri.AbsoluteUri;
            return true;
        }

        public static List<Feed> NormalizeAndFilterFeeds(IEnumerable<Feed> feeds)
        {
            var result = new List<Feed>();
            if (feeds == null)
            {
                return result;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var feed in feeds)
            {
                if (feed == null)
                {
                    continue;
                }

                if (!TryNormalizeFeedUrl(feed.Url, out var normalizedUrl))
                {
                    continue;
                }

                if (!seen.Add(normalizedUrl))
                {
                    continue;
                }

                feed.Url = normalizedUrl;
                result.Add(feed);
            }

            return result;
        }

        private static bool TryGetAllowedUri(string url, out Uri uri)
        {
            uri = null;

            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var candidate))
            {
                return false;
            }

            if (!AllowedSchemes.Contains(candidate.Scheme, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            if (IsBlockedHost(candidate.Host))
            {
                return false;
            }

            uri = candidate;
            return true;
        }

        private static bool IsBlockedHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return true;
            }

            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (IPAddress.TryParse(host, out var ip))
            {
                if (IPAddress.IsLoopback(ip))
                {
                    return true;
                }

                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    var bytes = ip.GetAddressBytes();
                    // 10.0.0.0/8
                    if (bytes[0] == 10)
                        return true;
                    // 172.16.0.0 - 172.31.255.255
                    if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                        return true;
                    // 192.168.0.0/16
                    if (bytes[0] == 192 && bytes[1] == 168)
                        return true;
                }
            }

            return false;
        }
    }
}