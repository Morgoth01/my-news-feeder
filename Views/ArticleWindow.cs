using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using MyNewsFeeder.Services;

namespace MyNewsFeeder.Views
{
    public partial class ArticleWindow : Window
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

        private readonly string _summaryArticleHtml;
        private readonly string _articleLink;
        private readonly bool _darkMode;
        private readonly BrowserSession _browserSession;
        private readonly bool _adBlockerEnabled;
        private readonly Action<string> _openExternalLinkWithPrompt;
        private readonly ArticleReaderService _articleReaderService;
        private ReaderArticleContent _readerContent;
        private bool _isShowingOriginalContent;
        private bool _isVideoPlaybackEnabled;
        private bool _isOriginalContentLoading;
        private bool _isReaderModeActive;
        private bool _isReaderModeLoading;
        private bool _isContentFullscreen;
        private WindowState _windowStateBeforeFullscreen;
        private WindowStyle _windowStyleBeforeFullscreen;
        private ResizeMode _resizeModeBeforeFullscreen;
        private Rect _boundsBeforeFullscreen;

        public ArticleWindow(
            string articleTitle,
            string articleHtml,
            string articleLink,
            bool darkMode,
            BrowserSession browserSession,
            bool adBlockerEnabled,
            Action<string> openExternalLinkWithPrompt)
        {
            InitializeComponent();

            _summaryArticleHtml = string.IsNullOrWhiteSpace(articleHtml)
                ? "<html><body><p>No article content available.</p></body></html>"
                : articleHtml;
            _articleLink = articleLink?.Trim() ?? string.Empty;
            _darkMode = darkMode;
            _browserSession = browserSession;
            _adBlockerEnabled = adBlockerEnabled;
            _openExternalLinkWithPrompt = openExternalLinkWithPrompt;
            _articleReaderService = new ArticleReaderService();

            var titleText = string.IsNullOrWhiteSpace(articleTitle) ? "Article" : articleTitle.Trim();
            Title = titleText;
            ArticleTitleText.Text = titleText;
            ArticleLinkText.Text = string.IsNullOrWhiteSpace(_articleLink) ? "No external link" : _articleLink;
            UpdateActionButtons();

            SourceInitialized += (_, __) => EnableDarkTitleBar();
            Loaded += ArticleWindow_Loaded;
            Closed += ArticleWindow_Closed;
        }

