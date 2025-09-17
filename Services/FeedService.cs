using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.ServiceModel.Syndication;
using System.Threading.Tasks;
using System.Xml;
using MyNewsFeeder.Models;

namespace MyNewsFeeder.Services
{
    public class FeedService
    {
        private readonly HttpClient _httpClient;
        private static readonly string[] AllowedSchemes =
        {
            Uri.UriSchemeHttp,
            Uri.UriSchemeHttps
        };

        public FeedService()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,
                CheckCertificateRevocationList = true
            };

            _httpClient = new HttpClient(handler, disposeHandler: true);
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            var version = System.Reflection.Assembly
            .GetEntryAssembly()?
            .GetName()?
            .Version?
            .ToString() ?? "1.0";
            _httpClient.DefaultRequestHeaders
                       .UserAgent.ParseAdd($"MyNewsFeeder/{version}");
        }

        public async Task<List<FeedItem>> FetchArticlesAsync(List<Feed> feeds, string keywordFilter, int maxItems)
        {
            var articles = new List<FeedItem>();

            foreach (var feed in feeds.Where(f => f.IsEnabled))
            {
                if (!TryNormalizeFeedUrl(feed.Url, out var normalizedUrl))
                {
                    articles.Add(new FeedItem
                    {
                        FeedName = feed.Name,
                        Title = "[ERROR] Unsupported or invalid feed URL",
                        Description = string.Empty,
                        Link = string.Empty,
                        PublicationDate = DateTime.MinValue
                    });
                    continue;
                }

                try
                {
                    using var response = await _httpClient.GetAsync(normalizedUrl);
                    response.EnsureSuccessStatusCode();

                    using var stream = await response.Content.ReadAsStreamAsync();
                    using var xmlReader = XmlReader.Create(stream);
                    var syndicationFeed = SyndicationFeed.Load(xmlReader);

                    var items = syndicationFeed.Items
                        .Take(maxItems)
                        .Where(item =>
                            string.IsNullOrEmpty(keywordFilter) ||
                            item.Title.Text.Contains(keywordFilter, StringComparison.OrdinalIgnoreCase) ||
                            (item.Summary?.Text?.Contains(keywordFilter, StringComparison.OrdinalIgnoreCase) ?? false));

                    foreach (var item in items)
                    {
                        articles.Add(new FeedItem
                        {
                            FeedName = feed.Name,
                            Title = item.Title.Text,
                            Description = item.Summary?.Text ?? string.Empty,
                            Link = item.Links.FirstOrDefault()?.Uri.ToString() ?? string.Empty,
                            PublicationDate = item.PublishDate.DateTime
                        });
                    }
                }
                catch (Exception ex)
                {
                    // Add error entry to articles list
                    articles.Add(new FeedItem
                    {
                        FeedName = feed.Name,
                        Title = $"[ERROR] {ex.Message}",
                        Description = string.Empty,
                        Link = string.Empty,
                        PublicationDate = DateTime.MinValue
                    });
                }
            }

            return articles;
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

            if (!string.IsNullOrEmpty(candidate.Host) && IPAddress.TryParse(candidate.Host, out _))
            {
                // Allow IP-based feeds but still block loopback/local if desired in future.
            }

            uri = candidate;
            return true;
        }
    }
}