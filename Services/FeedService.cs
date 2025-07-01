using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        public FeedService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("MyNewsFeeder/1.0");
        }

        public async Task<List<FeedItem>> FetchArticlesAsync(List<Feed> feeds, string keywordFilter, int maxItems)
        {
            var articles = new List<FeedItem>();

            foreach (var feed in feeds.Where(f => f.IsEnabled))
            {
                try
                {
                    using var response = await _httpClient.GetAsync(feed.Url);
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
    }
}
