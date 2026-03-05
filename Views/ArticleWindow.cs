using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
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
        private readonly CoreWebView2Environment _sharedEnvironment;
        private readonly ArticleReaderService _articleReaderService;
        private ReaderArticleContent _readerContent;
        private bool _isReaderModeActive;
        private bool _isReaderModeLoading;

        public ArticleWindow(
            string articleTitle,
            string articleHtml,
            string articleLink,
            bool darkMode,
            CoreWebView2Environment sharedEnvironment)
        {
            InitializeComponent();

            _summaryArticleHtml = string.IsNullOrWhiteSpace(articleHtml)
                ? "<html><body><p>No article content available.</p></body></html>"
                : articleHtml;
            _articleLink = articleLink?.Trim() ?? string.Empty;
            _darkMode = darkMode;
            _sharedEnvironment = sharedEnvironment;
            _articleReaderService = new ArticleReaderService();

            var titleText = string.IsNullOrWhiteSpace(articleTitle) ? "Article" : articleTitle.Trim();
            Title = titleText;
            ArticleTitleText.Text = titleText;
            ArticleLinkText.Text = string.IsNullOrWhiteSpace(_articleLink) ? "No external link" : _articleLink;
            OpenOriginalButton.IsEnabled = !string.IsNullOrWhiteSpace(_articleLink);
            ReaderModeButton.IsEnabled = !string.IsNullOrWhiteSpace(_articleLink);
            UpdateReaderModeButtonText();

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
                if (_sharedEnvironment != null)
                {
                    await ArticleContentWebView.EnsureCoreWebView2Async(_sharedEnvironment);
                }
                else
                {
                    await ArticleContentWebView.EnsureCoreWebView2Async();
                }

                if (ArticleContentWebView.CoreWebView2 != null)
                {
                    ArticleContentWebView.CoreWebView2.Settings.IsScriptEnabled = false;
                    ArticleContentWebView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
                    ArticleContentWebView.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
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
                ArticleContentWebView.CoreWebView2.NewWindowRequested -= CoreWebView2_NewWindowRequested;
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

        private async void ReaderModeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isReaderModeLoading)
            {
                return;
            }

            if (_isReaderModeActive)
            {
                _isReaderModeActive = false;
                NavigateSummaryContent();
                UpdateReaderModeButtonText();
                return;
            }

            if (string.IsNullOrWhiteSpace(_articleLink))
            {
                return;
            }

            _isReaderModeLoading = true;
            ReaderModeButton.IsEnabled = false;
            UpdateReaderModeButtonText();

            try
            {
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
                ReaderModeButton.IsEnabled = !string.IsNullOrWhiteSpace(_articleLink);
                UpdateReaderModeButtonText();
            }
        }

        private void OpenOriginalButton_Click(object sender, RoutedEventArgs e)
        {
            OpenExternalLink(_articleLink);
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

            e.Cancel = true;
            OpenExternalLink(e.Uri);
        }

        private void CoreWebView2_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            OpenExternalLink(e.Uri);
        }

        private static void OpenExternalLink(string link)
        {
            if (string.IsNullOrWhiteSpace(link))
            {
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
            ArticleContentWebView.NavigateToString(_summaryArticleHtml);
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