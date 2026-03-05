using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using MyNewsFeeder.Models;
using MyNewsFeeder.Services;

namespace MyNewsFeeder.Views
{
    public partial class HelpWindow : Window
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

        private readonly HelpContentService _helpContentService = new HelpContentService();
        private bool _topicsLoaded;
        private string _pendingTopicId;

        public HelpWindow()
        {
            InitializeComponent();
            SourceInitialized += (_, __) => EnableDarkTitleBar();
            Loaded += HelpWindow_Loaded;
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

        private void HelpWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_topicsLoaded)
            {
                ApplyPendingTopicSelection();
                ApplySelectedTopic();
                return;
            }

            _topicsLoaded = true;
            LoadTopics();
            ApplyPendingTopicSelection();
        }

        private void TopicsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplySelectedTopic();
        }

        private void LoadTopics()
        {
            if (TopicsListBox == null)
            {
                return;
            }

            var topics = _helpContentService.LoadTopics();
            TopicsListBox.ItemsSource = topics;

            if (topics.Count > 0)
            {
                TopicsListBox.SelectedIndex = 0;
            }
            else
            {
                TopicTitleText.Text = "Help";
                TopicContentViewer.Document = BuildMarkdownDocument("No help topics found.");
            }
        }

        public void SelectTopicById(string topicId)
        {
            if (string.IsNullOrWhiteSpace(topicId))
            {
                return;
            }

            _pendingTopicId = topicId.Trim();
            if (!_topicsLoaded)
            {
                return;
            }

            ApplyPendingTopicSelection();
        }

        private void ApplyPendingTopicSelection()
        {
            if (string.IsNullOrWhiteSpace(_pendingTopicId) || TopicsListBox == null)
            {
                return;
            }

            var desired = _pendingTopicId;
            var topic = TopicsListBox.Items.OfType<HelpTopic>()
                .FirstOrDefault(t => string.Equals(t.Id, desired, StringComparison.OrdinalIgnoreCase));
            if (topic == null)
            {
                return;
            }

            TopicsListBox.SelectedItem = topic;
            TopicsListBox.ScrollIntoView(topic);
            _pendingTopicId = null;
        }

        private void ApplySelectedTopic()
        {
            if (TopicsListBox == null || TopicTitleText == null || TopicContentViewer == null)
            {
                return;
            }

            if (TopicsListBox.SelectedItem is not HelpTopic topic)
            {
                return;
            }

            TopicTitleText.Text = topic.Title;
            var markdown = _helpContentService.LoadTopicMarkdown(topic);
            if (string.IsNullOrWhiteSpace(markdown))
            {
                markdown = "No content available for this topic yet.";
            }
            else
            {
                markdown = StripDuplicateTopHeading(markdown, topic.Title);
            }

            TopicContentViewer.Document = BuildMarkdownDocument(markdown);
        }

        private static string StripDuplicateTopHeading(string markdown, string topicTitle)
        {
            if (string.IsNullOrWhiteSpace(markdown) || string.IsNullOrWhiteSpace(topicTitle))
            {
                return markdown ?? string.Empty;
            }

            var normalizedTitle = topicTitle.Trim();
            var lines = markdown.Replace("\r\n", "\n").Split('\n').ToList();
            if (lines.Count == 0)
            {
                return markdown;
            }

            var firstContentIndex = -1;
            for (var i = 0; i < lines.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(lines[i]))
                {
                    firstContentIndex = i;
                    break;
                }
            }

            if (firstContentIndex < 0)
            {
                return markdown;
            }

            var firstLine = lines[firstContentIndex].Trim();
            if (firstLine.StartsWith("# ", StringComparison.Ordinal))
            {
                var heading = firstLine.Substring(2).Trim();
                if (string.Equals(heading, normalizedTitle, StringComparison.OrdinalIgnoreCase))
                {
                    lines.RemoveAt(firstContentIndex);
                    while (firstContentIndex < lines.Count && string.IsNullOrWhiteSpace(lines[firstContentIndex]))
                    {
                        lines.RemoveAt(firstContentIndex);
                    }

                    return string.Join("\n", lines);
                }
            }

            return markdown;
        }

        private static FlowDocument BuildMarkdownDocument(string markdown)
        {
            var doc = new FlowDocument
            {
                PagePadding = new Thickness(0),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 15
            };

            var lines = (markdown ?? string.Empty)
                .Replace("\r\n", "\n")
                .Split('\n');

            List currentList = null;

            foreach (var rawLine in lines)
            {
                var line = rawLine?.TrimEnd() ?? string.Empty;
                var trimmed = line.Trim();

                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    currentList = null;
                    continue;
                }

                if (trimmed.StartsWith("# ", StringComparison.Ordinal))
                {
                    currentList = null;
                    var heading = CreateParagraph(trimmed.Substring(2), fontSize: 26, FontWeights.Bold, marginBottom: 14);
                    doc.Blocks.Add(heading);
                    continue;
                }

                if (trimmed.StartsWith("## ", StringComparison.Ordinal))
                {
                    currentList = null;
                    var heading = CreateParagraph(trimmed.Substring(3), fontSize: 20, FontWeights.SemiBold, marginBottom: 10);
                    doc.Blocks.Add(heading);
                    continue;
                }

                if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
                {
                    if (currentList == null)
                    {
                        currentList = new List
                        {
                            MarkerStyle = TextMarkerStyle.Disc,
                            Margin = new Thickness(18, 0, 0, 10)
                        };
                        doc.Blocks.Add(currentList);
                    }

                    var itemText = trimmed.Substring(2).Trim();
                    var paragraph = CreateParagraph(itemText, fontSize: 15, FontWeights.Regular, marginBottom: 4);
                    currentList.ListItems.Add(new ListItem(paragraph));
                    continue;
                }

                currentList = null;
                doc.Blocks.Add(CreateParagraph(trimmed, fontSize: 15, FontWeights.Regular, marginBottom: 10));
            }

            if (!doc.Blocks.Any())
            {
                doc.Blocks.Add(CreateParagraph("No content available.", fontSize: 15, FontWeights.Regular, marginBottom: 0));
            }

            return doc;
        }

        private static Paragraph CreateParagraph(string text, double fontSize, FontWeight fontWeight, double marginBottom)
        {
            var paragraph = new Paragraph
            {
                Margin = new Thickness(0, 0, 0, marginBottom),
                FontSize = fontSize,
                FontWeight = fontWeight
            };

            AppendInlineMarkdown(paragraph.Inlines, text ?? string.Empty);
            return paragraph;
        }

        private static void AppendInlineMarkdown(InlineCollection inlines, string text)
        {
            if (inlines == null || string.IsNullOrEmpty(text))
            {
                return;
            }

            var cursor = 0;
            while (cursor < text.Length)
            {
                var boldStart = text.IndexOf("**", cursor, StringComparison.Ordinal);
                var linkStart = text.IndexOf('[', cursor);
                var nextToken = GetNextTokenStart(boldStart, linkStart);

                if (nextToken < 0)
                {
                    inlines.Add(new Run(text.Substring(cursor)));
                    return;
                }

                if (nextToken > cursor)
                {
                    inlines.Add(new Run(text.Substring(cursor, nextToken - cursor)));
                }

                if (nextToken == boldStart)
                {
                    var contentStart = boldStart + 2;
                    var boldEnd = text.IndexOf("**", contentStart, StringComparison.Ordinal);
                    if (boldEnd < 0)
                    {
                        inlines.Add(new Run(text.Substring(boldStart)));
                        return;
                    }

                    var boldContent = text.Substring(contentStart, boldEnd - contentStart);
                    var boldInline = new Bold();
                    AppendInlineMarkdown(boldInline.Inlines, boldContent);
                    inlines.Add(boldInline);
                    cursor = boldEnd + 2;
                    continue;
                }

                // Markdown link: [label](https://...)
                if (TryParseMarkdownLink(text, linkStart, out var label, out var url, out var endIndex) &&
                    TryCreateAllowedUri(url, out var navigateUri))
                {
                    var linkText = string.IsNullOrWhiteSpace(label) ? navigateUri.AbsoluteUri : label;
                    var hyperlink = new Hyperlink(new Run(linkText))
                    {
                        NavigateUri = navigateUri,
                        ToolTip = navigateUri.AbsoluteUri
                    };
                    hyperlink.Click += (_, __) => OpenExternalUri(navigateUri);
                    inlines.Add(hyperlink);
                    cursor = endIndex;
                    continue;
                }

                // Not a valid markdown link, keep literal '[' and continue.
                inlines.Add(new Run("["));
                cursor = linkStart + 1;
            }
        }

        private static int GetNextTokenStart(int boldStart, int linkStart)
        {
            if (boldStart < 0) return linkStart;
            if (linkStart < 0) return boldStart;
            return Math.Min(boldStart, linkStart);
        }

        private static bool TryParseMarkdownLink(string text, int linkStart, out string label, out string url, out int endIndex)
        {
            label = string.Empty;
            url = string.Empty;
            endIndex = -1;

            if (string.IsNullOrEmpty(text) || linkStart < 0 || linkStart >= text.Length || text[linkStart] != '[')
            {
                return false;
            }

            var labelEnd = text.IndexOf(']', linkStart + 1);
            if (labelEnd <= linkStart + 1)
            {
                return false;
            }

            if (labelEnd + 1 >= text.Length || text[labelEnd + 1] != '(')
            {
                return false;
            }

            var urlEnd = text.IndexOf(')', labelEnd + 2);
            if (urlEnd <= labelEnd + 2)
            {
                return false;
            }

            label = text.Substring(linkStart + 1, labelEnd - linkStart - 1).Trim();
            url = text.Substring(labelEnd + 2, urlEnd - labelEnd - 2).Trim();
            endIndex = urlEnd + 1;
            return !string.IsNullOrWhiteSpace(url);
        }

        private static bool TryCreateAllowedUri(string value, out Uri uri)
        {
            uri = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed))
            {
                return false;
            }

            if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            uri = parsed;
            return true;
        }

        private static void OpenExternalUri(Uri uri)
        {
            if (uri == null)
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = uri.AbsoluteUri,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Ignore failures when opening external links.
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}