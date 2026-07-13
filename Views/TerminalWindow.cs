using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Interop;
using MyNewsFeeder.Models;
using MyNewsFeeder.ViewModels;

namespace MyNewsFeeder.Views
{
    public partial class TerminalWindow : Window
    {
        private readonly TerminalViewModel _viewModel;
        private int _historyIndex = -1;
        private bool _shouldAutoScroll = true;
        private double _terminalFontSize = 14;
        private double _browseFeedsFontSize = 14;
        private double _browseArticlesFontSize = 14;
        private double _browseReaderFontSize = 14;
        private double _browseCrtFontSize = 14;
        private const int DwmwaBorderColor = 34;
        private const int DwmwaCaptionColor = 35;
        private const int DwmwaTextColor = 36;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

        public TerminalWindow(MainViewModel mainViewModel)
        {
            InitializeComponent();
            _viewModel = new TerminalViewModel(mainViewModel);
            DataContext = _viewModel;
            _viewModel.CloseRequested += ViewModel_CloseRequested;
            _viewModel.OutputChanged += ViewModel_OutputChanged;
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            _terminalFontSize = LoadTerminalFontSize();
            _browseFeedsFontSize = LoadFontSize("TerminalBrowseFeedsFontSize.txt", _terminalFontSize);
            _browseArticlesFontSize = LoadFontSize("TerminalBrowseArticlesFontSize.txt", _terminalFontSize);
            _browseReaderFontSize = LoadFontSize("TerminalBrowseReaderFontSize.txt", _terminalFontSize);
            _browseCrtFontSize = LoadFontSize("TerminalBrowseCrtFontSize.txt", _terminalFontSize);
            ApplyThemeResources();
            ApplyTerminalFontSizes();
        }

        private void TerminalWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyWindowChromeTheme();
            InputTextBox.Focus();
        }

        public void ShowNotificationActivation(ImportantNotificationItem item)
        {
            _viewModel.ShowNotificationActivation(item);
            BringTerminalToFront();
        }

        public void ShowLatestNotificationsActivation(IEnumerable<ImportantNotificationItem> items)
        {
            _viewModel.ShowLatestNotificationsActivation(items);
            BringTerminalToFront();
        }

