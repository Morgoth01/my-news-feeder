using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Resources;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MyNewsFeeder.Services
{
    /// <summary>
    /// Service for managing WebView2 browser functionality with integrated AdBlocker.
    /// Handles navigation, ad blocking, and popup prevention with native website dark mode only.
    /// </summary>
    public class BrowserService
    {
        private WebView2 _linkWebView; // For external links only
        private bool _isInitialized = false;
        private readonly AdBlockerService _adBlocker;
        private bool _darkModeEnabled = false;
        private bool _adBlockerEnabled = true;
        private CoreWebView2Environment _sharedEnvironment;
        private readonly Stack<string> _backStack = new Stack<string>();
        private readonly Stack<string> _forwardStack = new Stack<string>();
        private bool _suppressHistoryPush;
        private string _currentUrl;
        private static string _logoDataUri;
        private string _userDataFolder;
        private string CurrentTransitionMaskColor => _darkModeEnabled ? "#111111" : "#ffffff";
        private static readonly CoreWebView2WebResourceContext[] AdBlockResourceContexts =
        {
            CoreWebView2WebResourceContext.Document,
            CoreWebView2WebResourceContext.Script,
            CoreWebView2WebResourceContext.Stylesheet,
            CoreWebView2WebResourceContext.XmlHttpRequest,
            CoreWebView2WebResourceContext.Image,
            CoreWebView2WebResourceContext.Media,
            CoreWebView2WebResourceContext.Font
        };

        private static readonly string[] AllowedSchemes =
        {
            Uri.UriSchemeHttps
        };
        private static readonly string[] ExternalLinkWhitelistPrefixes =
        {
            "https://github.com/Morgoth01/my-news-feeder"
        };

        public BrowserService()
        {
            _adBlocker = new AdBlockerService();
        }

        public BrowserSession CreateSession(WebView2 webView = null)
        {
            return new BrowserSession(this, webView, _darkModeEnabled, _adBlockerEnabled);
        }

        public async Task PrimeAsync()
        {
            if (_linkWebView == null)
            {
                return;
            }

            try
            {
                if (!_isInitialized || _linkWebView.CoreWebView2 == null)
                {
                    await InitializeWebViewAsync();
                }

                if (_linkWebView?.CoreWebView2 == null)
                {
                    return;
                }

                _suppressHistoryPush = true;
                _currentUrl = null;
                _linkWebView.NavigateToString("<!DOCTYPE html><html><head><meta charset='utf-8'></head><body style='background:#111;'></body></html>");
                await Task.Delay(50);
                _suppressHistoryPush = true;
                _linkWebView.CoreWebView2.Navigate("about:blank");
            }
            catch
            {
                // Ignore prime failures; normal navigation still works.
            }
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

            uri = candidate;
            return true;
        }

        public bool ConfirmAndOpenExternal(string url, out bool userCancelled)
        {
            userCancelled = false;
            if (!TryGetAllowedUri(url, out var uri))
            {
                return false;
            }

            if (IsWhitelistedExternal(uri))
            {
                return TryOpenExternalLink(uri.AbsoluteUri);
            }

            var result = System.Windows.MessageBox.Show(
                $"Open external link?\n\n{uri.AbsoluteUri}",
                "Open external link",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning,
                System.Windows.MessageBoxResult.No);

            if (result != System.Windows.MessageBoxResult.Yes)
            {
                userCancelled = true;
                return false;
            }

            return TryOpenExternalLink(uri.AbsoluteUri);
        }

        private static bool IsWhitelistedExternal(Uri uri)
        {
            if (uri == null) return false;
            var absolute = uri.AbsoluteUri;
            return ExternalLinkWhitelistPrefixes.Any(prefix =>
                absolute.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        public void SetWebView(WebView2 webView)
        {
            _linkWebView = webView;
            if (_linkWebView?.CoreWebView2 != null)
            {
                WireCoreWebView(_linkWebView.CoreWebView2);
                _isInitialized = true;
                _suppressHistoryPush = true;
                _currentUrl = _linkWebView.Source?.ToString();
            }
            else
            {
                _ = InitializeWebViewAsync();
            }
        }

        private void WireCoreWebView(CoreWebView2 core)
        {
            // Setup ad blocking for all requests
            core.WebResourceRequested += OnWebResourceRequested;
            AddAdBlockFilters(core);

            // Handle new window requests to apply ad blocking
            core.NewWindowRequested += OnNewWindowRequested;

            // Navigation events for native dark mode fallback
            core.NavigationCompleted += OnNavigationCompleted;
            core.NavigationStarting += OnNavigationStarting;

            ApplyPreferredColorScheme();

            // Enhanced security settings
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsZoomControlEnabled = true;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsStatusBarEnabled = false;

            // Warm up renderer
            if (string.IsNullOrWhiteSpace(_linkWebView.Source?.ToString()))
            {
                core.Navigate("about:blank");
            }
        }

        internal void AddAdBlockFilters(CoreWebView2 core)
        {
            if (core == null)
            {
                return;
            }

            foreach (var context in AdBlockResourceContexts)
            {
                core.AddWebResourceRequestedFilter("*", context);
            }
        }

        internal bool ShouldBlockNewWindowRequest(string url)
        {
            if (_adBlocker.ShouldBlockUrl(url))
            {
                return true;
            }

            return IsLikelyPopup(url);
        }

        internal bool ShouldBlockResource(string url, string currentUrl)
        {
            if (!_adBlockerEnabled || string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            return _adBlocker.ShouldBlockUrl(url, currentUrl);
        }

        internal async Task<CoreWebView2Environment> GetSharedEnvironmentAsync()
        {
            if (_sharedEnvironment != null) return _sharedEnvironment;

            _userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MyNewsFeeder",
                "WebView2Cache");

            Directory.CreateDirectory(_userDataFolder);

            _sharedEnvironment = await CoreWebView2Environment.CreateAsync(userDataFolder: _userDataFolder);
            return _sharedEnvironment;
        }

        private async Task InitializeWebViewAsync()
        {
            if (_linkWebView == null)
            {
                return;
            }

            try
            {
                // If already initialized elsewhere (e.g., MainWindow), skip re-init
                if (_linkWebView.CoreWebView2 == null)
                {
                    var env = await GetSharedEnvironmentAsync();
                    await _linkWebView.EnsureCoreWebView2Async(env);
                    WireCoreWebView(_linkWebView.CoreWebView2);
                }

                _isInitialized = true;

            }
            catch (Exception)
            {
                // Ignore initialization failures; WebView will remain unavailable.
            }
        }

        private void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            try
            {
                var url = e.Uri;

                if (ShouldBlockNewWindowRequest(url))
                {
                    e.Handled = true; // Completely suppress the popup
                    return;
                }

                // For legitimate new windows, redirect to same WebView instead of opening new window
                e.Handled = true;
                _linkWebView.CoreWebView2.Navigate(url);
            }
            catch (Exception)
            {
                // Swallow popup handling errors to avoid crashing the app.
            }
        }

        internal bool IsLikelyPopup(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;

            var lowerUrl = url.ToLowerInvariant();

            // Common popup indicators
            var popupIndicators = new[]
            {
                "popup", "pop-up", "popunder", "overlay", "modal",
                "advertisement", "ads", "banner", "promo", "offer",
                "survey", "feedback", "newsletter", "subscribe"
            };

            return popupIndicators.Any(indicator => lowerUrl.Contains(indicator));
        }

        private void OnWebResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            try
            {
                var url = e.Request.Uri;

                if (ShouldBlockResource(url, _currentUrl))
                {
                    // Create proper blocking response with 204 No Content
                    e.Response = _linkWebView.CoreWebView2.Environment.CreateWebResourceResponse(
                        null, 204, "No Content", "Content-Type: text/plain");
                }
            }
            catch (Exception)
            {
                // Ignore ad-blocking failures and allow the request to continue.
            }
        }

        // Navigate method with reduced delays
        public async void Navigate(string url, bool forceReload = false)
        {
            if (string.IsNullOrEmpty(url)) return;


            if (_linkWebView?.CoreWebView2 != null && _isInitialized)
            {
                try
                {
                    // new article: clear history stacks
                    _backStack.Clear();
                    _forwardStack.Clear();
                    _suppressHistoryPush = true;
                    await MaskCurrentDocumentAsync();

                    if (forceReload)
                    {
                        // Reduced clear delay
                        _linkWebView.CoreWebView2.Navigate("about:blank");
                        await Task.Delay(100);
                    }

                    // Simplified loading with reduced delay
                    var loadingHtml = CreateSimpleLoadingHtml();
                    _linkWebView.CoreWebView2.NavigateToString(loadingHtml);

                    // Reduced navigation delay
                    await Task.Delay(50);

                    // Navigate to the actual URL
                    _linkWebView.CoreWebView2.Navigate(url);
                }
                catch (Exception)
                {
                    OpenInDefaultBrowser(url);
                }
            }
            else
            {
                OpenInDefaultBrowser(url);
            }
        }

        // NavigateWithClear with reduced delays
        public async void NavigateWithClear(string url)
        {
            if (string.IsNullOrEmpty(url)) return;


            if (_linkWebView?.CoreWebView2 != null && _isInitialized)
            {
                try
                {
                    _backStack.Clear();
                    _forwardStack.Clear();
                    _suppressHistoryPush = true; // new article load
                    await MaskCurrentDocumentAsync();

                    // Step 1: Clear browser content
                    _linkWebView.CoreWebView2.Navigate("about:blank");

                    // Reduced clear wait time
                    await Task.Delay(200);

                    // Step 2: Navigate directly to actual URL
                    _linkWebView.CoreWebView2.Navigate(url);
                }
                catch (Exception)
                {
                    OpenInDefaultBrowser(url);
                }
            }
            else
            {
                OpenInDefaultBrowser(url);
            }
        }

        // Fast navigation without loading screen
        public async void NavigateFast(string url)
        {
            if (string.IsNullOrEmpty(url)) return;


            if (_linkWebView?.CoreWebView2 != null && _isInitialized)
            {
                try
                {
                    _backStack.Clear();
                    _forwardStack.Clear();
                    _suppressHistoryPush = true; // new article load
                    _currentUrl = null;
                    await MaskCurrentDocumentAsync();
                    _linkWebView.NavigateToString(CreateTransitionHtml());
                    await Task.Delay(35);
                    _linkWebView.CoreWebView2.Navigate(url);
                }
                catch (Exception)
                {
                    OpenInDefaultBrowser(url);
                }
            }
            else
            {
                OpenInDefaultBrowser(url);
            }
        }

        private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            try
            {
                var uri = e.Uri;
                if (string.IsNullOrWhiteSpace(uri))
                {
                    return;
                }

                if (_suppressHistoryPush)
                {
                    _suppressHistoryPush = false;
                    _currentUrl = uri;
                    return;
                }

                if (!string.IsNullOrWhiteSpace(_currentUrl))
                {
                    _backStack.Push(_currentUrl);
                }
                _forwardStack.Clear();
                _currentUrl = uri;
            }
            catch
            {
                // Ignore navigation tracking errors.
            }
        }

        public bool TryOpenExternalLink(string url)
        {
            if (!TryGetAllowedUri(url, out var allowedUri))
            {
                return false;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = allowedUri.AbsoluteUri,
                    UseShellExecute = true
                });

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void OpenInDefaultBrowser(string url)
        {
            if (!TryOpenExternalLink(url))
            {
                if (TryGetAllowedUri(url, out var allowedUri))
                {
                    System.Windows.MessageBox.Show(
                        $"Could not open URL: {allowedUri}",
                        "Error",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
            }
        }

        public void NavigateToBlank()
        {
            if (_linkWebView?.CoreWebView2 != null && _isInitialized)
            {
                try
                {
                    _linkWebView.CoreWebView2.Navigate("about:blank");
                }
                catch (Exception)
                {
                    // Ignore failures while navigating to a blank page.
                }
            }
        }

        public void NavigateToPlaceholder()
        {
            if (_linkWebView == null)
            {
                return;
            }

            try
            {
                _suppressHistoryPush = true;
                _currentUrl = null;
                _linkWebView.NavigateToString(CreatePlaceholderHtml());
            }
            catch
            {
                // Ignore failures while navigating to placeholder content.
            }
        }

        public void GoBack()
        {
            try
            {
                if (_backStack.Count > 0 && _linkWebView?.CoreWebView2 != null)
                {
                    var target = _backStack.Pop();
                    _forwardStack.Push(_currentUrl ?? string.Empty);
                    _suppressHistoryPush = true;
                    _linkWebView.CoreWebView2.Navigate(target);
                }
            }
            catch
            {
                // Ignore navigation history errors.
            }
        }

        public void GoForward()
        {
            try
            {
                if (_forwardStack.Count > 0 && _linkWebView?.CoreWebView2 != null)
                {
                    var target = _forwardStack.Pop();
                    if (!string.IsNullOrWhiteSpace(_currentUrl))
                    {
                        _backStack.Push(_currentUrl);
                    }
                    _suppressHistoryPush = true;
                    _linkWebView.CoreWebView2.Navigate(target);
                }
            }
            catch (Exception)
            {
                // Ignore navigation history errors.
            }
        }

        public void Reload()
        {
            try
            {
                if (_linkWebView?.CoreWebView2 != null)
                {
                    _linkWebView.CoreWebView2.Reload();
                }
            }
            catch (Exception)
            {
                // Ignore reload failures.
            }
        }

        public void SetDarkMode(bool enabled)
        {
            _darkModeEnabled = enabled;
            ApplyPreferredColorScheme();
        }

        public void SetAdBlockerEnabled(bool enabled)
        {
            _adBlockerEnabled = enabled;
        }

        internal AdBlockerService SharedAdBlocker => _adBlocker;

        internal bool CurrentDarkModeEnabled => _darkModeEnabled;

        internal bool CurrentAdBlockerEnabled => _adBlockerEnabled;

        /// <summary>
        /// Ensure the WebView reports the correct preferred color scheme to pages.
        /// </summary>
        private void ApplyPreferredColorScheme()
        {
            try
            {
                var profile = _linkWebView?.CoreWebView2?.Profile;
                if (profile != null)
                {
                    profile.PreferredColorScheme = _darkModeEnabled
                        ? CoreWebView2PreferredColorScheme.Dark
                        : CoreWebView2PreferredColorScheme.Light;
                }
            }
            catch
            {
                // Older runtimes may not support PreferredColorScheme; ignore and continue.
            }
        }

        public void AddBlockedDomain(string domain)
        {
            _adBlocker.AddCustomDomain(domain);
        }

        public void RemoveBlockedDomain(string domain)
        {
            _adBlocker.RemoveCustomDomain(domain);
        }

        public (int domains, int patterns) GetAdBlockerStats()
        {
            return (_adBlocker.GetBlockedDomainsCount(), _adBlocker.GetBlockedPatternsCount());
        }

        public (int domains, int patterns, DateTime lastUpdate, List<string> availableLists, List<string> failedLists) GetDetailedAdBlockerStats()
        {
            return _adBlocker.GetDetailedStats();
        }

        public Task<AdBlockerUpdateResult> UpdateAdBlockerListsAsync()
        {
            return _adBlocker.UpdateFilterListsManuallyAsync();
        }

        public async void ClearCache()
        {
            try
            {
                await ClearSelectiveCacheAsync();
                PruneDiskCache();
            }
            catch (Exception)
            {
                // Ignore cache clear failures.
            }
        }

        public async Task ClearAllCacheAsync()
        {
            try
            {
                if (_linkWebView?.CoreWebView2?.Profile != null)
                {
                    await _linkWebView.CoreWebView2.Profile.ClearBrowsingDataAsync();
                }
                PruneDiskCache();
            }
            catch (Exception)
            {
                // Ignore cache clear failures.
            }
        }

        public async Task ClearSelectiveCacheAsync()
        {
            try
            {
                if (_linkWebView?.CoreWebView2?.Profile != null)
                {
                    // Use correct enum values for newer WebView2 versions
                    var dataKinds = CoreWebView2BrowsingDataKinds.FileSystems |
                                   CoreWebView2BrowsingDataKinds.IndexedDb |
                                   CoreWebView2BrowsingDataKinds.LocalStorage |
                                   CoreWebView2BrowsingDataKinds.WebSql |
                                   CoreWebView2BrowsingDataKinds.CacheStorage |
                                   CoreWebView2BrowsingDataKinds.AllDomStorage;

                    await _linkWebView.CoreWebView2.Profile.ClearBrowsingDataAsync(dataKinds);
                }
                PruneDiskCache();
            }
            catch (Exception)
            {
                // Ignore cache clear failures.
            }
        }

        public async Task ClearOldCacheAsync(int hoursOld = 24)
        {
            try
            {
                if (_linkWebView?.CoreWebView2?.Profile != null)
                {
                    var endTime = DateTime.Now;
                    var startTime = DateTime.Now.AddHours(-hoursOld);

                    // Use correct enum values
                    var dataKinds = CoreWebView2BrowsingDataKinds.FileSystems |
                                   CoreWebView2BrowsingDataKinds.CacheStorage;

                    await _linkWebView.CoreWebView2.Profile.ClearBrowsingDataAsync(dataKinds, startTime, endTime);
                }
                PruneDiskCache();
            }
            catch (Exception)
            {
                // Ignore cache clear failures.
            }
        }
        

        // Simplified loading HTML for faster rendering
        private string CreateSimpleLoadingHtml()
        {
            var backgroundColor = _darkModeEnabled ? "#121212" : "#f5f5f5";
            var textColor = _darkModeEnabled ? "#e0e0e0" : "#666";
            var accent = _darkModeEnabled ? "#8e8cd8" : "#7b5bd6";
            var logo = GetLogoDataUri();

            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='color-scheme' content='{(_darkModeEnabled ? "dark light" : "light dark")}'>
        <style>
            :root {{
                color-scheme: {(_darkModeEnabled ? "dark" : "light")};
            }}
            body {{
                font-family: 'Segoe UI', sans-serif;
                background-color: {backgroundColor};
                color: {textColor};
                display: flex;
                justify-content: center;
                align-items: center;
                height: 100vh;
                margin: 0;
            }}
            .loading {{
                text-align: center;
                font-size: 14px;
            }}
            .logo {{
                width: 72px;
                height: 72px;
                margin: 0 auto 12px auto;
                display: block;
                animation: spin 2.4s linear infinite;
                filter: drop-shadow(0 0 6px {accent});
            }}
            @keyframes spin {{
                from {{ transform: rotate(0deg); }}
                to   {{ transform: rotate(360deg); }}
            }}
        </style>
</head>
<body>
    <div class='loading'>
        {(logo != null ? $"<img class=\"logo\" src=\"{logo}\" alt=\"Loading\" />" : "")}
        <p>Loading…</p>
    </div>
</body>
</html>";
        }

        private string CreateTransitionHtml()
        {
            var backgroundColor = _darkModeEnabled ? "#121212" : "#f5f5f5";
            return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1'>
    <style>
        html, body {{
            margin: 0;
            width: 100%;
            height: 100%;
            background: {backgroundColor};
            overflow: hidden;
        }}
    </style>
</head>
<body></body>
</html>";
        }

        private string CreatePlaceholderHtml()
        {
            var backgroundColor = _darkModeEnabled ? "#121212" : "#f5f5f5";
            return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1'>
    <style>
        html, body {{
            margin: 0;
            width: 100%;
            height: 100%;
            background: {backgroundColor};
        }}
    </style>
</head>
<body></body>
</html>";
        }

        private string GetLogoDataUri()
        {
            if (_logoDataUri != null) return _logoDataUri;
            try
            {
                // Try embedded resource first (pack URI)
                var packUri = new Uri("pack://application:,,,/Resources/mynewsfeeder.ico", UriKind.Absolute);
                var resourceStream = Application.GetResourceStream(packUri);
                byte[] bytes = null;
                if (resourceStream != null)
                {
                    using var ms = new MemoryStream();
                    resourceStream.Stream.CopyTo(ms);
                    bytes = ms.ToArray();
                }
                else
                {
                    // Fallback to file in output/working directory
                    var exeDir = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
                    var path = Path.Combine(exeDir, "Resources", "mynewsfeeder.ico");
                    if (!File.Exists(path))
                    {
                        path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "mynewsfeeder.ico");
                        if (!File.Exists(path))
                        {
                            return null;
                        }
                    }
                    bytes = File.ReadAllBytes(path);
                }

                var b64 = Convert.ToBase64String(bytes);
                _logoDataUri = $"data:image/x-icon;base64,{b64}";
                return _logoDataUri;
            }
            catch
            {
                return null;
            }
        }

        private void PruneDiskCache()
        {
            try
            {
                var root = _userDataFolder;
                if (string.IsNullOrWhiteSpace(root))
                {
                    // Attempt to infer from shared environment if already created
                    root = _sharedEnvironment?.UserDataFolder;
                }

                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                {
                    return;
                }

                // WebView2 places caches under EBWebView/Default
                var targets = new[]
                {
                    Path.Combine(root, "EBWebView", "Default", "Cache"),
                    Path.Combine(root, "EBWebView", "Default", "Code Cache"),
                    Path.Combine(root, "EBWebView", "Default", "GPUCache"),
                    Path.Combine(root, "EBWebView", "Default", "Service Worker", "CacheStorage"),
                    Path.Combine(root, "EBWebView", "Crashpad"),
                };

                foreach (var dir in targets)
                {
                    TryDeleteDirectory(dir);
                }
            }
            catch
            {
                // ignore pruning failures
            }
        }

        private void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
                // ignore
            }
        }

        // Deprecated warm-up stub (kept to avoid re-adding usage warnings if referenced in the future)
        private Task PreWarmAsync() => Task.CompletedTask;

        // Navigation completed with immediate dark mode application
        private void OnNavigationCompleted(object sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
        {
        }

        private async Task MaskCurrentDocumentAsync()
        {
            if (_linkWebView?.CoreWebView2 == null)
            {
                return;
            }

            try
            {
                var color = CurrentTransitionMaskColor;
                var script = $@"(() => {{
    try {{
        const existing = document.getElementById('mnf-transition-mask');
        if (existing) existing.remove();
        const mask = document.createElement('div');
        mask.id = 'mnf-transition-mask';
        mask.style.position = 'fixed';
        mask.style.inset = '0';
        mask.style.background = '{color}';
        mask.style.opacity = '1';
        mask.style.pointerEvents = 'none';
        mask.style.zIndex = '2147483647';
        document.documentElement.style.background = '{color}';
        if (document.body) {{
            document.body.style.background = '{color}';
            document.body.appendChild(mask);
        }} else {{
            document.documentElement.appendChild(mask);
        }}
    }} catch (e) {{}}
}})();";
                await _linkWebView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch
            {
                // Ignore masking failures and continue with navigation.
            }
        }

        private void OnHistoryChanged(object sender, object e)
        {
            // No-op: history is trimmed during navigation completed to keep article history scoped.
        }

        // DevTools-based history helpers removed; custom stacks manage navigation now.
    }
}
