using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MyNewsFeeder.Services
{
    /// <summary>
    /// Enhanced AdBlocker service with comprehensive domain and pattern blocking.
    /// Supports multiple filter list formats and custom blocking rules.
    /// </summary>
    public class AdBlockerService
    {
        private static readonly string[] FastPatternIndicators =
        {
            "ad",
            "ads",
            "advert",
            "banner",
            "promo",
            "sponsor",
            "track",
            "analytics",
            "pixel",
            "doubleclick",
            "googlesyndication",
            "googleadservices",
            "pagead",
            "taboola",
            "outbrain",
            "gclid=",
            "utm_"
        };

        private readonly HashSet<string> _blockedDomains;
        private readonly List<Regex> _blockedPatterns;
        private readonly HttpClient _httpClient;
        private bool _isInitialized = false;
        private readonly ConcurrentDictionary<string, bool> _urlDecisionCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, bool> _blockedHostCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, bool> _allowedHostCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _allowedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Allow domains that are known to break when blocked.
            "html-load.com"
        };

        // Updated filter list URLs with correct endpoints
        private readonly Dictionary<string, string> _filterLists = new()
        {
            ["EasyList"] = "https://easylist.to/easylist/easylist.txt",
            ["EasyPrivacy"] = "https://easylist.to/easylist/easyprivacy.txt",
            ["FanboysAnnoyance"] = "https://easylist.to/easylist/fanboy-annoyance.txt",
            ["StevenBlackHosts"] = "https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts",
            ["AdGuardBase"] = "https://raw.githubusercontent.com/AdguardTeam/AdguardFilters/master/BaseFilter/sections/adservers.txt",
            ["UBlockOriginFilters"] = "https://raw.githubusercontent.com/uBlockOrigin/uAssets/master/filters/filters.txt",
        };

        public AdBlockerService()
        {
            _blockedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _blockedPatterns = new List<Regex>();
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("MyNewsFeeder/1.0");

            // CRITICAL: Load basic domains synchronously first
            LoadBasicDomainsSync();

            // Load comprehensive filter lists asynchronously
            _ = InitializeAsync();
        }

        // CRITICAL: Synchronous loading of essential domains for immediate blocking
        private void LoadBasicDomainsSync()
        {
            // Essential ad domains for immediate blocking
            var criticalDomains = new[]
            {
                "googleads.g.doubleclick.net",
                "doubleclick.net",
                "googlesyndication.com",
                "googleadservices.com",
                "google-analytics.com",
                "googletagmanager.com",
                "googletagservices.com",

                // ADDED: Taboola/Outbrain domains (from your logs)
                "taboola.com",
                "vidstat.taboola.com",
                "outbrain.com",
                "videoexternalapi.outbrain.com",
                "widgets.outbrain.com",
                "amplify.outbrain.com",

                // Other major ad networks
                "amazon-adsystem.com",
                "adsystem.amazon.com",
                "facebook.com",
                "connect.facebook.net",
                "scorecardresearch.com",
                "quantserve.com",
                "revcontent.com",
                "mgid.com"
            };

            foreach (var domain in criticalDomains)
            {
                _blockedDomains.Add(domain);
            }
            ResetDecisionCaches();

            // Essential URL patterns for immediate blocking
            var criticalPatterns = new[]
            {
                @".*doubleclick.*",
                @".*googlesyndication.*",
                @".*googleadservices.*",
                @".*\/aclk\?.*",
                @".*gclid=.*",
                @".*\/ads\/.*",
                @".*\/ad\/.*",
                @".*block-data.*",    // For Taboola URLs
                @".*blocked-data.*",  // For Outbrain URLs
                @".*taboola.*",
                @".*outbrain.*"
            };

            foreach (var pattern in criticalPatterns)
            {
                try
                {
                    _blockedPatterns.Add(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled));
                }
                catch (Exception)
                {
                    // Ignore invalid regex patterns during initialization.
                }
            }

            // Mark as initialized immediately for basic blocking
            _isInitialized = true;
        }

        private async Task InitializeAsync()
        {
            try
            {
                await LoadLocalBlockListsAsync();
                await UpdateFilterListsAsync();
            }
            catch (Exception)
            {
                // AdBlocker still works with basic domains loaded synchronously
            }
        }

        private async Task LoadLocalBlockListsAsync()
        {
            // Load comprehensive ad domains
            var basicAdDomains = new[]
            {
                "doubleclick.net", "googleadservices.com", "googlesyndication.com",
                "googletagmanager.com", "google-analytics.com", "googletagservices.com",
                "adsystem.amazon.com", "amazon-adsystem.com",
                "facebook.com/tr", "connect.facebook.net", "facebook.net",
                "ads.twitter.com", "analytics.twitter.com", "twitter.com/i/adsct",
                "bing.com/ads", "msads.net", "msn.com/ads",
                "outbrain.com", "taboola.com", "revcontent.com", "mgid.com",
                "scorecardresearch.com", "quantserve.com", "chartbeat.com",
                "addthis.com", "sharethis.com", "sharethrough.com"
            };

            foreach (var domain in basicAdDomains)
            {
                _blockedDomains.Add(domain);
            }
            ResetDecisionCaches();

            // Load comprehensive ad URL patterns
            var basicAdPatterns = new[]
            {
                @".*\/ads\/.*", @".*\/ad\/.*", @".*\/advertisement\/.*",
                @".*\/banner\/.*", @".*\/popup\/.*", @".*\/tracking\/.*",
                @".*analytics.*", @".*doubleclick.*", @".*googlesyndication.*",
                @".*googleadservices.*", @".*\/ad\?.*", @".*\/ads\?.*",
                @".*\/advert\?.*", @".*\/track\?.*", @".*\/pixel\?.*"
            };

            foreach (var pattern in basicAdPatterns)
            {
                try
                {
                    _blockedPatterns.Add(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled));
                }
                catch (Exception)
                {
                    // Ignore invalid regex patterns found in the bundled list.
                }
            }

            // Load custom blocklist if exists
            await LoadCustomBlockListAsync();
        }

        private async Task LoadCustomBlockListAsync()
        {
            var blockListFile = "adblocker_hosts.txt";
            if (!File.Exists(blockListFile))
            {
                // Create default blocklist file
                await CreateStrictBlockListAsync(blockListFile);
            }

            try
            {
                var lines = await File.ReadAllLinesAsync(blockListFile);
                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    if (!string.IsNullOrEmpty(trimmedLine) && !trimmedLine.StartsWith("#"))
                    {
                        // Support hosts file format: "0.0.0.0 domain.com" or just "domain.com"
                        var parts = trimmedLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        var domain = parts.Length > 1 ? parts[1] : parts[0];
                        if (!string.IsNullOrEmpty(domain) && domain.Contains('.'))
                        {
                            _blockedDomains.Add(domain);
                            ResetDecisionCaches();
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Ignore blocklist load failures; fallback lists still apply.
            }
        }

        private async Task CreateStrictBlockListAsync(string fileName)
        {
            var strictHosts = @"# MyNewsFeeder Strict AdBlocker - Comprehensive Hosts File
# This file blocks ads, trackers, analytics, and social widgets

# Google Ads & Analytics
0.0.0.0 googleadservices.com
0.0.0.0 googlesyndication.com
0.0.0.0 doubleclick.net
0.0.0.0 google-analytics.com
0.0.0.0 googletagmanager.com
0.0.0.0 googletagservices.com

# Taboola/Outbrain (Content Networks)
0.0.0.0 taboola.com
0.0.0.0 vidstat.taboola.com
0.0.0.0 outbrain.com
0.0.0.0 videoexternalapi.outbrain.com
0.0.0.0 widgets.outbrain.com
0.0.0.0 amplify.outbrain.com

# Facebook/Meta Tracking
0.0.0.0 facebook.com/tr
0.0.0.0 connect.facebook.net
0.0.0.0 pixel.facebook.com
0.0.0.0 fbcdn.net
0.0.0.0 fbsbx.com

# Amazon Ads
0.0.0.0 amazon-adsystem.com
0.0.0.0 adsystem.amazon.com

# Analytics & Tracking
0.0.0.0 scorecardresearch.com
0.0.0.0 quantserve.com
0.0.0.0 chartbeat.com
0.0.0.0 hotjar.com
0.0.0.0 crazyegg.com
0.0.0.0 mouseflow.com

# Social Widgets
0.0.0.0 addthis.com
0.0.0.0 sharethis.com
0.0.0.0 sharethrough.com

# Add your custom domains below:
";

            try
            {
                await File.WriteAllTextAsync(fileName, strictHosts);
            }
            catch (Exception)
            {
                // Ignore errors while creating the default blocklist file.
            }
        }

        // Download and parse popular filter lists
        private async Task<AdBlockerUpdateResult> UpdateFilterListsAsync()
        {
            var cacheDir = "FilterLists";
            Directory.CreateDirectory(cacheDir);

            var downloaded = 0;
            var cached = 0;
            var failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var filterList in _filterLists)
            {
                try
                {
                    var cacheFile = Path.Combine(cacheDir, $"{filterList.Key}.txt");
                    var shouldUpdate = !File.Exists(cacheFile) ||
                                       File.GetLastWriteTime(cacheFile) < DateTime.Now.AddDays(-1);

                    if (shouldUpdate)
                    {
                        var success = await DownloadWithRetry(filterList.Value, cacheFile, filterList.Key);
                        if (success)
                        {
                            downloaded++;
                            await ParseFilterListAsync(cacheFile);
                        }
                        else
                        {
                            failed.Add(filterList.Key);
                        }
                    }
                    else
                    {
                        cached++;
                        await ParseFilterListAsync(cacheFile);
                    }
                }
                catch
                {
                    failed.Add(filterList.Key);
                }
            }

            return new AdBlockerUpdateResult(downloaded, cached, failed.Count == 0 ? Array.Empty<string>() : new List<string>(failed));
        }

        // New method with retry logic
        private async Task<bool> DownloadWithRetry(string url, string cacheFile, string listName, int maxRetries = 3)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    using var response = await _httpClient.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        await File.WriteAllTextAsync(cacheFile, content);
                        return true;
                    }
                }
                catch (HttpRequestException)
                {
                    // Retry on transient network errors.
                }
                catch (Exception)
                {
                    break; // Don't retry for unexpected errors
                }

                if (attempt < maxRetries)
                {
                    await Task.Delay(2000 * attempt); // Progressive delay: 2s, 4s, 6s
                }
            }

            return false;
        }

        // Parse different filter list formats
        private async Task ParseFilterListAsync(string filePath)
        {
            try
            {
                var lines = await File.ReadAllLinesAsync(filePath);
                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    // Skip comments and empty lines
                    if (string.IsNullOrEmpty(trimmedLine) ||
                        trimmedLine.StartsWith("!") ||
                        trimmedLine.StartsWith("#") ||
                        trimmedLine.StartsWith("["))
                        continue;

                    // Parse EasyList format (||domain.com^)
                    if (trimmedLine.StartsWith("||") && trimmedLine.Contains("^"))
                    {
                        var domain = trimmedLine.Substring(2, trimmedLine.IndexOf("^") - 2);
                        if (IsValidDomain(domain))
                        {
                            _blockedDomains.Add(domain);
                            ResetDecisionCaches();
                        }
                    }
                    // Parse hosts file format (0.0.0.0 domain.com)
                    else if (trimmedLine.StartsWith("0.0.0.0") || trimmedLine.StartsWith("127.0.0.1"))
                    {
                        var parts = trimmedLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 && IsValidDomain(parts[1]))
                        {
                            _blockedDomains.Add(parts[1]);
                            ResetDecisionCaches();
                        }
                    }
                    // Parse URL patterns
                    else if (trimmedLine.Contains("*") || trimmedLine.Contains("?"))
                    {
                        try
                        {
                            var pattern = ConvertToRegex(trimmedLine);
                            _blockedPatterns.Add(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled));
                        }
                        catch
                        {
                            // Skip invalid patterns
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Ignore parse failures for individual list files.
            }
        }

        // Convert AdBlock Plus patterns to regex
        private string ConvertToRegex(string pattern)
        {
            pattern = Regex.Escape(pattern);
            pattern = pattern.Replace(@"\*", ".*");
            pattern = pattern.Replace(@"\^", @"[^\w\-.%]");
            pattern = pattern.Replace(@"\|", @"\b");
            return $"^{pattern}";
        }

        private bool IsValidDomain(string domain)
        {
            return !string.IsNullOrEmpty(domain) &&
                   domain.Contains('.') &&
                   !domain.Contains(' ') &&
                   domain.Length > 3;
        }

        // ENHANCED: Comprehensive URL blocking without diagnostics
        public bool ShouldBlockUrl(string url, string documentUrl = null)
        {
            if (!_isInitialized || string.IsNullOrEmpty(url))
                return false;

            var documentHost = GetHost(documentUrl);
            var cacheKey = string.IsNullOrWhiteSpace(documentHost)
                ? url
                : $"{documentHost}|{url}";

            if (_urlDecisionCache.TryGetValue(cacheKey, out var cachedDecision))
            {
                return cachedDecision;
            }

            try
            {
                var uri = new Uri(url);
                var host = uri.Host.ToLowerInvariant();
                var fullUrl = url.ToLowerInvariant();

                // Skip blocking if the host is explicitly allowed.
                if (IsAllowedHost(host))
                {
                    RememberUrlDecision(cacheKey, false);
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(documentHost) &&
                    string.Equals(host, documentHost, StringComparison.OrdinalIgnoreCase) &&
                    !IsBlockedHost(host) &&
                    !MayRequirePatternScan(fullUrl))
                {
                    RememberUrlDecision(cacheKey, false);
                    return false;
                }

                // ENHANCED: Specific ad domain checks (including Taboola/Outbrain)
                var specificAdDomains = new[]
                {
                    "googleads.g.doubleclick.net",
                    "doubleclick.net",
                    "googlesyndication.com",
                    "googleadservices.com",
                    "google-analytics.com",
                    "googletagmanager.com",
                    "googletagservices.com",
                    "vidstat.taboola.com",           // From your logs
                    "videoexternalapi.outbrain.com", // From your logs
                    "taboola.com",
                    "outbrain.com"
                };

                foreach (var domain in specificAdDomains)
                {
                    if (host == domain || host.EndsWith("." + domain))
                    {
                        RememberUrlDecision(cacheKey, true);
                        return true;
                    }
                }

                // ENHANCED: URL pattern checks (including block-data patterns)
                var adPatterns = new[]
                {
                    "block-data",      // For Taboola URLs
                    "blocked-data",    // For Outbrain URLs
                    "/aclk?",          // DoubleClick ad click
                    "/pagead/",        // Google PageAd
                    "/ads/",
                    "/ad/",
                    "advertisement",
                    "doubleclick",
                    "googlesyndication",
                    "gclid=",          // Google Click ID
                    "utm_source=adwords",
                    "utm_medium=cpc"
                };

                foreach (var pattern in adPatterns)
                {
                    if (fullUrl.Contains(pattern))
                    {
                        RememberUrlDecision(cacheKey, true);
                        return true;
                    }
                }

                if (IsBlockedHost(host))
                {
                    RememberUrlDecision(cacheKey, true);
                    return true;
                }

                if (!MayRequirePatternScan(fullUrl))
                {
                    RememberUrlDecision(cacheKey, false);
                    return false;
                }

                // Check URL patterns from filter lists
                foreach (var pattern in _blockedPatterns)
                {
                    if (pattern.IsMatch(fullUrl))
                    {
                        RememberUrlDecision(cacheKey, true);
                        return true;
                    }
                }

                RememberUrlDecision(cacheKey, false);
                return false;
            }
            catch (Exception)
            {
                RememberUrlDecision(cacheKey, false);
                return false;
            }
        }

        private static bool MayRequirePatternScan(string fullUrl)
        {
            if (string.IsNullOrWhiteSpace(fullUrl))
            {
                return false;
            }

            foreach (var indicator in FastPatternIndicators)
            {
                if (fullUrl.Contains(indicator, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsAllowedHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            return _allowedHostCache.GetOrAdd(host, static (key, state) => IsDomainOrParentListed(key, state), _allowedDomains);
        }

        private bool IsBlockedHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            return _blockedHostCache.GetOrAdd(host, static (key, state) => IsDomainOrParentListed(key, state), _blockedDomains);
        }

        private static bool IsDomainOrParentListed(string host, HashSet<string> domainSet)
        {
            if (string.IsNullOrWhiteSpace(host) || domainSet == null || domainSet.Count == 0)
            {
                return false;
            }

            if (domainSet.Contains(host))
            {
                return true;
            }

            var startIndex = 0;
            while (startIndex >= 0 && startIndex < host.Length)
            {
                var dotIndex = host.IndexOf('.', startIndex);
                if (dotIndex < 0 || dotIndex + 1 >= host.Length)
                {
                    break;
                }

                var suffix = host[(dotIndex + 1)..];
                if (domainSet.Contains(suffix))
                {
                    return true;
                }

                startIndex = dotIndex + 1;
            }

            return false;
        }

        private void RememberUrlDecision(string cacheKey, bool blocked)
        {
            if (string.IsNullOrWhiteSpace(cacheKey))
            {
                return;
            }

            if (_urlDecisionCache.Count > 5000)
            {
                _urlDecisionCache.Clear();
            }

            _urlDecisionCache[cacheKey] = blocked;
        }

        private static string GetHost(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            try
            {
                return new Uri(url).Host.ToLowerInvariant();
            }
            catch
            {
                return string.Empty;
            }
        }

        private void ResetDecisionCaches()
        {
            _urlDecisionCache.Clear();
            _blockedHostCache.Clear();
            _allowedHostCache.Clear();
        }

        // Manual filter list update
        public async Task<AdBlockerUpdateResult> UpdateFilterListsManuallyAsync()
        {
            return await UpdateFilterListsAsync();
        }

        // Get statistics with detailed information
        public (int domains, int patterns, DateTime lastUpdate, List<string> availableLists, List<string> failedLists) GetDetailedStats()
        {
            var cacheDir = "FilterLists";
            var lastUpdate = DateTime.MinValue;
            var availableLists = new List<string>();
            var failedLists = new List<string>();

            if (Directory.Exists(cacheDir))
            {
                foreach (var filterList in _filterLists)
                {
                    var cacheFile = Path.Combine(cacheDir, $"{filterList.Key}.txt");
                    if (File.Exists(cacheFile))
                    {
                        var fileTime = File.GetLastWriteTime(cacheFile);
                        if (fileTime > lastUpdate)
                        {
                            lastUpdate = fileTime;
                        }
                        availableLists.Add($"{filterList.Key} ({fileTime:yyyy-MM-dd HH:mm})");
                    }
                    else
                    {
                        failedLists.Add(filterList.Key);
                    }
                }
            }

            return (_blockedDomains.Count, _blockedPatterns.Count, lastUpdate, availableLists, failedLists);
        }

        // Allowlist management (for domains that must not be blocked)
        public void AddAllowedDomain(string domain)
        {
            if (!string.IsNullOrWhiteSpace(domain))
            {
                _allowedDomains.Add(domain.Trim().ToLowerInvariant());
                ResetDecisionCaches();
            }
        }

        public void RemoveAllowedDomain(string domain)
        {
            if (!string.IsNullOrWhiteSpace(domain))
            {
                _allowedDomains.Remove(domain.Trim().ToLowerInvariant());
                ResetDecisionCaches();
            }
        }

        // Domain management methods
        public void AddCustomDomain(string domain)
        {
            if (!string.IsNullOrEmpty(domain))
            {
                _blockedDomains.Add(domain.ToLowerInvariant());
                ResetDecisionCaches();
            }
        }

        public void RemoveCustomDomain(string domain)
        {
            if (!string.IsNullOrEmpty(domain))
            {
                _blockedDomains.Remove(domain.ToLowerInvariant());
                ResetDecisionCaches();
            }
        }

        // Statistics methods
        public int GetBlockedDomainsCount() => _blockedDomains.Count;
        public int GetBlockedPatternsCount() => _blockedPatterns.Count;

        // Cleanup
        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    public sealed record AdBlockerUpdateResult(int DownloadedLists, int CachedLists, IReadOnlyList<string> FailedLists);
}