        private void EnableDarkTitleBar()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int useDark = 1;
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, Marshal.SizeOf<int>()) != 0)
            {
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref useDark, Marshal.SizeOf<int>());
            }
        }

        private async void ArticleWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _browserSession?.SetDarkMode(_darkMode);
                _browserSession?.SetAdBlockerEnabled(_adBlockerEnabled);
                _browserSession?.SetMediaPlaybackEnabled(_isVideoPlaybackEnabled);
                _browserSession?.SetWebView(ArticleContentWebView);

                if (_browserSession != null)
                {
                    await _browserSession.EnsureInitializedAsync();
                }

                if (ArticleContentWebView.CoreWebView2 != null)
                {
                    ArticleContentWebView.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
                    ArticleContentWebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
                    ArticleContentWebView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
                }

                NavigateSummaryContent();
            }
            catch (Exception)
            {
                ArticleContentWebView.NavigateToString("<html><body><p>Could not load article content.</p></body></html>");
            }
        }

        private void ArticleWindow_Closed(object sender, EventArgs e)
        {
            try
            {
                if (ArticleContentWebView?.CoreWebView2 == null)
                {
                    return;
                }

                ArticleContentWebView.CoreWebView2.NavigationStarting -= CoreWebView2_NavigationStarting;
                ArticleContentWebView.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
                ArticleContentWebView.CoreWebView2.NewWindowRequested -= CoreWebView2_NewWindowRequested;
            }
            catch
            {
                // Best effort cleanup only.
            }

            try
            {
                _browserSession?.Dispose();
            }
            catch
            {
                // Best effort cleanup only.
            }

            try
            {
                _articleReaderService?.Dispose();
            }
            catch
            {
                // Best effort cleanup only.
            }
        }

        private async void RefreshPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isOriginalContentLoading || _isReaderModeLoading || string.IsNullOrWhiteSpace(_articleLink))
            {
                return;
            }

            if (_isReaderModeActive)
            {
                _readerContent = null;
                await LoadReaderModeAsync(forceReaderRefresh: true);
                return;
            }

            if (_isShowingOriginalContent)
            {
                await ShowOriginalContentAsync();
            }
        }

        private async void ShowContentButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isOriginalContentLoading || _isReaderModeLoading)
            {
                return;
            }

            if (_isShowingOriginalContent && !_isReaderModeActive)
            {
                NavigateSummaryContent();
                return;
            }

            await ShowOriginalContentAsync();
        }

        private async void EnableVideoButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isVideoPlaybackEnabled || _isReaderModeActive || string.IsNullOrWhiteSpace(_articleLink))
            {
                return;
            }

            _isVideoPlaybackEnabled = true;
            _browserSession?.SetMediaPlaybackEnabled(true);
            await ShowOriginalContentAsync();
        }

        private async void ReaderModeButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadReaderModeAsync(forceReaderRefresh: false);
        }

        private async Task LoadReaderModeAsync(bool forceReaderRefresh)
        {
            if (_isReaderModeLoading || _isOriginalContentLoading)
            {
                return;
            }

            if (_isReaderModeActive)
            {
                NavigateSummaryContent();
                return;
            }

            if (string.IsNullOrWhiteSpace(_articleLink))
            {
                return;
            }

            _isReaderModeLoading = true;
            UpdateActionButtons();

            try
            {
                if (forceReaderRefresh)
                {
                    _readerContent = null;
                }

                ResetVideoPlaybackState();
                _readerContent ??= await _articleReaderService.ExtractAsync(_articleLink);
                if (_readerContent == null || string.IsNullOrWhiteSpace(_readerContent.HtmlContent))
                {
                    MessageBox.Show(
                        this,
                        "Reader Mode: full text could not be extracted for this article.",
                        "Reader Mode",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var readerHtml = BuildReaderModeHtml(_readerContent);
                _isShowingOriginalContent = false;
                _isOriginalContentLoading = false;
                ApplyWebViewModeSettings(enableScripts: false);
                ArticleContentWebView.NavigateToString(readerHtml);
                _isReaderModeActive = true;
            }
            catch
            {
                MessageBox.Show(
                    this,
                    "Reader Mode: failed to load article content.",
                    "Reader Mode",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                _isReaderModeLoading = false;
                UpdateActionButtons();
            }
        }

        private void CoreWebView2_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Uri))
            {
                return;
            }

            if (e.Uri.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase) ||
                e.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (_isShowingOriginalContent)
            {
                return;
            }

            e.Cancel = true;
            OpenExternalLink(e.Uri);
        }

        private void CoreWebView2_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!_isShowingOriginalContent)
            {
                return;
            }

            _isOriginalContentLoading = false;
            UpdateActionButtons();
        }

        private void CoreWebView2_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            OpenExternalLink(e.Uri);
        }

        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.F11)
            {
                if (_isContentFullscreen || _isShowingOriginalContent || _isReaderModeActive)
                {
                    ToggleContentFullscreen();
                    e.Handled = true;
                }

                return;
            }

            if (e.Key == System.Windows.Input.Key.Escape && _isContentFullscreen)
            {
                SetContentFullscreen(false);
                e.Handled = true;
            }
        }

        private void OpenExternalLink(string link)
        {
            if (string.IsNullOrWhiteSpace(link))
            {
                return;
            }

            if (_openExternalLinkWithPrompt != null)
            {
                _openExternalLinkWithPrompt(link);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
            }
            catch
            {
                // Ignore launch failures and keep the window responsive.
            }
        }

        private void NavigateSummaryContent()
        {
            _isShowingOriginalContent = false;
            _isOriginalContentLoading = false;
            _isReaderModeActive = false;
            ResetVideoPlaybackState();
            ApplyWebViewModeSettings(enableScripts: false);
            ArticleContentWebView.NavigateToString(_summaryArticleHtml);
            UpdateActionButtons();
        }

        private async Task ShowOriginalContentAsync()
        {
            if (string.IsNullOrWhiteSpace(_articleLink))
            {
                return;
            }

            try
            {
                if (_browserSession == null)
                {
                    OpenExternalLink(_articleLink);
                    return;
                }

                if (!await _browserSession.EnsureInitializedAsync())
                {
                    OpenExternalLink(_articleLink);
                    return;
                }

                _isShowingOriginalContent = true;
                _isOriginalContentLoading = true;
                _isReaderModeActive = false;
                _browserSession.SetMediaPlaybackEnabled(_isVideoPlaybackEnabled);
                ApplyWebViewModeSettings(enableScripts: true);
                UpdateActionButtons();
                await _browserSession.NavigateFastAsync(_articleLink);
            }
            catch
            {
                _isShowingOriginalContent = false;
                _isOriginalContentLoading = false;
                ApplyWebViewModeSettings(enableScripts: false);
                UpdateActionButtons();
                OpenExternalLink(_articleLink);
            }
        }

        private void ApplyWebViewModeSettings(bool enableScripts)
        {
            if (ArticleContentWebView?.CoreWebView2 == null)
            {
                return;
            }

            ArticleContentWebView.CoreWebView2.Settings.IsScriptEnabled = enableScripts;
            ArticleContentWebView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
        }

        private void UpdateActionButtons()
        {
            var hasLink = !string.IsNullOrWhiteSpace(_articleLink);
            var hasContentPreview = _isShowingOriginalContent || _isReaderModeActive;
            var canEnableVideo = hasLink && _isShowingOriginalContent && !_isReaderModeActive && !_isOriginalContentLoading && !_isReaderModeLoading;
            ShowContentButton.IsEnabled = hasLink && !_isReaderModeLoading && !_isOriginalContentLoading;
            ReaderModeButton.IsEnabled = hasLink && !_isReaderModeLoading && !_isOriginalContentLoading;
            RefreshPageButton.IsEnabled = hasLink && hasContentPreview && !_isReaderModeLoading && !_isOriginalContentLoading;
            FullscreenRefreshButton.IsEnabled = hasLink && hasContentPreview && !_isReaderModeLoading && !_isOriginalContentLoading;
            FullscreenLinkTextBlock.Text = hasLink ? _articleLink : string.Empty;
            FullscreenToolbar.Visibility = _isContentFullscreen && hasContentPreview ? Visibility.Visible : Visibility.Collapsed;
            EnableVideoButton.Visibility = canEnableVideo ? Visibility.Visible : Visibility.Collapsed;
            EnableVideoButton.IsEnabled = canEnableVideo && !_isVideoPlaybackEnabled;
            EnableVideoButton.Content = _isVideoPlaybackEnabled ? "Media Allowed" : "Allow Media";
            FullscreenEnableVideoButton.Visibility = canEnableVideo ? Visibility.Visible : Visibility.Collapsed;
            FullscreenEnableVideoButton.IsEnabled = canEnableVideo && !_isVideoPlaybackEnabled;
            FullscreenEnableVideoButton.Content = _isVideoPlaybackEnabled ? "Media Allowed" : "Allow Media";
            FullscreenEnableVideoSeparator.Visibility = canEnableVideo ? Visibility.Visible : Visibility.Collapsed;
            UpdateShowContentButtonText();
            UpdateReaderModeButtonText();
        }

        private void ResetVideoPlaybackState()
        {
            if (!_isVideoPlaybackEnabled && (_browserSession == null || !_browserSession.IsMediaPlaybackEnabled))
            {
                return;
            }

            _isVideoPlaybackEnabled = false;
            _browserSession?.SetMediaPlaybackEnabled(false);
        }

        private void FullscreenButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleContentFullscreen();
        }

        private void ToggleContentFullscreen()
        {
            SetContentFullscreen(!_isContentFullscreen);
        }

        private void SetContentFullscreen(bool isFullscreen)
        {
            if (_isContentFullscreen == isFullscreen)
            {
                return;
            }

            if (isFullscreen && !(_isShowingOriginalContent || _isReaderModeActive))
            {
                return;
            }

            _isContentFullscreen = isFullscreen;
            HeaderBorder.Visibility = isFullscreen ? Visibility.Collapsed : Visibility.Visible;
            HeaderRow.Height = isFullscreen ? new GridLength(0) : GridLength.Auto;

            if (isFullscreen)
            {
                _windowStateBeforeFullscreen = WindowState;
                _windowStyleBeforeFullscreen = WindowStyle;
                _resizeModeBeforeFullscreen = ResizeMode;
                _boundsBeforeFullscreen = new Rect(Left, Top, Width, Height);

                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;
            }
            else
            {
                WindowState = WindowState.Normal;
                WindowStyle = _windowStyleBeforeFullscreen;
                ResizeMode = _resizeModeBeforeFullscreen;
                Left = _boundsBeforeFullscreen.Left;
                Top = _boundsBeforeFullscreen.Top;
                Width = _boundsBeforeFullscreen.Width;
                Height = _boundsBeforeFullscreen.Height;
                WindowState = _windowStateBeforeFullscreen;
            }

            UpdateActionButtons();
        }

        private void UpdateShowContentButtonText()
        {
            if (_isOriginalContentLoading)
            {
                ShowContentButton.Content = "Loading Page...";
                return;
            }

            ShowContentButton.Content = _isShowingOriginalContent && !_isReaderModeActive
                ? "Article Summary"
                : "Show Content";
        }

        private void UpdateReaderModeButtonText()
        {
            if (_isReaderModeLoading)
            {
                ReaderModeButton.Content = "Loading Reader...";
                return;
            }

            ReaderModeButton.Content = _isReaderModeActive ? "Feed Summary" : "Reader Mode";
        }

        private string BuildReaderModeHtml(ReaderArticleContent content)
        {
            var backgroundColor = _darkMode ? "#121212" : "#ffffff";
            var textColor = _darkMode ? "#e0e0e0" : "#333333";
            var headerColor = _darkMode ? "#90caf9" : "#673ab7";
            var metaColor = _darkMode ? "#b0b0b0" : "#666666";
            var borderColor = _darkMode ? "#90caf9" : "#673ab7";
            var linkColor = _darkMode ? "#81c784" : "#673ab7";
            var title = string.IsNullOrWhiteSpace(content.Title) ? ArticleTitleText.Text : content.Title;
            var encodedTitle = System.Web.HttpUtility.HtmlEncode(title);
            var encodedLink = System.Web.HttpUtility.HtmlEncode(_articleLink);
            var encodedLinkAttribute = System.Web.HttpUtility.HtmlAttributeEncode(_articleLink);

            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1'>
    <meta name='color-scheme' content='{(_darkMode ? "dark light" : "light dark")}'>
    <meta http-equiv='Content-Security-Policy' content=""default-src 'none'; img-src https: http: data:; style-src 'unsafe-inline'; font-src data:; base-uri 'none'; form-action 'none'; child-src 'none'; frame-ancestors 'none'"">
    <style>
        :root {{
            color-scheme: {(_darkMode ? "dark" : "light")};
        }}
        body {{
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            margin: 15px;
            padding: 0;
            line-height: 1.5;
            color: {textColor};
            background-color: {backgroundColor};
            overflow-x: hidden;
        }}
        .article-header {{
            border-bottom: 2px solid {borderColor};
            padding-bottom: 10px;
            margin-bottom: 15px;
        }}
        .article-title {{
            font-size: 22px;
            font-weight: bold;
            color: {headerColor};
            margin-bottom: 8px;
            word-wrap: break-word;
        }}
        .article-meta {{
            font-size: 12px;
            color: {metaColor};
            margin-bottom: 10px;
            word-break: break-all;
        }}
        .article-meta a {{
            color: {linkColor};
            text-decoration: none;
        }}
        .article-meta a:hover {{
            text-decoration: underline;
        }}
        .article-content {{
            font-size: 15px;
            line-height: 1.55;
            word-wrap: break-word;
            color: {textColor};
        }}
        .article-content p {{
            margin: 0 0 14px 0;
        }}
        .article-content img {{
            max-width: min(100%, 520px) !important;
            width: auto !important;
            height: auto !important;
            max-height: 320px !important;
            margin: 12px auto;
            display: block;
            border-radius: 6px;
            object-fit: contain;
            float: none !important;
            clear: both !important;
        }}
        .article-content p,
        .article-content div,
        .article-content figure {{
            clear: both;
        }}
    </style>
</head>
<body>
    <div class='article-header'>
        <div class='article-title'>{encodedTitle}</div>
        <div class='article-meta'>
            <a href='{encodedLinkAttribute}'>{encodedLink}</a>
        </div>
    </div>
    <div class='article-content'>
        {content.HtmlContent}
    </div>
</body>
</html>";
        }
    }
}