        public void BringTerminalToFront()
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Show();
            Activate();
            Topmost = true;
            Topmost = false;
        }

        private void TerminalWindow_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control)
            {
                return;
            }

            AdjustZoomForMouseWheelSource(e.OriginalSource as DependencyObject, e.Delta > 0 ? 1 : -1);
            e.Handled = true;
        }

        private void AdjustZoomForMouseWheelSource(DependencyObject source, int direction)
        {
            if (_viewModel.IsBrowseMode)
            {
                if (IsCrtTheme)
                {
                    AdjustBrowseCrtZoom(direction);
                    return;
                }

                if (IsDescendantOf(source, BrowseReaderTextBox) || IsDescendantOf(source, BrowseReaderScrollViewer))
                {
                    AdjustBrowseReaderZoom(direction);
                    return;
                }

                if (IsDescendantOf(source, BrowseArticlesList))
                {
                    AdjustBrowseArticlesZoom(direction);
                    return;
                }

                if (IsDescendantOf(source, BrowseFeedsList))
                {
                    AdjustBrowseFeedsZoom(direction);
                    return;
                }
            }

            AdjustCommandZoom(direction);
        }

        private void AdjustCommandZoom(int direction)
        {
            var next = ClampFontSize(_terminalFontSize + direction);
            if (Math.Abs(next - _terminalFontSize) < 0.01)
            {
                return;
            }

            _terminalFontSize = next;
            ApplyCommandFontSize();
            SaveFontSize("TerminalFontSize.txt", _terminalFontSize);
        }

        private void AdjustBrowseCrtZoom(int direction)
        {
            var next = ClampFontSize(_browseCrtFontSize + direction);
            if (Math.Abs(next - _browseCrtFontSize) < 0.01)
            {
                return;
            }

            _browseCrtFontSize = next;
            ApplyBrowseCrtFontSize();
            SaveFontSize("TerminalBrowseCrtFontSize.txt", _browseCrtFontSize);
        }

        private void AdjustBrowseFeedsZoom(int direction)
        {
            var next = ClampFontSize(_browseFeedsFontSize + direction);
            if (Math.Abs(next - _browseFeedsFontSize) < 0.01)
            {
                return;
            }

            _browseFeedsFontSize = next;
            ApplyBrowseFeedsFontSize();
            SaveFontSize("TerminalBrowseFeedsFontSize.txt", _browseFeedsFontSize);
        }

        private void AdjustBrowseArticlesZoom(int direction)
        {
            var next = ClampFontSize(_browseArticlesFontSize + direction);
            if (Math.Abs(next - _browseArticlesFontSize) < 0.01)
            {
                return;
            }

            _browseArticlesFontSize = next;
            ApplyBrowseArticlesFontSize();
            SaveFontSize("TerminalBrowseArticlesFontSize.txt", _browseArticlesFontSize);
        }

        private void AdjustBrowseReaderZoom(int direction)
        {
            var next = ClampFontSize(_browseReaderFontSize + direction);
            if (Math.Abs(next - _browseReaderFontSize) < 0.01)
            {
                return;
            }

            _browseReaderFontSize = next;
            ApplyBrowseReaderFontSize();
            SaveFontSize("TerminalBrowseReaderFontSize.txt", _browseReaderFontSize);
        }

        private void ApplyTerminalFontSizes()
        {
            ApplyCommandFontSize();
            ApplyBrowseThemeFontSizes();
        }

        private void ApplyCommandFontSize()
        {
            if (OutputTextBox != null)
            {
                OutputTextBox.FontSize = _terminalFontSize;
            }

            if (InputTextBox != null)
            {
                InputTextBox.FontSize = _terminalFontSize;
            }

            if (PromptTextBlock != null)
            {
                PromptTextBlock.FontSize = _terminalFontSize;
            }
        }

        private void ApplyBrowseFeedsFontSize()
        {
            if (BrowseFeedsList != null)
            {
                BrowseFeedsList.FontSize = _browseFeedsFontSize;
            }

            if (BrowseFeedsHeaderTextBlock != null)
            {
                BrowseFeedsHeaderTextBlock.FontSize = _browseFeedsFontSize;
            }
        }

        private void ApplyBrowseArticlesFontSize()
        {
            if (BrowseArticlesList != null)
            {
                BrowseArticlesList.FontSize = _browseArticlesFontSize;
            }

            if (BrowseArticlesHeaderTextBlock != null)
            {
                BrowseArticlesHeaderTextBlock.FontSize = _browseArticlesFontSize;
            }
        }

        private void ApplyBrowseReaderFontSize()
        {
            if (BrowseReaderTextBox != null)
            {
                BrowseReaderTextBox.FontSize = _browseReaderFontSize;
            }

            if (BrowseReaderHeaderTextBlock != null)
            {
                BrowseReaderHeaderTextBlock.FontSize = _browseReaderFontSize;
            }
        }

        private void ApplyBrowseThemeFontSizes()
        {
            if (IsCrtTheme)
            {
                ApplyBrowseCrtFontSize();
                return;
            }

            ApplyBrowseFeedsFontSize();
            ApplyBrowseArticlesFontSize();
            ApplyBrowseReaderFontSize();
        }

        private void ApplyBrowseCrtFontSize()
        {
            ApplyBrowseFontSize(_browseCrtFontSize);
        }

        private void ApplyBrowseFontSize(double fontSize)
        {
            if (BrowseFeedsList != null)
            {
                BrowseFeedsList.FontSize = fontSize;
            }

            if (BrowseFeedsHeaderTextBlock != null)
            {
                BrowseFeedsHeaderTextBlock.FontSize = fontSize;
            }

            if (BrowseArticlesList != null)
            {
                BrowseArticlesList.FontSize = fontSize;
            }

            if (BrowseArticlesHeaderTextBlock != null)
            {
                BrowseArticlesHeaderTextBlock.FontSize = fontSize;
            }

            if (BrowseReaderTextBox != null)
            {
                BrowseReaderTextBox.FontSize = fontSize;
            }

            if (BrowseReaderHeaderTextBlock != null)
            {
                BrowseReaderHeaderTextBlock.FontSize = fontSize;
            }
        }

        private bool IsCrtTheme => _viewModel?.IsCrtTheme == true;

        private static double ClampFontSize(double value)
        {
            return Math.Max(10, Math.Min(28, value));
        }

        private static double LoadTerminalFontSize()
        {
            return LoadFontSize("TerminalFontSize.txt", 14);
        }

        private static double LoadFontSize(string fileName, double fallback)
        {
            try
            {
                var path = GetTerminalFontSizePath(fileName);
                if (System.IO.File.Exists(path) &&
                    double.TryParse(System.IO.File.ReadAllText(path), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
                {
                    return ClampFontSize(value);
                }
            }
            catch
            {
                // Use default terminal size.
            }

            return ClampFontSize(fallback);
        }

        private static void SaveFontSize(string fileName, double value)
        {
            try
            {
                var path = GetTerminalFontSizePath(fileName);
                var directory = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                }

                System.IO.File.WriteAllText(path, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            catch
            {
                // Ignore font-size persistence failures.
            }
        }

        private static string GetTerminalFontSizePath(string fileName)
        {
            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MyNewsFeeder",
                fileName);
        }

        private static bool IsDescendantOf(DependencyObject source, DependencyObject target)
        {
            var current = source;
            while (current != null)
            {
                if (ReferenceEquals(current, target))
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private void OutputTextBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                CopySelectedTextAfterSelectionSettles(textBox, isBrowseReader: false);
            }
        }

        private void CopySelectedTextAfterSelectionSettles(TextBox textBox, bool isBrowseReader)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var textToCopy = textBox?.SelectedText;
                if (string.IsNullOrWhiteSpace(textToCopy))
                {
                    return;
                }

                try
                {
                    Clipboard.SetText(textToCopy);
                }
                catch (Exception ex)
                {
                    if (isBrowseReader)
                    {
                        _viewModel.BrowseStatus = "Copy failed: " + ex.Message;
                    }
                    else
                    {
                        _viewModel.Lines.Add(new TerminalLine($"Copy failed: {ex.Message}", TerminalLineType.Error));
                        _viewModel.TriggerOutputChanged();
                    }
                }
            }), DispatcherPriority.ContextIdle);
        }

        private void InputTextBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            ClearOutputSelection();
        }

        private void InputTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            ClearOutputSelection();
        }

        private void SelectableOutputTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (_viewModel.IsBrowseMode || string.IsNullOrEmpty(e.Text))
            {
                return;
            }

            FocusCommandInputAtEnd();
            InputTextBox.SelectedText = e.Text;
            InputTextBox.CaretIndex = InputTextBox.Text?.Length ?? 0;
            _viewModel.CurrentInput = InputTextBox.Text;
            e.Handled = true;
        }

        private void SelectableOutputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_viewModel.IsBrowseMode || InputTextBox == null)
            {
                return;
            }

            if (e.Key == Key.Tab)
            {
                FocusCommandInputAtEnd();
                DoTabCompletion();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                FocusCommandInputAtEnd();
                _historyIndex = -1;
                if (_viewModel.ExecuteCommand?.CanExecute(null) == true)
                {
                    _viewModel.ExecuteCommand.Execute(null);
                }

                e.Handled = true;
                return;
            }

            if (e.Key == Key.Back)
            {
                FocusCommandInputAtEnd();
                if (InputTextBox.Text?.Length > 0)
                {
                    InputTextBox.Text = InputTextBox.Text.Substring(0, InputTextBox.Text.Length - 1);
                    InputTextBox.CaretIndex = InputTextBox.Text.Length;
                    _viewModel.CurrentInput = InputTextBox.Text;
                }

                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up)
            {
                FocusCommandInputAtEnd();
                ShowHistory(delta: -1);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Down)
            {
                FocusCommandInputAtEnd();
                ShowHistory(delta: 1);
                e.Handled = true;
                return;
            }
        }

        private void FocusCommandInputAtEnd()
        {
            ClearOutputSelection();
            InputTextBox.Focus();
            InputTextBox.CaretIndex = InputTextBox.Text?.Length ?? 0;
        }

        private void ClearOutputSelection()
        {
            if (OutputTextBox != null && OutputTextBox.SelectionLength > 0)
            {
                OutputTextBox.Select(OutputTextBox.CaretIndex, 0);
            }
        }

        private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null && !(child is T))
            {
                child = VisualTreeHelper.GetParent(child);
            }
            return child as T;
        }

        private string _searchQuery = string.Empty;
        private int _searchIndex = -1;

        private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            // Strg+R: History Suche (reverse search)
            if (e.Key == Key.R && Keyboard.Modifiers == ModifierKeys.Control)
            {
                DoReverseSearch();
                e.Handled = true;
                return;
            }

            // Strg+C: Kopieren in Zwischenspeicher
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                DoCopyToClipboard();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Tab)
            {
                DoTabCompletion();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                _historyIndex = -1;
                if (_viewModel.ExecuteCommand?.CanExecute(null) == true)
                {
                    _viewModel.ExecuteCommand.Execute(null);
                }

                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up)
            {
                ShowHistory(delta: -1);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Down)
            {
                ShowHistory(delta: 1);
                e.Handled = true;
            }
        }

        private void DoTabCompletion()
        {
            var currentText = InputTextBox.Text;
            if (string.IsNullOrWhiteSpace(currentText))
                return;

            var completions = _viewModel.GetCompletions(currentText);
            if (completions.Count == 0)
                return;

            if (completions.Count == 1)
            {
                InputTextBox.Text = completions[0];
                InputTextBox.CaretIndex = InputTextBox.Text.Length;
                _viewModel.CurrentInput = InputTextBox.Text;
                return;
            }

            var commonPrefix = FindCommonPrefix(completions);
            if (commonPrefix.Length > currentText.Length)
            {
                InputTextBox.Text = commonPrefix;
                InputTextBox.CaretIndex = InputTextBox.Text.Length;
                _viewModel.CurrentInput = InputTextBox.Text;
            }
            else
            {
                // Füge die Completions als Hinweiszeile ein
                var suggestions = string.Join(", ", completions.Take(10));
                if (completions.Count > 10)
                    suggestions += ", ...";
                _viewModel.Lines.Add(new TerminalLine(
                    $"Completions: {suggestions}",
                    TerminalLineType.Dim));
                _viewModel.TriggerOutputChanged();
            }
        }

        private string FindCommonPrefix(List<string> strings)
        {
            if (strings.Count == 0) return string.Empty;
            if (strings.Count == 1) return strings[0];

            var first = strings[0];
            for (int i = 0; i < first.Length; i++)
            {
                var c = first[i];
                foreach (var s in strings.Skip(1))
                {
                    if (i >= s.Length || s[i] != c)
                        return first.Substring(0, i);
                }
            }
            return first;
        }

        private void DoReverseSearch()
        {
            // Strg+R: Suche rückwärts durch History
            var history = _viewModel.History;
            if (history.Count == 0)
                return;

            // Start neue Suche oder fortsetzen
            if (string.IsNullOrWhiteSpace(_searchQuery))
            {
                // Starte neue Suche mit aktuellem Input
                _searchQuery = InputTextBox.Text;
                _searchIndex = history.Count - 1;
            }

            // Suche rückwärts
            for (int i = _searchIndex; i >= 0; i--)
            {
                if (history[i].Contains(_searchQuery, StringComparison.OrdinalIgnoreCase))
                {
                    InputTextBox.Text = history[i];
                    InputTextBox.CaretIndex = InputTextBox.Text.Length;
                    _viewModel.CurrentInput = InputTextBox.Text;
                    _searchIndex = i - 1; // Nächste Suche startet vor diesem Index
                    return;
                }
            }

            // Wenn nichts gefunden, von vorne beginnen
            for (int i = history.Count - 1; i >= 0; i--)
            {
                if (history[i].Contains(_searchQuery, StringComparison.OrdinalIgnoreCase))
                {
                    InputTextBox.Text = history[i];
                    InputTextBox.CaretIndex = InputTextBox.Text.Length;
                    _viewModel.CurrentInput = InputTextBox.Text;
                    _searchIndex = i - 1;
                    return;
                }
            }

            // Nichts gefunden - zur Normalansicht zurück
            _searchQuery = string.Empty;
            _searchIndex = -1;
        }

        private void DoCopyToClipboard()
        {
            string textToCopy = string.Empty;

            if (_viewModel.IsBrowseMode)
            {
                // Im Browse-Modus: Artikel-Text kopieren
                textToCopy = _viewModel.ReaderText;
            }
            else
            {
                // Im Command-Modus: Ausgewählten Text aus der Output kopieren
                // oder aktuellen Input
                textToCopy = InputTextBox.SelectedText;
                if (string.IsNullOrWhiteSpace(textToCopy))
                {
                    textToCopy = InputTextBox.Text;
                }
            }

            if (!string.IsNullOrWhiteSpace(textToCopy))
            {
                try
                {
                    Clipboard.SetText(textToCopy);
                }
                catch (Exception ex)
                {
                    _viewModel.Lines.Add(new TerminalLine($"Copy failed: {ex.Message}", TerminalLineType.Error));
                    _viewModel.TriggerOutputChanged();
                }
            }
        }

        private async void BrowseGrid_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_viewModel.IsBrowseMode)
            {
                return;
            }

            var source = e.OriginalSource as DependencyObject;
            if (source == null)
            {
                return;
            }

            if (IsChildOf(source, BrowseReaderTextBox) || IsChildOf(source, BrowseReaderScrollViewer))
            {
                return;
            }

            var listBoxItem = FindVisualParent<ListBoxItem>(source);
            if (listBoxItem == null || !IsChildOf(listBoxItem, BrowseArticlesList))
            {
                return;
            }

            if (listBoxItem.DataContext is not TerminalArticleEntry item)
            {
                return;
            }

            if (e.ChangedButton == MouseButton.Right)
            {
                _viewModel.SelectedBrowseArticle = item;
                var markAsRead = item.Item?.IsUnread == true;
                _viewModel.ToggleReadState(item.Item);
                ShowTemporaryBrowseStatus(markAsRead ? "Marked as read" : "Marked as unread");
                e.Handled = true;
                return;
            }

            if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
            {
                _viewModel.SelectedBrowseArticle = item;
                await _viewModel.LoadSelectedBrowseReaderAsync();
                e.Handled = true;
            }
        }

        private void BrowseReaderTextBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_viewModel.IsBrowseMode)
            {
                return;
            }

            if (sender is TextBox textBox)
            {
                CopySelectedTextAfterSelectionSettles(textBox, isBrowseReader: true);
            }
        }

        private async Task ResetBrowseStatusAfterDelayAsync()
        {
            await Task.Delay(2000);
            if (_viewModel != null)
            {
                _viewModel.BrowseStatus = "Up/Down: article  PageUp/PageDown: jump  Home/End: article first/last  Left/Right: feed  Ctrl+Home/Ctrl+End: feed first/last  Enter: reader  J/K: scroll  G/Shift+G: top/bottom  Space/right-click: read/unread  M: read  U: unread  Ctrl+L: copy link  R: refresh  ?: help  Esc/q: command";
            }
        }

        private void ShowTemporaryBrowseStatus(string status)
        {
            if (_viewModel == null)
            {
                return;
            }

            _viewModel.BrowseStatus = status;
            _ = ResetBrowseStatusAfterDelayAsync();
        }

        private static bool IsChildOf(DependencyObject child, DependencyObject parent)
        {
            while (child != null && child != parent)
            {
                child = VisualTreeHelper.GetParent(child);
            }
            return child == parent;
        }

        private async void BrowseGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_viewModel.IsBrowseMode)
            {
                return;
            }

            if (e.Key == Key.Q)
            {
                _viewModel.ExitBrowseMode();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                _viewModel.ExitBrowseMode();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Down)
            {
                _viewModel.MoveBrowseArticle(1);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up)
            {
                _viewModel.MoveBrowseArticle(-1);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.PageDown)
            {
                _viewModel.MoveBrowseArticle(10);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.PageUp)
            {
                _viewModel.MoveBrowseArticle(-10);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Home && Keyboard.Modifiers == ModifierKeys.None)
            {
                _viewModel.MoveBrowseArticle(-int.MaxValue);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.End && Keyboard.Modifiers == ModifierKeys.None)
            {
                _viewModel.MoveBrowseArticle(int.MaxValue);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.End && Keyboard.Modifiers == ModifierKeys.Control)
            {
                _viewModel.MoveBrowseFeed(int.MaxValue);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Home && Keyboard.Modifiers == ModifierKeys.Control)
            {
                _viewModel.MoveBrowseFeed(-int.MaxValue);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Right && Keyboard.Modifiers == ModifierKeys.None)
            {
                _viewModel.MoveBrowseFeed(1);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Left && Keyboard.Modifiers == ModifierKeys.None)
            {
                _viewModel.MoveBrowseFeed(-1);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                await _viewModel.LoadSelectedBrowseReaderAsync();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.O)
            {
                _viewModel.OpenArticleInBrowser(_viewModel.SelectedBrowseArticle?.Item);
                ShowTemporaryBrowseStatus("Opening in browser...");
                e.Handled = true;
                return;
            }

            if (e.Key == Key.R)
            {
                _ = _viewModel.RefreshFeedAsync(_viewModel.SelectedBrowseFeed?.Feed);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.J && Keyboard.Modifiers == ModifierKeys.None)
            {
                ScrollBrowseReaderBySmallStep(1);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.None)
            {
                ScrollBrowseReaderBySmallStep(-1);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.G && Keyboard.Modifiers == ModifierKeys.None)
            {
                BrowseReaderScrollViewer.ScrollToTop();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.G && Keyboard.Modifiers == ModifierKeys.Shift)
            {
                BrowseReaderScrollViewer.ScrollToBottom();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Space)
            {
                _viewModel.ToggleReadState(_viewModel.SelectedBrowseArticle?.Item);
                ShowTemporaryBrowseStatus("Toggled read state");
                e.Handled = true;
                return;
            }

            if (e.Key == Key.M)
            {
                _viewModel.MarkRead(_viewModel.SelectedBrowseArticle?.Item);
                ShowTemporaryBrowseStatus("Marked as read");
                e.Handled = true;
                return;
            }

            if (e.Key == Key.U)
            {
                _viewModel.MarkUnread(_viewModel.SelectedBrowseArticle?.Item);
                ShowTemporaryBrowseStatus("Marked as unread");
                e.Handled = true;
                return;
            }

            if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control)
            {
                var link = _viewModel.SelectedBrowseArticle?.Item?.Link;
                if (!string.IsNullOrWhiteSpace(link))
                {
                    Clipboard.SetText(link);
                    ShowTemporaryBrowseStatus("Article link copied");
                }
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F1 || e.Key == Key.OemQuestion)
            {
                _viewModel.BrowseStatus = "Browse: Up/Down article  PageUp/PageDown jump  Left/Right feed  Enter reader  J/K scroll  G/Shift+G top/bottom  Space read/unread  M read  U unread  Ctrl+L link  R refresh  Esc/q command";
                e.Handled = true;
                return;
            }
        }

        private void ScrollBrowseReaderBySmallStep(int direction)
        {
            if (BrowseReaderScrollViewer == null)
            {
                return;
            }

            var lineHeight = Math.Max(18, BrowseReaderTextBox?.FontSize * 1.6 ?? 24);
            BrowseReaderScrollViewer.ScrollToVerticalOffset(BrowseReaderScrollViewer.VerticalOffset + (direction * lineHeight * 3));
        }

        private void ShowHistory(int delta)
        {
            if (_viewModel.History.Count == 0)
            {
                return;
            }

            if (_historyIndex < 0)
            {
                _historyIndex = _viewModel.History.Count;
            }

            _historyIndex = Math.Max(0, Math.Min(_viewModel.History.Count, _historyIndex + delta));
            _viewModel.CurrentInput = _historyIndex >= _viewModel.History.Count
                ? string.Empty
                : _viewModel.History[_historyIndex];
            InputTextBox.CaretIndex = InputTextBox.Text?.Length ?? 0;
        }

        private void ViewModel_OutputChanged()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_viewModel.IsBrowseMode)
                {
                    BrowseArticlesList.Focus();
                    BrowseReaderScrollViewer.ScrollToTop();
                    return;
                }

                if (_shouldAutoScroll)
                {
                    OutputTextBox.ScrollToEnd();
                }
                InputTextBox.Focus();
            }), DispatcherPriority.Background);
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TerminalViewModel.CurrentTheme))
            {
                ApplyThemeResources();
                ApplyBrowseThemeFontSizes();
            }

            if (e.PropertyName == nameof(TerminalViewModel.IsBrowseMode))
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_viewModel.IsBrowseMode)
                    {
                        BrowseArticlesList.Focus();
                    }
                    else
                    {
                        InputTextBox.Focus();
                    }
                }), DispatcherPriority.Background);
            }

            if (e.PropertyName == nameof(TerminalViewModel.ReaderText))
            {
                Dispatcher.BeginInvoke(new Action(() => BrowseReaderScrollViewer.ScrollToTop()), DispatcherPriority.Background);
            }
        }

        private void ApplyThemeResources()
        {
            var theme = _viewModel?.CurrentTheme ?? TerminalTheme.Default;
            SetBrushResource("TerminalBackgroundBrush", theme.Background);
            SetBrushResource("TerminalPanelBrush", theme.Panel);
            SetBrushResource("TerminalTextBrush", theme.Foreground);
            SetBrushResource("TerminalDimTextBrush", theme.Dim);
            SetBrushResource("TerminalBorderBrush", theme.Border);
            SetBrushResource("TerminalSelectionBrush", theme.Selection);
            SetBrushResource("TerminalTextSelectionBrush", theme.Selection);
            SetBrushResource("TerminalTextSelectionTextBrush", theme.Foreground);
            SetBrushResource(SystemColors.HighlightBrushKey, theme.Selection);
            SetBrushResource(SystemColors.HighlightTextBrushKey, theme.Foreground);
            SetBrushResource(SystemColors.InactiveSelectionHighlightBrushKey, theme.Selection);
            SetBrushResource(SystemColors.InactiveSelectionHighlightTextBrushKey, theme.Foreground);
            SetBrushResource("CommandBrush", theme.Command);
            SetBrushResource("ErrorBrush", theme.Error);
            SetBrushResource("SuccessBrush", theme.Success);
            SetBrushResource("WarningBrush", theme.Warning);
            SetBrushResource("HeaderBrush", theme.Header);
            Background = new SolidColorBrush(theme.Background);
            Foreground = new SolidColorBrush(theme.Foreground);
            ApplyWindowChromeTheme();
        }

        private void SetBrushResource(object key, Color color)
        {
            Resources[key] = new SolidColorBrush(color);
        }

        private void ApplyWindowChromeTheme()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero)
                {
                    return;
                }

                var theme = _viewModel?.CurrentTheme ?? TerminalTheme.Default;
                var captionColor = ToColorRef(theme.Background);
                var borderColor = ToColorRef(theme.Border);
                var textColor = ToColorRef(theme.Foreground);
                DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref captionColor, Marshal.SizeOf<int>());
                DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref borderColor, Marshal.SizeOf<int>());
                DwmSetWindowAttribute(hwnd, DwmwaTextColor, ref textColor, Marshal.SizeOf<int>());
            }
            catch
            {
                // Older Windows versions may not support themed title bars.
            }
        }

        private static int ToColorRef(Color color)
        {
            return color.R | (color.G << 8) | (color.B << 16);
        }

        private void ViewModel_CloseRequested()
        {
            Dispatcher.BeginInvoke(new Action(Close), DispatcherPriority.Background);
        }

        protected override void OnClosed(EventArgs e)
        {
            _viewModel.CloseRequested -= ViewModel_CloseRequested;
            _viewModel.OutputChanged -= ViewModel_OutputChanged;
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            base.OnClosed(e);
        }
    }
}
