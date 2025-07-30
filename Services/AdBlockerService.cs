using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
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
        private readonly HashSet<string> _blockedDomains;
        private readonly List<Regex> _blockedPatterns;
        private readonly HttpClient _httpClient;
        private bool _isInitialized = false;

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
            System.Diagnostics.Debug.WriteLine("🔧 Loading basic ad domains synchronously...");

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
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error compiling pattern {pattern}: {ex.Message}");
                }
            }

            // Mark as initialized immediately for basic blocking
            _isInitialized = true;

            System.Diagnostics.Debug.WriteLine($"✅ Basic AdBlocker initialized with {_blockedDomains.Count} domains and {_blockedPatterns.Count} patterns");
        }

        private async Task InitializeAsync()
        {
            try
            {
                await LoadLocalBlockListsAsync();
                await UpdateFilterListsAsync();
                System.Diagnostics.Debug.WriteLine($"✅ Enhanced AdBlocker initialized with {_blockedDomains.Count} domains and {_blockedPatterns.Count} patterns.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Failed to initialize enhanced AdBlocker: {ex.Message}");
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
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Invalid regex pattern: {pattern}, Error: {ex.Message}");
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
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading custom blocklist: {ex.Message}");
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
                System.Diagnostics.Debug.WriteLine($"✅ Created comprehensive strict blocklist file: {fileName}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Failed to create strict blocklist: {ex.Message}");
            }
        }

        // Download and parse popular filter lists
        private async Task UpdateFilterListsAsync()
        {
            var cacheDir = "FilterLists";
            Directory.CreateDirectory(cacheDir);
            var successCount = 0;
            var errorCount = 0;
            var errors = new List<string>();

            foreach (var filterList in _filterLists)
            {
                try
                {
                    var cacheFile = Path.Combine(cacheDir, $"{filterList.Key}.txt");
                    var shouldUpdate = !File.Exists(cacheFile) ||
                                     File.GetLastWriteTime(cacheFile) < DateTime.Now.AddDays(-1);

                    if (shouldUpdate)
                    {
                        System.Diagnostics.Debug.WriteLine($"Updating filter list: {filterList.Key}");
                        // Retry logic for failed downloads
                        var success = await DownloadWithRetry(filterList.Value, cacheFile, filterList.Key);
                        if (success)
                        {
                            successCount++;
                            await ParseFilterListAsync(cacheFile);
                        }
                        else
                        {
                            errorCount++;
                            errors.Add(filterList.Key);
                        }
                    }
                    else
                    {
                        // Parse existing cached file
                        await ParseFilterListAsync(cacheFile);
                        System.Diagnostics.Debug.WriteLine($"Using cached filter list: {filterList.Key}");
                    }
                }
                catch (Exception ex)
                {
                    errorCount++;
                    errors.Add(filterList.Key);
                    System.Diagnostics.Debug.WriteLine($"Error updating {filterList.Key}: {ex.Message}");
                }
            }

            System.Diagnostics.Debug.WriteLine($"Filter list update completed. Success: {successCount}, Errors: {errorCount}");
            if (errors.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"Failed lists: {string.Join(", ", errors)}");
            }
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
                        System.Diagnostics.Debug.WriteLine($"✅ Successfully downloaded {listName} (attempt {attempt})");
                        return true;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ HTTP {response.StatusCode} for {listName} (attempt {attempt})");
                        if (attempt == maxRetries)
                        {
                            System.Diagnostics.Debug.WriteLine($"❌ Failed to download {listName} after {maxRetries} attempts: {response.StatusCode}");
                        }
                    }
                }
                catch (HttpRequestException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Network error downloading {listName} (attempt {attempt}): {ex.Message}");
                    if (attempt == maxRetries)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Failed to download {listName} after {maxRetries} attempts due to network errors");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Unexpected error downloading {listName} (attempt {attempt}): {ex.Message}");
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
                        }
                    }
                    // Parse hosts file format (0.0.0.0 domain.com)
                    else if (trimmedLine.StartsWith("0.0.0.0") || trimmedLine.StartsWith("127.0.0.1"))
                    {
                        var parts = trimmedLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 && IsValidDomain(parts[1]))
                        {
                            _blockedDomains.Add(parts[1]);
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing filter list {filePath}: {ex.Message}");
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

        // ENHANCED: Comprehensive URL blocking with detailed logging
        public bool ShouldBlockUrl(string url)
        {
            if (!_isInitialized || string.IsNullOrEmpty(url))
                return false;

            try
            {
                var uri = new Uri(url);
                var host = uri.Host.ToLowerInvariant();
                var fullUrl = url.ToLowerInvariant();

                System.Diagnostics.Debug.WriteLine($"🔍 Checking URL: {url}");
                System.Diagnostics.Debug.WriteLine($"🔍 Host: {host}");
                System.Diagnostics.Debug.WriteLine($"🔍 Total domains: {_blockedDomains.Count}");

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
                        System.Diagnostics.Debug.WriteLine($"🚫 BLOCKED (Specific Ad Domain): {url}");
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
                        System.Diagnostics.Debug.WriteLine($"🚫 BLOCKED (Pattern '{pattern}'): {url}");
                        return true;
                    }
                }

                // Check exact domain match from filter lists
                if (_blockedDomains.Contains(host))
                {
                    System.Diagnostics.Debug.WriteLine($"🚫 BLOCKED (Domain): {host}");
                    return true;
                }

                // Check subdomain matches from filter lists
                foreach (var blockedDomain in _blockedDomains)
                {
                    if (host.EndsWith("." + blockedDomain))
                    {
                        System.Diagnostics.Debug.WriteLine($"🚫 BLOCKED (Subdomain): {host} -> {blockedDomain}");
                        return true;
                    }
                }

                // Check URL patterns from filter lists
                foreach (var pattern in _blockedPatterns)
                {
                    if (pattern.IsMatch(fullUrl))
                    {
                        System.Diagnostics.Debug.WriteLine($"🚫 BLOCKED (Filter Pattern): {url}");
                        return true;
                    }
                }

                System.Diagnostics.Debug.WriteLine($"✅ ALLOWED: {url}");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error checking URL {url}: {ex.Message}");
                return false;
            }
        }

        // Manual filter list update
        public async Task UpdateFilterListsManuallyAsync()
        {
            await UpdateFilterListsAsync();
            System.Diagnostics.Debug.WriteLine("✅ Filter lists updated manually.");
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

        // Domain management methods
        public void AddCustomDomain(string domain)
        {
            if (!string.IsNullOrEmpty(domain))
            {
                _blockedDomains.Add(domain.ToLowerInvariant());
                System.Diagnostics.Debug.WriteLine($"✅ Added custom blocked domain: {domain}");
            }
        }

        public void RemoveCustomDomain(string domain)
        {
            if (!string.IsNullOrEmpty(domain))
            {
                _blockedDomains.Remove(domain.ToLowerInvariant());
                System.Diagnostics.Debug.WriteLine($"✅ Removed blocked domain: {domain}");
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
}