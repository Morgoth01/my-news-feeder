using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MyNewsFeeder.Services
{
    public sealed class BrowserSession : IDisposable
    {
        private readonly BrowserService _owner;
        private WebView2 _webView;
        private bool _isInitialized;
        private bool _darkModeEnabled;
        private bool _adBlockerEnabled;
        private readonly Stack<string> _backStack = new Stack<string>();
        private readonly Stack<string> _forwardStack = new Stack<string>();
        private bool _suppressHistoryPush;
        private string _currentUrl;
        private Task<bool> _initializationTask;
        private CoreWebView2 _wiredCore;
        private bool _mediaPlaybackEnabled;
        private string CurrentTransitionMaskColor => _darkModeEnabled ? "#111111" : "#ffffff";
        internal BrowserSession(BrowserService owner, WebView2 webView, bool darkModeEnabled, bool adBlockerEnabled)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _darkModeEnabled = darkModeEnabled;
            _adBlockerEnabled = adBlockerEnabled;

            if (webView != null)
            {
                SetWebView(webView);
            }
        }

        public WebView2 WebView => _webView;

        public CoreWebView2 CoreWebView2 => _webView?.CoreWebView2;
        public bool IsMediaPlaybackEnabled => _mediaPlaybackEnabled;

        public void SetWebView(WebView2 webView)
        {
            if (ReferenceEquals(_webView, webView))
            {
                return;
            }

            if (_wiredCore != null)
            {
                UnwireCoreWebView(_wiredCore);
            }

            _webView = webView;
            _isInitialized = false;
            _initializationTask = null;

            if (_webView?.CoreWebView2 != null)
            {
                WireCoreWebView(_webView.CoreWebView2);
                _isInitialized = true;
                _suppressHistoryPush = true;
                _currentUrl = _webView.Source?.ToString();
            }
        }

        public async Task<bool> EnsureInitializedAsync()
        {
            if (_isInitialized && _webView?.CoreWebView2 != null)
            {
                return true;
            }

            if (_webView == null)
            {
                return false;
            }

            _initializationTask ??= EnsureInitializedCoreAsync();
            return await _initializationTask.ConfigureAwait(true);
        }

        private async Task<bool> EnsureInitializedCoreAsync()
        {
            if (_webView == null)
            {
                _initializationTask = null;
                return false;
            }

            try
            {
                if (_webView.CoreWebView2 == null)
                {
                    var env = await _owner.GetSharedEnvironmentAsync().ConfigureAwait(true);
                    await _webView.EnsureCoreWebView2Async(env).ConfigureAwait(true);
                }

                if (_webView.CoreWebView2 != null)
                {
                    WireCoreWebView(_webView.CoreWebView2);
                    _isInitialized = true;
                    return true;
                }
            }
            catch
            {
                // Keep callers resilient and let them fall back to external open.
            }

            _initializationTask = null;
            return false;
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

        public void SetMediaPlaybackEnabled(bool enabled)
        {
            _mediaPlaybackEnabled = enabled;
        }

        public async Task PrimeAsync()
        {
            if (!await EnsureInitializedAsync().ConfigureAwait(true) || _webView?.CoreWebView2 == null)
            {
                return;
            }

            try
            {
                _suppressHistoryPush = true;
                _currentUrl = null;
                _webView.NavigateToString("<!DOCTYPE html><html><head><meta charset='utf-8'></head><body style='background:#111;'></body></html>");
                await Task.Delay(50).ConfigureAwait(true);
                _suppressHistoryPush = true;
                _webView.CoreWebView2.Navigate("about:blank");
            }
            catch
            {
                // Ignore prime failures; regular navigation remains available.
            }
        }

        public async Task<bool> NavigateFastAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            if (!await EnsureInitializedAsync().ConfigureAwait(true) || _webView?.CoreWebView2 == null)
            {
                OpenInDefaultBrowser(url);
                return false;
            }

            try
            {
                _backStack.Clear();
                _forwardStack.Clear();
                _suppressHistoryPush = true;
                _currentUrl = null;
                await MaskCurrentDocumentAsync().ConfigureAwait(true);
                _webView.NavigateToString(CreateTransitionHtml());
                await Task.Delay(35).ConfigureAwait(true);
                _webView.CoreWebView2.Navigate(url);
                return true;
            }
            catch
            {
                OpenInDefaultBrowser(url);
                return false;
            }
        }

        public void NavigateFast(string url)
        {
            _ = NavigateFastAsync(url);
        }

        public void GoBack()
        {
            try
            {
                if (_backStack.Count > 0 && _webView?.CoreWebView2 != null)
                {
                    var target = _backStack.Pop();
                    _forwardStack.Push(_currentUrl ?? string.Empty);
                    _suppressHistoryPush = true;
                    _webView.CoreWebView2.Navigate(target);
                }
            }
            catch
            {
            }
        }

        public void GoForward()
        {
            try
            {
                if (_forwardStack.Count > 0 && _webView?.CoreWebView2 != null)
                {
                    var target = _forwardStack.Pop();
                    if (!string.IsNullOrWhiteSpace(_currentUrl))
                    {
                        _backStack.Push(_currentUrl);
                    }

                    _suppressHistoryPush = true;
                    _webView.CoreWebView2.Navigate(target);
                }
            }
            catch
            {
            }
        }

        public void Reload()
        {
            try
            {
                _webView?.CoreWebView2?.Reload();
            }
            catch
            {
            }
        }

        public void NavigateToBlank()
        {
            try
            {
                _webView?.CoreWebView2?.Navigate("about:blank");
            }
            catch
            {
            }
        }

        public void NavigateToPlaceholder()
        {
            if (_webView == null)
            {
                return;
            }

            try
            {
                _suppressHistoryPush = true;
                _currentUrl = null;
                _webView.NavigateToString(CreatePlaceholderHtml());
            }
            catch
            {
            }
        }

        private void WireCoreWebView(CoreWebView2 core)
        {
            if (core == null || ReferenceEquals(_wiredCore, core))
            {
                return;
            }

            if (_wiredCore != null)
            {
                UnwireCoreWebView(_wiredCore);
            }

            _wiredCore = core;
            core.WebResourceRequested += OnWebResourceRequested;
            _owner.AddAdBlockFilters(core);
            core.NewWindowRequested += OnNewWindowRequested;
            core.NavigationCompleted += OnNavigationCompleted;
            core.NavigationStarting += OnNavigationStarting;

            ApplyPreferredColorScheme();

            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsZoomControlEnabled = true;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsStatusBarEnabled = false;

            if (string.IsNullOrWhiteSpace(_webView?.Source?.ToString()))
            {
                core.Navigate("about:blank");
            }
        }

        private void UnwireCoreWebView(CoreWebView2 core)
        {
            if (core == null)
            {
                return;
            }

            try { core.WebResourceRequested -= OnWebResourceRequested; } catch { }
            try { core.NewWindowRequested -= OnNewWindowRequested; } catch { }
            try { core.NavigationCompleted -= OnNavigationCompleted; } catch { }
            try { core.NavigationStarting -= OnNavigationStarting; } catch { }
            _wiredCore = null;
        }

        private void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            try
            {
                if (_adBlockerEnabled && _owner.ShouldBlockNewWindowRequest(e.Uri))
                {
                    e.Handled = true;
                    return;
                }

                e.Handled = true;
                _webView?.CoreWebView2?.Navigate(e.Uri);
            }
            catch
            {
            }
        }

        private void OnWebResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            try
            {
                var url = e.Request?.Uri;
                if (!_adBlockerEnabled || string.IsNullOrWhiteSpace(url))
                {
                    return;
                }

                if (_mediaPlaybackEnabled)
                {
                    return;
                }

                if (_owner.ShouldBlockResource(url, _currentUrl))
                {
                    e.Response = _webView.CoreWebView2.Environment.CreateWebResourceResponse(
                        null,
                        204,
                        "No Content",
                        "Content-Type: text/plain");
                }
            }
            catch
            {
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
            }
        }

        private void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!_mediaPlaybackEnabled || _webView?.CoreWebView2 == null || !e.IsSuccess)
            {
                return;
            }

            try
            {
                const string disableAutoplayScript = @"(() => {
    try {
        document.querySelectorAll('video, audio').forEach(media => {
            try {
                media.autoplay = false;
                media.removeAttribute('autoplay');
            } catch {}
        });
    } catch {}
})();";
                _ = _webView.CoreWebView2.ExecuteScriptAsync(disableAutoplayScript);
            }
            catch
            {
            }
        }

        private async Task MaskCurrentDocumentAsync()
        {
            if (_webView?.CoreWebView2 == null)
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
                await _webView.CoreWebView2.ExecuteScriptAsync(script).ConfigureAwait(true);
            }
            catch
            {
                // Ignore masking failures and continue with navigation.
            }
        }

        private void ApplyPreferredColorScheme()
        {
            try
            {
                var profile = _webView?.CoreWebView2?.Profile;
                if (profile != null)
                {
                    profile.PreferredColorScheme = _darkModeEnabled
                        ? CoreWebView2PreferredColorScheme.Dark
                        : CoreWebView2PreferredColorScheme.Light;
                }
            }
            catch
            {
            }
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

        private void OpenInDefaultBrowser(string url)
        {
            if (!_owner.TryOpenExternalLink(url) &&
                Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                MessageBox.Show(
                    $"Could not open URL: {uri}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        public void Dispose()
        {
            if (_wiredCore != null)
            {
                UnwireCoreWebView(_wiredCore);
            }
        }
    }
}