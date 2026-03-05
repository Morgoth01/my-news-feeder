using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace MyNewsFeeder.Services
{
    public sealed class ArticleReaderService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private static readonly Encoding Utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        private static readonly Regex CharsetRegex = new Regex(
            @"charset\s*=\s*[""']?\s*([a-zA-Z0-9_\-]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly HashSet<string> NoiseKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "comment", "footer", "nav", "menu", "sidebar", "related", "recommend",
            "advert", "promo", "share", "social", "cookie", "banner"
        };

        private static readonly HashSet<string> ConsentKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cookie",
            "cookies",
            "consent",
            "tracking",
            "privacy",
            "datenschutz",
            "zustimmen",
            "einwilligung",
            "verarbeitung",
            "zwecke",
            "anbieter"
        };

        private static readonly string[] ConsentContainerKeywords =
        {
            "consent",
            "cookie",
            "cmp",
            "onetrust",
            "didomi",
            "sourcepoint",
            "privacy",
            "sp_message"
        };

        private static readonly string[] PreferredContentSelectors =
        {
            "//*[@itemprop='articleBody']",
            "//article",
            "//main",
            "//*[contains(@class,'article-body')]",
            "//*[contains(@class,'article-content')]",
            "//*[contains(@class,'post-content')]",
            "//*[contains(@class,'entry-content')]",
            "//*[contains(@class,'story-body')]"
        };

        private static readonly string[] StopSectionKeywords =
        {
            "related",
            "recommended",
            "read next",
            "you may also like",
            "more from",
            "newsletter",
            "comments",
            "kommentare",
            "mehr zum thema",
            "auch interessant",
            "cookie",
            "datenschutz",
            "werbung",
            "anzeige",
            "sponsored"
        };

        private sealed class ExtractionAttemptResult
        {
            public ReaderArticleContent Content { get; set; }
            public List<string> FallbackUrls { get; } = new List<string>();
        }

        public ArticleReaderService()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(25)
            };
            var version = System.Reflection.Assembly
                .GetEntryAssembly()?
                .GetName()?
                .Version?
                .ToString() ?? "1.0";
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"MyNewsFeeder/{version}");
        }

        public async Task<ReaderArticleContent> ExtractAsync(string articleUrl)
        {
            if (!FeedService.TryNormalizeFeedUrl(articleUrl, out var normalizedUrl))
            {
                return null;
            }

            var attemptedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pendingUrls = new Queue<string>();
            pendingUrls.Enqueue(normalizedUrl);

            while (pendingUrls.Count > 0 && attemptedUrls.Count < 8)
            {
                var currentUrl = pendingUrls.Dequeue();
                if (!FeedService.TryNormalizeFeedUrl(currentUrl, out var normalizedCurrentUrl))
                {
                    continue;
                }

                if (!attemptedUrls.Add(normalizedCurrentUrl))
                {
                    continue;
                }

                var extraction = await TryExtractFromUrlAsync(normalizedCurrentUrl);
                if (extraction?.Content != null)
                {
                    return extraction.Content;
                }

                if (extraction?.FallbackUrls == null)
                {
                    continue;
                }

                foreach (var fallbackUrl in extraction.FallbackUrls)
                {
                    if (string.IsNullOrWhiteSpace(fallbackUrl))
                    {
                        continue;
                    }

                    if (!attemptedUrls.Contains(fallbackUrl))
                    {
                        pendingUrls.Enqueue(fallbackUrl);
                    }
                }
            }

            return null;
        }

        private async Task<ExtractionAttemptResult> TryExtractFromUrlAsync(string normalizedUrl)
        {
            var result = new ExtractionAttemptResult();
            try
            {
                using var response = await _httpClient.GetAsync(normalizedUrl, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode)
                {
                    return result;
                }

                var mediaType = response.Content.Headers.ContentType?.MediaType;
                if (!string.IsNullOrWhiteSpace(mediaType) &&
                    !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
                {
                    return result;
                }

                var rawBytes = await response.Content.ReadAsByteArrayAsync();
                var detectedEncoding = ResolveEncoding(response, rawBytes);
                var html = detectedEncoding.GetString(rawBytes);
                if (string.IsNullOrWhiteSpace(html))
                {
                    return result;
                }

                var document = new HtmlDocument();
                document.LoadHtml(html);
                var root = document.DocumentNode;
                if (root == null)
                {
                    return result;
                }

                result.FallbackUrls.AddRange(DiscoverFallbackUrls(root, normalizedUrl));
                result.Content = BuildReaderArticleContent(root, normalizedUrl);
                return result;
            }
            catch
            {
                return result;
            }
        }

        private static ReaderArticleContent BuildReaderArticleContent(HtmlNode root, string normalizedUrl)
        {
            if (root == null)
            {
                return null;
            }

            var paragraphs = ExtractCandidateParagraphs(root, normalizedUrl);
            if (paragraphs.Count == 0 || !PassesFinalQualityGate(paragraphs))
            {
                return null;
            }

            var title = ExtractTitle(root, normalizedUrl);
            var htmlContent = BuildArticleHtmlFromParagraphs(paragraphs);
            var plainText = string.Join(Environment.NewLine + Environment.NewLine, paragraphs);

            return new ReaderArticleContent
            {
                SourceUrl = normalizedUrl,
                Title = title,
                HtmlContent = htmlContent,
                PlainText = plainText
            };
        }

        private static List<string> ExtractCandidateParagraphs(HtmlNode root, string normalizedUrl)
        {
            var jsonLdParagraphs = TryExtractJsonLdParagraphs(root);
            RemoveNoiseNodes(root);
            var strictNode = SelectStrictArticleNode(root, normalizedUrl);
            var fallbackNode = SelectMainContentNode(root);
            var strictParagraphs = ExtractStrictParagraphs(strictNode);
            var fallbackStrictParagraphs = ExtractStrictParagraphs(fallbackNode);
            var fallbackParagraphs = ExtractFallbackParagraphs(fallbackNode);
            var bodyFallback = new List<string>();
            var fallbackText = NormalizeWhitespace(root.SelectSingleNode("//body")?.InnerText ?? root.InnerText);
            if (!string.IsNullOrWhiteSpace(fallbackText))
            {
                bodyFallback.Add(fallbackText);
            }

            var paragraphs = SelectBestParagraphSet(
                jsonLdParagraphs,
                strictParagraphs,
                fallbackStrictParagraphs,
                fallbackParagraphs,
                bodyFallback);

            if (!PassesFinalQualityGate(paragraphs))
            {
                paragraphs = SelectBestParagraphSet(
                    jsonLdParagraphs,
                    strictParagraphs,
                    fallbackStrictParagraphs,
                    fallbackParagraphs);
            }

            return paragraphs;
        }

        private static List<string> DiscoverFallbackUrls(HtmlNode root, string normalizedUrl)
        {
            var result = new List<string>();
            if (root == null || string.IsNullOrWhiteSpace(normalizedUrl))
            {
                return result;
            }

            if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var baseUri))
            {
                return result;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddCandidate(string candidate)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    return;
                }

                if (!Uri.TryCreate(baseUri, candidate.Trim(), out var resolvedUri))
                {
                    return;
                }

                if (!FeedService.TryNormalizeFeedUrl(resolvedUri.AbsoluteUri, out var normalizedCandidate))
                {
                    return;
                }

                if (string.Equals(normalizedCandidate, normalizedUrl, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (seen.Add(normalizedCandidate))
                {
                    result.Add(normalizedCandidate);
                }
            }

            var ampLinks = root.SelectNodes("//link[contains(translate(@rel,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'amphtml') and @href]");
            if (ampLinks != null)
            {
                foreach (var ampLink in ampLinks.Take(5))
                {
                    AddCandidate(ampLink.GetAttributeValue("href", string.Empty));
                }
            }

            var alternateAmpLinks = root.SelectNodes("//link[contains(translate(@rel,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'alternate') and contains(translate(@href,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'amp') and @href]");
            if (alternateAmpLinks != null)
            {
                foreach (var alternateLink in alternateAmpLinks.Take(5))
                {
                    AddCandidate(alternateLink.GetAttributeValue("href", string.Empty));
                }
            }

            foreach (var domainFallback in BuildDomainFallbackCandidates(baseUri))
            {
                AddCandidate(domainFallback);
            }

            return result;
        }

        private static IEnumerable<string> BuildDomainFallbackCandidates(Uri uri)
        {
            if (uri == null)
            {
                yield break;
            }

            if (uri.Host.EndsWith("golem.de", StringComparison.OrdinalIgnoreCase))
            {
                var path = uri.AbsolutePath ?? string.Empty;
                if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) &&
                    !path.EndsWith("-amp.html", StringComparison.OrdinalIgnoreCase))
                {
                    var ampPath = path.Substring(0, path.Length - 5) + "-amp.html";
                    yield return new UriBuilder(uri) { Path = ampPath, Query = string.Empty }.Uri.AbsoluteUri;
                }

                yield return new UriBuilder(uri) { Query = "amp=1" }.Uri.AbsoluteUri;
                yield return new UriBuilder(uri) { Query = "output=amp" }.Uri.AbsoluteUri;
            }
        }

        private static Encoding ResolveEncoding(HttpResponseMessage response, byte[] contentBytes)
        {
            if (TryResolveEncodingFromHeader(response, out var headerEncoding))
            {
                return headerEncoding;
            }

            if (TryResolveEncodingFromMeta(contentBytes, out var metaEncoding))
            {
                return metaEncoding;
            }

            if (IsValidUtf8(contentBytes))
            {
                return Encoding.UTF8;
            }

            // Typical fallback for western sites that omit charset.
            return GetEncodingOrDefault("windows-1252", Encoding.Latin1);
        }

        private static bool TryResolveEncodingFromHeader(HttpResponseMessage response, out Encoding encoding)
        {
            encoding = null;

            var charset = response?.Content?.Headers?.ContentType?.CharSet;
            if (string.IsNullOrWhiteSpace(charset))
            {
                return false;
            }

            var normalized = NormalizeCharset(charset);
            try
            {
                encoding = Encoding.GetEncoding(normalized);
                return encoding != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryResolveEncodingFromMeta(byte[] contentBytes, out Encoding encoding)
        {
            encoding = null;
            if (contentBytes == null || contentBytes.Length == 0)
            {
                return false;
            }

            var probeLength = Math.Min(contentBytes.Length, 12288);
            var probeText = Encoding.ASCII.GetString(contentBytes, 0, probeLength);

            var match = CharsetRegex.Match(probeText);
            if (!match.Success || match.Groups.Count < 2)
            {
                return false;
            }

            var charset = NormalizeCharset(match.Groups[1].Value);
            try
            {
                encoding = Encoding.GetEncoding(charset);
                return encoding != null;
            }
            catch
            {
                return false;
            }
        }

        private static string NormalizeCharset(string charset)
        {
            if (string.IsNullOrWhiteSpace(charset))
            {
                return string.Empty;
            }

            var value = charset.Trim().Trim('"', '\'').ToLowerInvariant();
            return value switch
            {
                "latin1" => "iso-8859-1",
                "cp1252" => "windows-1252",
                _ => value
            };
        }

        private static bool IsValidUtf8(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return true;
            }

            try
            {
                _ = Utf8Strict.GetString(bytes);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Encoding GetEncodingOrDefault(string name, Encoding fallback)
        {
            try
            {
                return Encoding.GetEncoding(name);
            }
            catch
            {
                return fallback;
            }
        }

        private static HtmlNode SelectStrictArticleNode(HtmlNode root, string normalizedUrl)
        {
            HtmlNode bestNode = null;
            double bestScore = double.MinValue;

            var domainSpecificNode = TrySelectDomainSpecificNode(root, normalizedUrl);
            if (domainSpecificNode != null)
            {
                return domainSpecificNode;
            }

            foreach (var selector in PreferredContentSelectors)
            {
                var nodes = root.SelectNodes(selector);
                if (nodes == null)
                {
                    continue;
                }

                foreach (var node in nodes.Take(20))
                {
                    var score = ScoreContainer(node, selectorBonus: 700);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestNode = node;
                    }
                }
            }

            return bestNode ?? SelectMainContentNode(root);
        }

        private static HtmlNode TrySelectDomainSpecificNode(HtmlNode root, string normalizedUrl)
        {
            if (root == null || string.IsNullOrWhiteSpace(normalizedUrl))
            {
                return null;
            }

            if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri))
            {
                return null;
            }

            if (uri.Host.EndsWith("golem.de", StringComparison.OrdinalIgnoreCase))
            {
                return TrySelectGolemArticleNode(root);
            }

            return null;
        }

        private static HtmlNode TrySelectGolemArticleNode(HtmlNode root)
        {
            var selectors = new[]
            {
                "//article//*[contains(@class,'formatted')]",
                "//article//*[contains(@class,'entry-content')]",
                "//article//*[contains(@class,'article-body')]",
                "//article//*[contains(@class,'article-content')]",
                "//*[contains(@class,'formatted')]",
                "//*[contains(@class,'entry-content')]",
                "//*[contains(@class,'article-body')]",
                "//*[contains(@class,'article-content')]",
                "//*[contains(@id,'article')]"
            };

            HtmlNode bestNode = null;
            double bestScore = double.MinValue;

            foreach (var selector in selectors)
            {
                var nodes = root.SelectNodes(selector);
                if (nodes == null)
                {
                    continue;
                }

                foreach (var node in nodes.Take(40))
                {
                    if (LooksLikeConsentContainer(node))
                    {
                        continue;
                    }

                    var score = ScoreContainer(node, selectorBonus: 1150);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestNode = node;
                    }
                }
            }

            return bestNode;
        }

        private static List<string> TryExtractJsonLdParagraphs(HtmlNode root)
        {
            var result = new List<string>();
            if (root == null)
            {
                return result;
            }

            var scriptNodes = root.SelectNodes("//script[@type='application/ld+json']");
            if (scriptNodes == null || scriptNodes.Count == 0)
            {
                return result;
            }

            var rawBodies = new List<string>();

            foreach (var script in scriptNodes.Take(20))
            {
                var json = script?.InnerText;
                if (string.IsNullOrWhiteSpace(json))
                {
                    continue;
                }

                try
                {
                    using var document = JsonDocument.Parse(json);
                    CollectArticleBodyStrings(document.RootElement, rawBodies);
                }
                catch
                {
                    // Skip malformed JSON-LD fragments.
                }
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in rawBodies)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                var normalizedRaw = NormalizeWhitespace(WebUtility.HtmlDecode(raw));
                if (string.IsNullOrWhiteSpace(normalizedRaw))
                {
                    continue;
                }

                var chunks = normalizedRaw
                    .Split(new[] { ". ", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(chunk => NormalizeWhitespace(chunk))
                    .Where(chunk => chunk.Length >= 45);

                foreach (var chunk in chunks)
                {
                    if (seen.Add(chunk))
                    {
                        result.Add(chunk.EndsWith(".") ? chunk : chunk + ".");
                    }
                }
            }

            return result;
        }

        private static void CollectArticleBodyStrings(JsonElement element, List<string> output)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.NameEquals("articleBody") || property.NameEquals("text") || property.NameEquals("description"))
                        {
                            if (property.Value.ValueKind == JsonValueKind.String)
                            {
                                var value = property.Value.GetString();
                                if (!string.IsNullOrWhiteSpace(value))
                                {
                                    output.Add(value);
                                }
                            }
                            else
                            {
                                CollectArticleBodyStrings(property.Value, output);
                            }
                        }
                        else
                        {
                            CollectArticleBodyStrings(property.Value, output);
                        }
                    }
                    break;

                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        CollectArticleBodyStrings(item, output);
                    }
                    break;
            }
        }

        private static void RemoveNoiseNodes(HtmlNode root)
        {
            if (root == null)
            {
                return;
            }

            var hardRemoveXPath = "//script|//style|//noscript|//iframe|//svg|//footer|//nav|//aside|//form|//button|//input|//figure[contains(@class,'ad')]";
            var hardNodes = root.SelectNodes(hardRemoveXPath);
            if (hardNodes != null)
            {
                foreach (var node in hardNodes.ToList())
                {
                    node.Remove();
                }
            }

            // Remove likely boilerplate containers by class/id semantics.
            var semanticContainers = root.SelectNodes("//*[@class or @id]");
            if (semanticContainers == null)
            {
                return;
            }

            foreach (var node in semanticContainers.ToList())
            {
                var classId = $"{node.GetAttributeValue("class", string.Empty)} {node.GetAttributeValue("id", string.Empty)}";
                if (string.IsNullOrWhiteSpace(classId))
                {
                    continue;
                }

                if (ContainsNoiseKeyword(classId))
                {
                    var tag = node.Name?.ToLowerInvariant() ?? string.Empty;
                    if (tag == "article" || tag == "main")
                    {
                        continue;
                    }

                    // Keep nodes that look like real content blocks.
                    var textLength = NormalizeWhitespace(node.InnerText).Length;
                    var paragraphCount = node.Descendants("p").Count();
                    if (textLength > 1200 && paragraphCount >= 6)
                    {
                        continue;
                    }

                    node.Remove();
                }
            }

            RemoveConsentNodes(root);
        }

        private static void RemoveConsentNodes(HtmlNode root)
        {
            if (root == null)
            {
                return;
            }

            var consentNodes = root.SelectNodes("//*[contains(@id,'consent') or contains(@class,'consent') or contains(@id,'cookie') or contains(@class,'cookie') or contains(@id,'cmp') or contains(@class,'cmp') or contains(@id,'onetrust') or contains(@class,'onetrust') or contains(@id,'didomi') or contains(@class,'didomi') or contains(@id,'sourcepoint') or contains(@class,'sourcepoint')]");
            if (consentNodes != null)
            {
                foreach (var node in consentNodes.ToList())
                {
                    node.Remove();
                }
            }

            var semanticConsentNodes = root.Descendants()
                .Where(node => node.NodeType == HtmlNodeType.Element)
                .Take(1600)
                .ToList();
            foreach (var node in semanticConsentNodes)
            {
                if (LooksLikeConsentContainer(node))
                {
                    node.Remove();
                }
            }

            var textNodes = root.Descendants()
                .Where(node =>
                    node.NodeType == HtmlNodeType.Element &&
                    (node.Name.Equals("div", StringComparison.OrdinalIgnoreCase) ||
                     node.Name.Equals("section", StringComparison.OrdinalIgnoreCase) ||
                     node.Name.Equals("article", StringComparison.OrdinalIgnoreCase) ||
                     node.Name.Equals("main", StringComparison.OrdinalIgnoreCase) ||
                     node.Name.Equals("p", StringComparison.OrdinalIgnoreCase)))
                .Take(1200)
                .ToList();

            foreach (var node in textNodes)
            {
                var text = NormalizeWhitespace(node.InnerText);
                if (text.Length == 0 || text.Length > 5000)
                {
                    continue;
                }

                if (LooksLikeConsentText(text) && !LooksLikeArticleContainer(node))
                {
                    node.Remove();
                }
            }
        }

        private static HtmlNode SelectMainContentNode(HtmlNode root)
        {
            var candidates = root.Descendants()
                .Where(node =>
                    string.Equals(node.Name, "article", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(node.Name, "main", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(node.Name, "section", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(node.Name, "div", StringComparison.OrdinalIgnoreCase))
                .Take(600)
                .ToList();

            HtmlNode best = null;
            double bestScore = double.MinValue;

            foreach (var node in candidates)
            {
                if (LooksLikeConsentContainer(node))
                {
                    continue;
                }

                var textLength = NormalizeWhitespace(node.InnerText).Length;
                if (textLength < 200)
                {
                    continue;
                }

                var score = ScoreContainer(node, selectorBonus: 0);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = node;
                }
            }

            return best ?? root.SelectSingleNode("//body") ?? root;
        }

        private static double ScoreContainer(HtmlNode node, int selectorBonus)
        {
            if (node == null)
            {
                return double.MinValue;
            }

            var textLength = NormalizeWhitespace(node.InnerText).Length;
            var paragraphCount = node.Descendants("p").Count();
            var headingCount = node.Descendants()
                .Count(child => child.Name.Equals("h1", StringComparison.OrdinalIgnoreCase) ||
                                child.Name.Equals("h2", StringComparison.OrdinalIgnoreCase) ||
                                child.Name.Equals("h3", StringComparison.OrdinalIgnoreCase));

            var linkTextLength = node.Descendants("a")
                .Select(anchor => NormalizeWhitespace(anchor.InnerText).Length)
                .Sum();
            var linkDensity = (double)linkTextLength / Math.Max(1, textLength);

            var score = textLength + (paragraphCount * 180) + (headingCount * 35) - (linkDensity * 1200.0) + selectorBonus;

            var classId = $"{node.GetAttributeValue("class", string.Empty)} {node.GetAttributeValue("id", string.Empty)}";
            if (ContainsNoiseKeyword(classId))
            {
                score -= 1800;
            }

            if (LooksLikeConsentContainer(node))
            {
                score -= 5200;
            }

            if (string.Equals(node.Name, "article", StringComparison.OrdinalIgnoreCase))
            {
                score += 450;
            }
            else if (string.Equals(node.Name, "main", StringComparison.OrdinalIgnoreCase))
            {
                score += 260;
            }

            if (!string.IsNullOrWhiteSpace(node.GetAttributeValue("itemprop", string.Empty)) &&
                node.GetAttributeValue("itemprop", string.Empty).IndexOf("articleBody", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 500;
            }

            return score;
        }

        private static bool ContainsNoiseKeyword(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return NoiseKeywords.Any(keyword => value.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static List<string> ExtractStrictParagraphs(HtmlNode node)
        {
            var result = new List<string>();
            if (node == null)
            {
                return result;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var contentLength = 0;

            var blocks = node.DescendantsAndSelf()
                .Where(child =>
                    child.Name.Equals("p", StringComparison.OrdinalIgnoreCase) ||
                    child.Name.Equals("h2", StringComparison.OrdinalIgnoreCase) ||
                    child.Name.Equals("h3", StringComparison.OrdinalIgnoreCase) ||
                    child.Name.Equals("blockquote", StringComparison.OrdinalIgnoreCase) ||
                    child.Name.Equals("li", StringComparison.OrdinalIgnoreCase))
                .Take(450)
                .ToList();

            foreach (var block in blocks)
            {
                var normalized = NormalizeWhitespace(WebUtility.HtmlDecode(block.InnerText));
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    continue;
                }

                if (ShouldStopAtBlock(block, normalized, contentLength))
                {
                    break;
                }

                if (LooksLikeConsentText(normalized))
                {
                    continue;
                }

                var score = ScoreTextBlock(block, normalized);
                var minScore = block.Name.Equals("h2", StringComparison.OrdinalIgnoreCase) || block.Name.Equals("h3", StringComparison.OrdinalIgnoreCase)
                    ? 20
                    : block.Name.Equals("li", StringComparison.OrdinalIgnoreCase)
                        ? 120
                        : 90;
                if (score < minScore)
                {
                    continue;
                }

                if (seen.Add(normalized))
                {
                    result.Add(normalized);
                    contentLength += normalized.Length;
                }

                if (result.Count >= 120 || contentLength > 22000)
                {
                    break;
                }
            }

            return result;
        }

        private static bool ShouldStopAtBlock(HtmlNode block, string text, int accumulatedContentLength)
        {
            if (accumulatedContentLength < 380)
            {
                return false;
            }

            if (text.Length > 120)
            {
                return false;
            }

            var lower = text.ToLowerInvariant();
            if (StopSectionKeywords.Any(keyword => lower.Contains(keyword)))
            {
                return true;
            }

            var classId = $"{block.GetAttributeValue("class", string.Empty)} {block.GetAttributeValue("id", string.Empty)}";
            return StopSectionKeywords.Any(keyword => classId.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool LooksLikeConsentText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var lower = text.ToLowerInvariant();
            var hits = CountConsentKeywordHits(lower);
            if (hits >= 3)
            {
                return true;
            }

            return lower.Contains("nutzung aller cookies") ||
                   lower.Contains("cookies zustimmen") ||
                   lower.Contains("datenschutzerklärung") ||
                   (lower.Contains("cookie") && lower.Contains("tracking") && lower.Contains("zustimmen"));
        }

        private static int CountConsentKeywordHits(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            return ConsentKeywords.Count(keyword => text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool LooksLikeConsentContainer(HtmlNode node)
        {
            if (node == null)
            {
                return false;
            }

            var classId = $"{node.GetAttributeValue("class", string.Empty)} {node.GetAttributeValue("id", string.Empty)}";
            if (!string.IsNullOrWhiteSpace(classId) &&
                ConsentContainerKeywords.Any(keyword => classId.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return true;
            }

            var text = NormalizeWhitespace(node.InnerText);
            if (text.Length < 40 || text.Length > 6000)
            {
                return false;
            }

            var consentHits = CountConsentKeywordHits(text);
            if (consentHits < 4)
            {
                return false;
            }

            return text.IndexOf("zustimmen", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("akzeptieren", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("ablehnen", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("einwilligung", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool LooksLikeArticleContainer(HtmlNode node)
        {
            if (node == null)
            {
                return false;
            }

            var paragraphNodes = node.Descendants("p").Take(16).ToList();
            var paragraphCount = paragraphNodes.Count;
            if (paragraphCount >= 5)
            {
                return true;
            }

            if (paragraphCount == 0)
            {
                return false;
            }

            var avgParagraphLength = paragraphNodes
                .Select(p => NormalizeWhitespace(p.InnerText).Length)
                .DefaultIfEmpty(0)
                .Average();

            return avgParagraphLength >= 85;
        }

        private static double ScoreTextBlock(HtmlNode block, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return double.MinValue;
            }

            var lengthScore = text.Length;
            var punctuationScore = text.Count(ch => ch == '.' || ch == '!' || ch == '?') * 14;
            var hasSentenceShape = text.Contains(' ') && (text.EndsWith(".") || text.EndsWith("!") || text.EndsWith("?"));
            var sentenceShapeBonus = hasSentenceShape ? 70 : 0;

            var linkTextLength = block.Descendants("a").Select(a => NormalizeWhitespace(a.InnerText).Length).Sum();
            var linkDensity = (double)linkTextLength / Math.Max(1, text.Length);
            var linkPenalty = linkDensity * 420.0;

            var lower = text.ToLowerInvariant();
            var noisePenalty = StopSectionKeywords.Count(keyword => lower.Contains(keyword)) * 120;

            var tagBonus = 0;
            if (block.Name.Equals("p", StringComparison.OrdinalIgnoreCase))
            {
                tagBonus = 50;
            }
            else if (block.Name.Equals("blockquote", StringComparison.OrdinalIgnoreCase))
            {
                tagBonus = 25;
            }
            else if (block.Name.Equals("h2", StringComparison.OrdinalIgnoreCase) || block.Name.Equals("h3", StringComparison.OrdinalIgnoreCase))
            {
                tagBonus = 10;
            }
            else if (block.Name.Equals("li", StringComparison.OrdinalIgnoreCase))
            {
                tagBonus = -15;
            }

            return lengthScore + punctuationScore + sentenceShapeBonus + tagBonus - linkPenalty - noisePenalty;
        }

        private static bool IsHighQualityExtraction(IReadOnlyList<string> paragraphs)
        {
            if (paragraphs == null || paragraphs.Count == 0)
            {
                return false;
            }

            var totalChars = paragraphs.Sum(text => text?.Length ?? 0);
            var longParagraphs = paragraphs.Count(text => !string.IsNullOrWhiteSpace(text) && text.Length >= 90);
            var punctuationHits = paragraphs.Sum(text => text?.Count(ch => ch == '.' || ch == '!' || ch == '?') ?? 0);
            var consentHits = CountConsentKeywordHits(string.Join(" ", paragraphs));

            return totalChars >= 320 && longParagraphs >= 2 && punctuationHits >= 4 && consentHits < 3;
        }

        private static List<string> SelectBestParagraphSet(params List<string>[] candidates)
        {
            var best = new List<string>();
            var bestScore = double.MinValue;

            foreach (var candidate in candidates)
            {
                if (candidate == null || candidate.Count == 0)
                {
                    continue;
                }

                var score = ScoreParagraphSet(candidate);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best;
        }

        private static double ScoreParagraphSet(IReadOnlyList<string> paragraphs)
        {
            if (paragraphs == null || paragraphs.Count == 0)
            {
                return double.MinValue;
            }

            var totalChars = paragraphs.Sum(text => text?.Length ?? 0);
            var longParagraphs = paragraphs.Count(text => !string.IsNullOrWhiteSpace(text) && text.Length >= 90);
            var shortParagraphs = paragraphs.Count(text => !string.IsNullOrWhiteSpace(text) && text.Length <= 35);
            var punctuationHits = paragraphs.Sum(text => text?.Count(ch => ch == '.' || ch == '!' || ch == '?') ?? 0);
            var joined = string.Join(" ", paragraphs);
            var noiseHits = StopSectionKeywords.Count(keyword => joined.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
            var consentHits = CountConsentKeywordHits(joined);

            var score = totalChars + (longParagraphs * 120) + (punctuationHits * 8) - (shortParagraphs * 45) - (noiseHits * 180) - (consentHits * 240);
            if (IsHighQualityExtraction(paragraphs))
            {
                score += 800;
            }

            if (consentHits >= 4)
            {
                score -= 1600;
            }

            return score;
        }

        private static bool PassesFinalQualityGate(IReadOnlyList<string> paragraphs)
        {
            if (paragraphs == null || paragraphs.Count == 0)
            {
                return false;
            }

            var joined = string.Join(" ", paragraphs);
            var consentHits = CountConsentKeywordHits(joined);
            if (consentHits >= 4)
            {
                return false;
            }

            var totalChars = paragraphs.Sum(text => text?.Length ?? 0);
            var longParagraphs = paragraphs.Count(text => !string.IsNullOrWhiteSpace(text) && text.Length >= 90);
            var veryShortBlocks = paragraphs.Count(text => !string.IsNullOrWhiteSpace(text) && text.Length <= 30);
            var punctuationHits = paragraphs.Sum(text => text?.Count(ch => ch == '.' || ch == '!' || ch == '?') ?? 0);

            if (IsHighQualityExtraction(paragraphs))
            {
                return true;
            }

            if (totalChars < 420)
            {
                return false;
            }

            if (longParagraphs < 2)
            {
                return false;
            }

            if (punctuationHits < 3)
            {
                return false;
            }

            return veryShortBlocks <= Math.Max(3, paragraphs.Count / 2);
        }

        private static List<string> ExtractFallbackParagraphs(HtmlNode node)
        {
            var result = new List<string>();
            if (node == null)
            {
                return result;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddIfUseful(string text, int minLength = 28)
            {
                var normalized = NormalizeWhitespace(text);
                if (normalized.Length < minLength)
                {
                    return;
                }

                if (LooksLikeConsentText(normalized))
                {
                    return;
                }

                if (!normalized.Any(ch => ch == '.' || ch == '!' || ch == '?') && normalized.Length < 55)
                {
                    return;
                }

                if (seen.Add(normalized))
                {
                    result.Add(normalized);
                }
            }

            foreach (var paragraph in node.Descendants("p").Take(160))
            {
                AddIfUseful(paragraph.InnerText);
            }

            foreach (var listItem in node.Descendants("li").Take(80))
            {
                AddIfUseful(listItem.InnerText, minLength: 35);
            }

            if (result.Count == 0)
            {
                foreach (var div in node.Descendants("div").Take(160))
                {
                    AddIfUseful(div.InnerText, minLength: 45);
                }
            }

            return result;
        }

        private static string BuildArticleHtmlFromParagraphs(IEnumerable<string> paragraphs)
        {
            var builder = new StringBuilder();
            foreach (var paragraph in paragraphs)
            {
                var encoded = System.Web.HttpUtility.HtmlEncode(paragraph);
                builder.Append("<p>");
                builder.Append(encoded.Replace("\n", "<br/>"));
                builder.Append("</p>");
            }

            return builder.Length == 0 ? "<p></p>" : builder.ToString();
        }

        private static string ExtractTitle(HtmlNode root, string fallbackUrl)
        {
            var selectors = new[]
            {
                "//meta[@property='og:title']",
                "//meta[@name='twitter:title']",
                "//h1",
                "//title"
            };

            foreach (var selector in selectors)
            {
                var node = root.SelectSingleNode(selector);
                if (node == null)
                {
                    continue;
                }

                var value = node.GetAttributeValue("content", string.Empty);
                if (string.IsNullOrWhiteSpace(value))
                {
                    value = node.InnerText;
                }

                var normalized = NormalizeWhitespace(WebUtility.HtmlDecode(value));
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    return normalized;
                }
            }

            if (Uri.TryCreate(fallbackUrl, UriKind.Absolute, out var uri))
            {
                return uri.Host;
            }

            return "Article";
        }

        private static string NormalizeWhitespace(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            value = DecodeHtmlEntitiesDeep(value);

            var builder = new StringBuilder(value.Length);
            var previousWhitespace = false;

            foreach (var ch in value)
            {
                if (char.IsWhiteSpace(ch))
                {
                    if (!previousWhitespace)
                    {
                        builder.Append(' ');
                        previousWhitespace = true;
                    }
                }
                else
                {
                    builder.Append(ch);
                    previousWhitespace = false;
                }
            }

            return builder.ToString().Trim();
        }

        private static string DecodeHtmlEntitiesDeep(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var current = value;

            // Some pages return nested entity encoding, e.g. "&amp;ouml;".
            for (var i = 0; i < 4; i++)
            {
                var decoded = WebUtility.HtmlDecode(current);
                if (string.Equals(decoded, current, StringComparison.Ordinal))
                {
                    break;
                }

                current = decoded;
            }

            // Remove soft hyphen and zero-width artifacts often left by CMS output.
            current = current
                .Replace("\u00AD", string.Empty) // soft hyphen
                .Replace("\u200B", string.Empty) // zero width space
                .Replace("\uFEFF", string.Empty); // zero width no-break space

            return current;
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    public sealed class ReaderArticleContent
    {
        public string SourceUrl { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string HtmlContent { get; init; } = "<p></p>";
        public string PlainText { get; init; } = string.Empty;
    }
}