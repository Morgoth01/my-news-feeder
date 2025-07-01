using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
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

        public BrowserService()
        {
            _adBlocker = new AdBlockerService();
            System.Diagnostics.Debug.WriteLine("✅ BrowserService created with AdBlocker");
        }

        public void SetWebView(WebView2 webView)
        {
            _linkWebView = webView;
            System.Diagnostics.Debug.WriteLine("🔧 SetWebView called");
            _ = InitializeWebViewAsync();
        }

        private async Task InitializeWebViewAsync()
        {
            if (_linkWebView == null)
            {
                System.Diagnostics.Debug.WriteLine("❌ WebView is null during initialization");
                return;
            }

            try
            {
                await _linkWebView.EnsureCoreWebView2Async();
                System.Diagnostics.Debug.WriteLine("✅ CoreWebView2 ensured");

                // Setup ad blocking for all requests
                _linkWebView.CoreWebView2.WebResourceRequested += OnWebResourceRequested;
                _linkWebView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                System.Diagnostics.Debug.WriteLine("✅ WebResourceRequested event handler registered");

                // Handle new window requests to apply ad blocking
                _linkWebView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
                System.Diagnostics.Debug.WriteLine("✅ NewWindowRequested event handler registered");

                // Navigation events for native dark mode only
                _linkWebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
                _linkWebView.CoreWebView2.DOMContentLoaded += OnDOMContentLoaded;

                // Enhanced security settings
                _linkWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                _linkWebView.CoreWebView2.Settings.IsZoomControlEnabled = true;
                _linkWebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
                _linkWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                _linkWebView.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
                _linkWebView.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
                _linkWebView.CoreWebView2.Settings.AreHostObjectsAllowed = false;

                // Apply JavaScript-based popup blocking in background
                _ = Task.Run(async () => await ApplyPopupBlockingScript());

                _isInitialized = true;
                System.Diagnostics.Debug.WriteLine("✅ External link WebView2 with enhanced AdBlocker initialized.");

                // Quick stats check without URL testing
                await QuickAdBlockerStatsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Failed to initialize WebView2: {ex.Message}");
            }
        }

        private Task QuickAdBlockerStatsAsync()
        {
            try
            {
                var stats = GetAdBlockerStats();
                System.Diagnostics.Debug.WriteLine($"📊 AdBlocker Ready - Domains: {stats.domains}, Patterns: {stats.patterns}");

                if (stats.domains == 0)
                {
                    System.Diagnostics.Debug.WriteLine("❌ CRITICAL: AdBlocker has no domains loaded!");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"✅ AdBlocker ready with {stats.domains} domains");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error checking AdBlocker stats: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        private void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            try
            {
                var url = e.Uri;
                System.Diagnostics.Debug.WriteLine($"🔗 New window requested: {url}");

                // Check if the URL should be blocked
                if (_adBlocker.ShouldBlockUrl(url))
                {
                    e.Handled = true; // Completely suppress the popup
                    System.Diagnostics.Debug.WriteLine($"🚫 Blocked new window: {url}");
                    return;
                }

                // Check for popup characteristics
                if (IsLikelyPopup(url))
                {
                    e.Handled = true; // Block likely popups
                    System.Diagnostics.Debug.WriteLine($"🚫 Blocked popup window: {url}");
                    return;
                }

                // For legitimate new windows, redirect to same WebView instead of opening new window
                e.Handled = true;
                _linkWebView.CoreWebView2.Navigate(url);
                System.Diagnostics.Debug.WriteLine($"↪️ Redirected new window to same WebView: {url}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error handling new window request: {ex.Message}");
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
                System.Diagnostics.Debug.WriteLine($"🔥 WebResourceRequested FIRED: {url}");

                // Check if AdBlocker is enabled
                if (!_adBlockerEnabled)
                {
                    System.Diagnostics.Debug.WriteLine($"⚪ AdBlocker disabled, allowing: {url}");
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

                    System.Diagnostics.Debug.WriteLine($"🚫 BLOCKED: {url}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"✅ ALLOWED: {url}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error in web resource blocking: {ex.Message}");
            }
        }

        // Navigate method with reduced delays
        public async void Navigate(string url, bool forceReload = false)
        {
            if (string.IsNullOrEmpty(url)) return;

            System.Diagnostics.Debug.WriteLine($"🌐 Navigate called: {url} (forceReload: {forceReload})");
            System.Diagnostics.Debug.WriteLine($"WebView ready: {_linkWebView?.CoreWebView2 != null}");
            System.Diagnostics.Debug.WriteLine($"Initialized: {_isInitialized}");

            if (_linkWebView?.CoreWebView2 != null && _isInitialized)
            {
                try
                {
                    if (forceReload)
                    {
                        // Reduced clear delay
                        System.Diagnostics.Debug.WriteLine("🧹 Force clearing browser for Always-On content");
                        _linkWebView.CoreWebView2.Navigate("about:blank");
                        await Task.Delay(100);
                        System.Diagnostics.Debug.WriteLine("✅ Browser cleared, proceeding with navigation");
                    }

                    // Simplified loading with reduced delay
                    var loadingHtml = CreateSimpleLoadingHtml();
                    _linkWebView.CoreWebView2.NavigateToString(loadingHtml);

                    // Reduced navigation delay
                    await Task.Delay(50);

                    // Navigate to the actual URL
                    _linkWebView.CoreWebView2.Navigate(url);
                    System.Diagnostics.Debug.WriteLine($"✅ Navigating to: {url}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ WebView2 navigation failed: {ex.Message}");
                    OpenInDefaultBrowser(url);
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("❌ WebView2 not ready, opening in default browser.");
                OpenInDefaultBrowser(url);
            }
        }

        // NavigateWithClear with reduced delays
        public async void NavigateWithClear(string url)
        {
            if (string.IsNullOrEmpty(url)) return;

            System.Diagnostics.Debug.WriteLine($"🧹 NavigateWithClear called: {url}");

            if (_linkWebView?.CoreWebView2 != null && _isInitialized)
            {
                try
                {
                    // Step 1: Clear browser content
                    _linkWebView.CoreWebView2.Navigate("about:blank");
                    System.Diagnostics.Debug.WriteLine("🧹 Browser cleared (about:blank)");

                    // Reduced clear wait time
                    await Task.Delay(200);

                    // Step 2: Navigate directly to actual URL
                    _linkWebView.CoreWebView2.Navigate(url);
                    System.Diagnostics.Debug.WriteLine($"✅ Navigating with clear to: {url}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ NavigateWithClear failed: {ex.Message}");
                    OpenInDefaultBrowser(url);
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("❌ WebView2 not ready for NavigateWithClear");
                OpenInDefaultBrowser(url);
            }
        }

        // Fast navigation without loading screen
        public void NavigateFast(string url)
        {
            if (string.IsNullOrEmpty(url)) return;

            System.Diagnostics.Debug.WriteLine($"🚀 Fast navigate called: {url}");

            if (_linkWebView?.CoreWebView2 != null && _isInitialized)
            {
                try
                {
                    // Direct navigation without loading screen
                    _linkWebView.CoreWebView2.Navigate(url);
                    System.Diagnostics.Debug.WriteLine($"✅ Fast navigating to: {url}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Fast navigation failed: {ex.Message}");
                    OpenInDefaultBrowser(url);
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("❌ WebView2 not ready for fast navigation");
                OpenInDefaultBrowser(url);
            }
        }

        private void OpenInDefaultBrowser(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                System.Diagnostics.Debug.WriteLine($"✅ Opened in default browser: {url}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Failed to open URL in default browser: {ex.Message}");
                System.Windows.MessageBox.Show($"Could not open URL: {url}\nError: {ex.Message}", "Error");
            }
        }

        public void NavigateToBlank()
        {
            if (_linkWebView?.CoreWebView2 != null && _isInitialized)
            {
                try
                {
                    _linkWebView.CoreWebView2.Navigate("about:blank");
                    System.Diagnostics.Debug.WriteLine("✅ Navigated to blank page");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Error navigating to blank: {ex.Message}");
                }
            }
        }

        public void GoBack()
        {
            try
            {
                if (_linkWebView?.CoreWebView2 != null && _linkWebView.CoreWebView2.CanGoBack)
                {
                    _linkWebView.CoreWebView2.GoBack();
                    System.Diagnostics.Debug.WriteLine("✅ Browser navigated back.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error going back: {ex.Message}");
            }
        }

        public void GoForward()
        {
            try
            {
                if (_linkWebView?.CoreWebView2 != null && _linkWebView.CoreWebView2.CanGoForward)
                {
                    _linkWebView.CoreWebView2.GoForward();
                    System.Diagnostics.Debug.WriteLine("✅ Browser navigated forward.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error going forward: {ex.Message}");
            }
        }

        public void Reload()
        {
            try
            {
                if (_linkWebView?.CoreWebView2 != null)
                {
                    _linkWebView.CoreWebView2.Reload();
                    System.Diagnostics.Debug.WriteLine("✅ Browser reloaded.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error reloading: {ex.Message}");
            }
        }

        public void SetDarkMode(bool enabled)
        {
            _darkModeEnabled = enabled;
            ApplyNativeDarkModeToWebView();
            System.Diagnostics.Debug.WriteLine($"🌙 Native dark mode set to: {enabled}");
        }

        public void SetAdBlockerEnabled(bool enabled)
        {
            _adBlockerEnabled = enabled;
            System.Diagnostics.Debug.WriteLine($"🔧 AdBlocker enabled set to: {enabled}");
        }

        // Native Dark Mode Only - No CSS Filters
        private void ApplyNativeDarkModeToWebView()
        {
            // Thread safety check
            if (!System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                System.Diagnostics.Debug.WriteLine("⚠️ ApplyNativeDarkModeToWebView called from background thread, dispatching to UI thread");
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
                        System.Diagnostics.Debug.WriteLine("✅ Native dark mode applied to WebView");
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
                        System.Diagnostics.Debug.WriteLine("✅ Native dark mode removed from WebView");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Error setting native dark mode: {ex.Message}");
                }
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

        public async Task UpdateAdBlockerListsAsync()
        {
            await _adBlocker.UpdateFilterListsManuallyAsync();
        }

        public async void ClearCache()
        {
            try
            {
                await ClearSelectiveCacheAsync();
                System.Diagnostics.Debug.WriteLine("✅ Browser cache cleared.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error clearing cache: {ex.Message}");
            }
        }

        public async Task ClearAllCacheAsync()
        {
            try
            {
                if (_linkWebView?.CoreWebView2?.Profile != null)
                {
                    await _linkWebView.CoreWebView2.Profile.ClearBrowsingDataAsync();
                    System.Diagnostics.Debug.WriteLine("✅ All WebView2 cache cleared");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error clearing cache: {ex.Message}");
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
                    System.Diagnostics.Debug.WriteLine("✅ Selective WebView2 cache cleared");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error clearing selective cache: {ex.Message}");
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
                    System.Diagnostics.Debug.WriteLine($"✅ Cache older than {hoursOld} hours cleared");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error clearing old cache: {ex.Message}");
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
                    System.Diagnostics.Debug.WriteLine("✅ Popup blocking script added.");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Error adding popup blocking script: {ex.Message}");
                }
            }
        }

        // Simplified loading HTML for faster rendering
        private string CreateSimpleLoadingHtml()
        {
            var backgroundColor = _darkModeEnabled ? "#121212" : "#f5f5f5";
            var textColor = _darkModeEnabled ? "#e0e0e0" : "#666";

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
    </style>
</head>
<body>
    <div class='loading'>
        <p>Loading...</p>
    </div>
</body>
</html>";
        }

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
                        System.Diagnostics.Debug.WriteLine("🌙 Native dark mode applied immediately after navigation");
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error in OnNavigationCompleted: {ex.Message}");
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
                    System.Diagnostics.Debug.WriteLine("🌙 Native dark mode applied on DOM content loaded");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error in OnDOMContentLoaded: {ex.Message}");
            }
        }
    }
}
