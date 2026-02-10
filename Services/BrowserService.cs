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
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);

            // Handle new window requests to apply ad blocking
            core.NewWindowRequested += OnNewWindowRequested;

            // Navigation events for native dark mode only
            core.NavigationCompleted += OnNavigationCompleted;
            core.DOMContentLoaded += OnDOMContentLoaded;
            core.NavigationStarting += OnNavigationStarting;

            ApplyPreferredColorScheme();

            // Enhanced security settings
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsZoomControlEnabled = true;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.AreDevToolsEnabled = true; // needed for devtools protocol calls
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsStatusBarEnabled = false;

            // Apply JavaScript-based popup blocking in background
            _ = Task.Run(async () => await ApplyPopupBlockingScript());

            // Warm up renderer
            if (string.IsNullOrWhiteSpace(_linkWebView.Source?.ToString()))
            {
                core.Navigate("about:blank");
            }
        }

        private async Task<CoreWebView2Environment> GetSharedEnvironmentAsync()
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

                // Check if the URL should be blocked
                if (_adBlocker.ShouldBlockUrl(url))
                {
                    e.Handled = true; // Completely suppress the popup
                    return;
                }

                // Check for popup characteristics
                if (IsLikelyPopup(url))
                {
                    e.Handled = true; // Block likely popups
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

        private bool IsLikelyPopup(string url)
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

                // Check if AdBlocker is enabled
                if (!_adBlockerEnabled)
                {
                    return;
                }

                // Set user agent for better compatibility
                e.Request.Headers.SetHeader("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

                if (_adBlocker.ShouldBlockUrl(url))
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
        public void NavigateFast(string url)
        {
            if (string.IsNullOrEmpty(url)) return;


            if (_linkWebView?.CoreWebView2 != null && _isInitialized)
            {
                try
                {
                    _backStack.Clear();
                    _forwardStack.Clear();
                    _suppressHistoryPush = true; // new article load

                    // Direct navigation without loading screen
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
            ApplyNativeDarkModeToWebView();
        }

        public void SetAdBlockerEnabled(bool enabled)
        {
            _adBlockerEnabled = enabled;
        }

        // Native Dark Mode Only - No CSS Filters
        private void ApplyNativeDarkModeToWebView()
        {
            // Thread safety check
            if (!System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() => ApplyNativeDarkModeToWebView());
                return;
            }

            if (_linkWebView?.CoreWebView2 != null)
            {
                try
                {
                    if (_darkModeEnabled)
                    {
                        // Native dark mode only - no CSS filters
                        var nativeDarkModeScript = @"
(function() {
    console.log('Applying native dark mode only...');
    
    // Remove any existing dark mode styles first
    var existingStyles = document.querySelectorAll('style[data-native-dark-mode]');
    existingStyles.forEach(function(style) {
        style.remove();
    });
    
    // Step 1: Check if website has native dark mode support
    const hasColorSchemeMeta = !!document.querySelector('meta[name=""color-scheme""]');
    const hasThemeColorMeta = !!document.querySelector('meta[name=""theme-color""]');
    let hasDarkModeCSS = false;
    
    // Check for CSS dark mode rules
    try {
        Array.from(document.styleSheets).forEach(sheet => {
            try {
                Array.from(sheet.cssRules || []).forEach(rule => {
                    if (rule.cssText && rule.cssText.includes('prefers-color-scheme: dark')) {
                        hasDarkModeCSS = true;
                    }
                });
            } catch(e) {}
        });
    } catch(e) {}
    
    // Check for common dark mode indicators
    const hasDataTheme = !!document.querySelector('[data-theme]');
    const hasDarkClass = !!document.querySelector('.dark, .dark-mode, .night-mode');
    
    // Website-specific detection
    const hostname = window.location.hostname.toLowerCase();
    const knownDarkModeSites = [
        'github.com', 'stackoverflow.com', 'reddit.com', 'twitter.com',
        'youtube.com', 'discord.com', 'slack.com', 'notion.so',
        'medium.com', 'dev.to', 'codepen.io'
    ];
    
    const isKnownDarkModeSite = knownDarkModeSites.some(site => hostname.includes(site));
    const hasNativeSupport = hasDarkModeCSS || hasColorSchemeMeta || isKnownDarkModeSite || hasDataTheme;
    
    console.log('Native dark mode detection:', {
        hasColorSchemeMeta: hasColorSchemeMeta,
        hasThemeColorMeta: hasThemeColorMeta,
        hasDarkModeCSS: hasDarkModeCSS,
        hasDataTheme: hasDataTheme,
        hasDarkClass: hasDarkClass,
        isKnownDarkModeSite: isKnownDarkModeSite,
        hasNativeSupport: hasNativeSupport,
        hostname: hostname
    });
    
    // ONLY apply native dark mode if website supports it
    if (hasNativeSupport) {
        console.log('Website supports native dark mode - applying enhancements');
        
        var style = document.createElement('style');
        style.setAttribute('data-native-dark-mode', 'true');
        
        // Set color scheme meta if not present
        if (!hasColorSchemeMeta) {
            var metaColorScheme = document.createElement('meta');
            metaColorScheme.name = 'color-scheme';
            metaColorScheme.content = 'dark light';
            document.head.appendChild(metaColorScheme);
        }
        
        // Apply website-specific dark mode triggers
        if (hostname.includes('github.com')) {
            document.documentElement.setAttribute('data-color-mode', 'dark');
            document.documentElement.setAttribute('data-dark-theme', 'dark');
        } else if (hostname.includes('stackoverflow.com')) {
            localStorage.setItem('so-theme', 'dark');
        } else if (hostname.includes('reddit.com')) {
            document.documentElement.setAttribute('data-theme', 'dark');
        } else if (hostname.includes('youtube.com')) {
            document.documentElement.setAttribute('dark', '');
        }
        
        style.innerHTML = `
            /* Native dark mode enhancements only */
            :root {
                color-scheme: dark !important;
            }
            
            html, body {
                color-scheme: dark !important;
            }
            
            /* Force dark mode preference */
            @media (prefers-color-scheme: light) {
                :root { 
                    color-scheme: dark !important; 
                }
            }
        `;
        
        // Add style to head
        if (document.head) {
            document.head.appendChild(style);
        } else {
            document.documentElement.appendChild(style);
        }
        
        // Set global indicators
        document.documentElement.style.setProperty('--webview-dark-mode', 'native');
        document.documentElement.setAttribute('data-dark-mode-type', 'native');
        
        console.log('Native dark mode applied successfully');
    } else {
        console.log('Website does not support native dark mode - no changes applied');
        document.documentElement.style.setProperty('--webview-dark-mode', 'not-supported');
        document.documentElement.setAttribute('data-dark-mode-type', 'not-supported');
    }
})();
";
                        _linkWebView.CoreWebView2.ExecuteScriptAsync(nativeDarkModeScript);
                    }
                    else
                    {
                        // Remove native dark mode
                        var removeNativeDarkModeScript = @"
(function() {
    console.log('Removing native dark mode...');
    
    // Remove all native dark mode styles
    var darkStyles = document.querySelectorAll('style[data-native-dark-mode]');
    darkStyles.forEach(function(style) {
        style.remove();
    });
    
    // Reset properties
    document.documentElement.style.removeProperty('--webview-dark-mode');
    document.documentElement.removeAttribute('data-dark-mode-type');
    
    // Reset website-specific dark mode attributes
    document.documentElement.removeAttribute('data-color-mode');
    document.documentElement.removeAttribute('data-dark-theme');
    document.documentElement.removeAttribute('data-theme');
    document.documentElement.removeAttribute('dark');
    
    // Reset color scheme to light
    var metaColorScheme = document.querySelector('meta[name=""color-scheme""]');
    if (metaColorScheme) {
        metaColorScheme.content = 'light';
    }
    
    console.log('Native dark mode removed successfully');
})();
";
                        _linkWebView.CoreWebView2.ExecuteScriptAsync(removeNativeDarkModeScript);
                    }
                }
                catch (Exception)
                {
                    // Ignore failures while applying native dark mode script.
                }
            }
        }

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
        

        private async Task ApplyPopupBlockingScript()
        {
            if (_linkWebView?.CoreWebView2 != null)
            {
                try
                {
                    var popupBlockingScript = @"
(function() {
    console.log('AdBlocker popup blocking script loaded');
    
    // Override window.open to prevent popups
    var originalOpen = window.open;
    window.open = function(url, name, features) {
        console.log('🚫 Popup blocked by script:', url);
        return null;
    };
    
    // Block common popup triggers
    document.addEventListener('click', function(e) {
        var target = e.target;
        if (target.tagName === 'A' && target.target === '_blank') {
            var href = target.href;
            if (href && (href.includes('popup') || href.includes('ad') || href.includes('doubleclick'))) {
                e.preventDefault();
                console.log('🚫 Popup link blocked by script:', href);
            }
        }
    });
    
    // Block setTimeout/setInterval based popups
    var originalSetTimeout = window.setTimeout;
    window.setTimeout = function(func, delay) {
        if (typeof func === 'string' && func.includes('window.open')) {
            console.log('🚫 Popup setTimeout blocked');
            return null;
        }
        return originalSetTimeout.apply(this, arguments);
    };
})();
";
                    await _linkWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(popupBlockingScript);
                }
                catch (Exception)
                {
                    // Ignore popup script injection failures.
                }
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
            try
            {
                if (e.IsSuccess && _darkModeEnabled)
                {
                    // Apply dark mode immediately without delay
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        ApplyNativeDarkModeToWebView();
                    });
                }
            }
            catch (Exception)
            {
                // Ignore navigation callbacks that fail during shutdown.
            }
        }

        private void OnDOMContentLoaded(object sender, Microsoft.Web.WebView2.Core.CoreWebView2DOMContentLoadedEventArgs e)
        {
            try
            {
                if (_darkModeEnabled)
                {
                    // Ensure we're on UI thread
                    if (System.Windows.Application.Current.Dispatcher.CheckAccess())
                    {
                        ApplyNativeDarkModeToWebView();
                    }
                    else
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            ApplyNativeDarkModeToWebView();
                        });
                    }
                }
            }
            catch (Exception)
            {
                // Ignore navigation callbacks that fail during shutdown.
            }
        }

        private void OnHistoryChanged(object sender, object e)
        {
            // No-op: history is trimmed during navigation completed to keep article history scoped.
        }

        // DevTools-based history helpers removed; custom stacks manage navigation now.
    }
}
