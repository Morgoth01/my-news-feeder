using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MyNewsFeeder.Models;
using MyNewsFeeder.Services;

namespace MyNewsFeeder.ViewModels
{
    public class TerminalViewModel : INotifyPropertyChanged
    {
        private readonly MainViewModel _mainViewModel;
        private readonly TerminalCommandService _commandService;
        private readonly ArticleReaderService _articleReaderService = new ArticleReaderService();
        private readonly Dictionary<string, ReaderArticleContent> _readerContentCache = new Dictionary<string, ReaderArticleContent>(StringComparer.OrdinalIgnoreCase);
        private const string BrowseHelpStatus = "Up/Down: article  PageUp/PageDown: jump  Home/End: article first/last  Left/Right: feed  Ctrl+Home/Ctrl+End: feed first/last  Enter: reader  J/K: scroll  G/Shift+G: top/bottom  Space/right-click: read/unread  M: read  U: unread  Ctrl+L: copy link  R: refresh  ?: help  Esc/q: command";
        private const int MaxPersistentReaderCacheEntries = 200;
        private static readonly TimeSpan PersistentReaderCacheRetention = TimeSpan.FromDays(14);
        private const int BootLineDelayMs = 260;
        private const int BootFastDelayMs = 90;
        private const int BootLogoDelayMs = 32;
        private string _currentInput = string.Empty;
        private string _browseStatus = "Type 'browse' to start.";
        private string _browseTitle = "Terminal Browser";
        private string _readerText = string.Empty;
        private TerminalFeedEntry _selectedBrowseFeed;
        private TerminalArticleEntry _selectedBrowseArticle;
        private bool _isExecuting;
        private bool _isBrowseMode;
        private bool _bootAnimationEnabled = true;
        private TerminalTheme _currentTheme = TerminalTheme.Default;
        private static TerminalTheme _staticCurrentTheme = TerminalTheme.Default;

        public TerminalViewModel(MainViewModel mainViewModel)
        {
            _mainViewModel = mainViewModel ?? throw new ArgumentNullException(nameof(mainViewModel));
            _commandService = new TerminalCommandService(mainViewModel);
            ExecuteCommand = new RelayCommand(async _ => await ExecuteCurrentInputAsync(), _ => !IsExecuting);
            Lines.CollectionChanged += (_, __) => OnPropertyChanged(nameof(OutputText));
            
            LoadThemeSettings();
            LoadBootAnimationSettings();

            if (_bootAnimationEnabled)
            {
                _ = RunBootAnimationAsync();
            }
            else
            {
                AddWelcomeLines();
            }
        }

        public TerminalTheme CurrentTheme
        {
            get => _currentTheme;
            set
            {
                if (_currentTheme == value) return;
                _currentTheme = value ?? TerminalTheme.Default;
                _staticCurrentTheme = _currentTheme;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ThemeBackgroundBrush));
                OnPropertyChanged(nameof(ThemeForegroundBrush));
                OnPropertyChanged(nameof(ThemePanelBrush));
                OnPropertyChanged(nameof(ThemeBorderBrush));
                OnPropertyChanged(nameof(ThemeSelectionBrush));
                OnPropertyChanged(nameof(ThemeFontFamily));
                OnPropertyChanged(nameof(ThemeScanlineOpacity));
                OnPropertyChanged(nameof(ThemeVignetteOpacity));
                OnPropertyChanged(nameof(ThemeGlowOpacity));
                OnPropertyChanged(nameof(IsCrtTheme));
                SaveThemeSettings();
            }
        }

        // Statische Eigenschaft für Converter-Zugriff
        public static TerminalTheme StaticCurrentTheme => _staticCurrentTheme;

        // Brushes als Properties für XAML-Bindung
        public SolidColorBrush ThemeBackgroundBrush => new SolidColorBrush(_currentTheme.Background);
        public SolidColorBrush ThemeForegroundBrush => new SolidColorBrush(_currentTheme.Foreground);
        public SolidColorBrush ThemePanelBrush => new SolidColorBrush(_currentTheme.Panel);
        public SolidColorBrush ThemeBorderBrush => new SolidColorBrush(_currentTheme.Border);
        public SolidColorBrush ThemeSelectionBrush => new SolidColorBrush(_currentTheme.Selection);
        public string ThemeFontFamily => _currentTheme.FontFamily;
        public bool IsCrtTheme => string.Equals(_currentTheme.Name, "CRT", StringComparison.OrdinalIgnoreCase);
        public double ThemeScanlineOpacity => IsCrtTheme ? 0.28 : 0.0;
        public double ThemeVignetteOpacity => IsCrtTheme ? 0.62 : 0.0;
        public double ThemeGlowOpacity => IsCrtTheme ? 0.42 : 0.0;

        // Wechsel zum angegebenen Theme
        public bool SetTheme(string themeName)
        {
            themeName = NormalizeThemeName(themeName);
            var theme = TerminalTheme.AllThemes.FirstOrDefault(item =>
                string.Equals(item.Name, themeName, StringComparison.OrdinalIgnoreCase));
            if (theme == null)
            {
                return false;
            }

            CurrentTheme = theme;
            return true;
        }

        private static string NormalizeThemeName(string themeName)
        {
            var normalized = (themeName ?? string.Empty).Trim();
            if (normalized.StartsWith("theme ", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(6).Trim();
            }

            return normalized.ToLowerInvariant() switch
            {
                "solarized" => "SolarizedDark",
                "solarized-dark" => "SolarizedDark",
                "solarized_dark" => "SolarizedDark",
                "green" => "CRT",
                "classic" => "Default",
                _ => normalized
            };
        }

        // Liste aller verfügbaren Themes
        public List<string> AvailableThemes => TerminalTheme.AllThemes.Select(t => t.Name).ToList();

        // Lade Theme aus Einstellungen
        private void LoadThemeSettings()
        {
            try
            {
                var configuredTheme = _mainViewModel?.TerminalThemeName;
                if (!string.IsNullOrWhiteSpace(configuredTheme))
                {
                    var configured = TerminalTheme.GetByName(configuredTheme);
                    _currentTheme = configured ?? TerminalTheme.Default;
                    _staticCurrentTheme = _currentTheme;
                    return;
                }

                // Versuche, Theme aus lokaler Datei zu laden
                var themeFile = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MyNewsFeeder", "TerminalTheme.txt");
                
                if (System.IO.File.Exists(themeFile))
                {
                    var savedTheme = System.IO.File.ReadAllText(themeFile).Trim();
                    if (!string.IsNullOrWhiteSpace(savedTheme))
                    {
                        var theme = TerminalTheme.GetByName(savedTheme);
                        if (theme != null)
                        {
                            _currentTheme = theme;
                            _staticCurrentTheme = theme;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Ignorieren, standard Theme verwenden
            }
        }

        // Speichere Theme in Einstellungen
        private void SaveThemeSettings()
        {
            try
            {
                _mainViewModel?.SetTerminalThemeName(_currentTheme.Name);

                var themeFile = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MyNewsFeeder", "TerminalTheme.txt");
                
                var directory = System.IO.Path.GetDirectoryName(themeFile);
                if (!System.IO.Directory.Exists(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                }
                
                System.IO.File.WriteAllText(themeFile, _currentTheme.Name);
            }
            catch (Exception)
            {
                // Ignorieren
            }
        }

        private void LoadBootAnimationSettings()
        {
            try
            {
                if (_mainViewModel != null)
                {
                    _bootAnimationEnabled = _mainViewModel.TerminalBootAnimationEnabled;
                    return;
                }

                var value = ReadLocalTextFile("TerminalBootAnimation.txt")?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    _bootAnimationEnabled = !string.Equals(value, "off", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception)
            {
                _bootAnimationEnabled = true;
            }
        }

        private void SaveBootAnimationSettings()
        {
            try
            {
                _mainViewModel?.SetTerminalBootAnimationEnabled(_bootAnimationEnabled);
                WriteLocalTextFile("TerminalBootAnimation.txt", _bootAnimationEnabled ? "on" : "off");
            }
            catch (Exception)
            {
                // Ignore local preference save failures.
            }
        }

        private static string ReadLocalTextFile(string fileName)
        {
            var path = GetLocalSettingsPath(fileName);
            return System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : null;
        }

        private static void WriteLocalTextFile(string fileName, string value)
        {
            var path = GetLocalSettingsPath(fileName);
            var directory = System.IO.Path.GetDirectoryName(path);
            if (!System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            System.IO.File.WriteAllText(path, value ?? string.Empty);
        }

        private static string GetLocalSettingsPath(string fileName)
        {
            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MyNewsFeeder",
                fileName);
        }

        private async Task RunBootAnimationAsync()
        {
            IsExecuting = true;

            try
            {
                AddLine(TerminalLine.Header("MY NEWS FEEDER ROM BIOS v3.2"));
                AddLine(TerminalLine.Dim("Copyright (C) 1984-2026 My News Feeder Systems"));
                OutputChanged?.Invoke();
                await Task.Delay(420);

                AddLine(TerminalLine.Normal(string.Empty));
                await AddBootLineAsync("CPU: NewsCore 486DX processor initialized");
                await AddBootLineAsync("Video: Monochrome display adapter detected");
                await AddMemoryTestAsync();
                await AddBootLineAsync("Keyboard: detected");
                await AddBootLineAsync("Mouse: detected");
                await AddBootLineAsync("Feed controller: RSS/Atom interface ready");
                await AddBootLineAsync("Reader cache: online");

                AddLine(TerminalLine.Normal(string.Empty));
                await AddAnimatedStatusAsync("Scanning feed channels", 8);
                await AddAnimatedStatusAsync("Mounting article index", 7);
                await AddAnimatedStatusAsync("Starting terminal browser", 6);

                AddLine(TerminalLine.Normal(string.Empty));
                await RevealLogoAsync();

                AddLine(TerminalLine.Normal(string.Empty));
                await AddBootLineAsync("[ OK ] Ready.", TerminalLineType.Success, 320);
                AddLine(TerminalLine.Dim("Type 'help' for commands."));
                AddLine(TerminalLine.Normal(string.Empty));
                OutputChanged?.Invoke();
            }
            finally
            {
                IsExecuting = false;
            }
        }

        private async Task AddBootLineAsync(string text, TerminalLineType type = TerminalLineType.Dim, int delayMs = BootLineDelayMs)
        {
            AddLine(new TerminalLine(text, type));
            OutputChanged?.Invoke();
            await Task.Delay(delayMs);
        }

        private async Task AddMemoryTestAsync()
        {
            var startIndex = Lines.Count;
            var values = new[] { 64, 128, 256, 384, 512, 640 };
            foreach (var value in values)
            {
                RemoveLinesFrom(startIndex);
                AddLine(TerminalLine.Dim($"Memory Test: {value:000}K"));
                OutputChanged?.Invoke();
                await Task.Delay(120);
            }

            RemoveLinesFrom(startIndex);
            AddLine(TerminalLine.Dim("Memory Test: 640K OK"));
            OutputChanged?.Invoke();
            await Task.Delay(240);
        }

        private async Task AddAnimatedStatusAsync(string text, int steps)
        {
            var startIndex = Lines.Count;
            for (var i = 0; i <= steps; i++)
            {
                var dots = new string('.', i % 4);
                RemoveLinesFrom(startIndex);
                AddLine(TerminalLine.Dim($"{text}{dots}"));
                OutputChanged?.Invoke();
                await Task.Delay(BootFastDelayMs);
            }

            RemoveLinesFrom(startIndex);
            AddLine(TerminalLine.Success($"[ OK ] {text}"));
            OutputChanged?.Invoke();
            await Task.Delay(180);
        }

        private void AddWelcomeLines()
        {
            AddLine(TerminalLine.Header("My News Feeder Terminal"));
            AddLine(TerminalLine.Dim("Type 'help' for commands."));
            AddLine(TerminalLine.Normal(string.Empty));
        }

        private void RemoveLinesFrom(int startIndex)
        {
            while (Lines.Count > startIndex)
            {
                Lines.RemoveAt(Lines.Count - 1);
            }
        }

        private async Task RevealLogoAsync()
        {
            AddLine(TerminalLine.Header("MY NEWS FEEDER TERMINAL"));
            AddLine(TerminalLine.Dim("======================="));
            OutputChanged?.Invoke();
            await Task.Delay(180);

            foreach (var line in FinalLogo)
            {
                AddLine(TerminalLine.Header(line));
                OutputChanged?.Invoke();
                await Task.Delay(BootLogoDelayMs);
            }
        }

        private static readonly string[] FinalLogo = NormalizeAsciiLogo("""
                                      ###
           ###############         ######
        #####    ####    ####   ####  ###
      ###  #### ##          #####     ##
    ###    #### ##         ###    ######
   ###     ###  ###      ###    ###  ##
  ##      ####    ###  ###    ###   ###
 ##     ###     #### ###    ###    ###
### ######     ###  ###   ###  ######
######         ### ###   ###   ######
#######        ### ##  ###     ### ###
##   #########  ##### ###     ###  ###
###  ###    ###   ## ###    ####  ####
########     #### #####   ###########
 ##   ###      ####### #### ##    ###
 ###   ####   ### #######  ###   ###
  ###    ##  ##  ###   #####    ###
    ####### ##  ###     ## #######
     #### ################## ###
       #### ## ###     #######
          #################
                 ####
              MY NEWS FEEDER
""");

        private static string[] NormalizeAsciiLogo(string logo)
        {
            var lines = (logo ?? string.Empty)
                .Replace("\r\n", "\n")
                .Split('\n')
                .Select(line => line.TrimEnd())
                .ToList();

            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
            {
                lines.RemoveAt(0);
            }

            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[lines.Count - 1]))
            {
                lines.RemoveAt(lines.Count - 1);
            }

            return lines.ToArray();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public event Action CloseRequested;
        public event Action OutputChanged;

        public void TriggerOutputChanged()
        {
            OutputChanged?.Invoke();
        }

        public ObservableCollection<TerminalLine> Lines { get; } = new ObservableCollection<TerminalLine>();
        public ObservableCollection<string> History { get; } = new ObservableCollection<string>();
        public ObservableCollection<TerminalFeedEntry> BrowseFeeds { get; } = new ObservableCollection<TerminalFeedEntry>();
        public ObservableCollection<TerminalArticleEntry> BrowseArticles { get; } = new ObservableCollection<TerminalArticleEntry>();
        public ICommand ExecuteCommand { get; }
        public string OutputText => string.Join(Environment.NewLine, Lines.Select(line => line.Text));

        public bool IsExecuting
        {
            get => _isExecuting;
            private set
            {
                if (_isExecuting == value)
                {
                    return;
                }

                _isExecuting = value;
                OnPropertyChanged();
                (ExecuteCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public string CurrentInput
        {
            get => _currentInput;
            set
            {
                if (_currentInput == value)
                {
                    return;
                }

                _currentInput = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        public bool IsBrowseMode
        {
            get => _isBrowseMode;
            private set
            {
                if (_isBrowseMode == value)
                {
                    return;
                }

                _isBrowseMode = value;
                OnPropertyChanged();
            }
        }

        public string BrowseStatus
        {
            get => _browseStatus;
            set
            {
                if (_browseStatus == value)
                {
                    return;
                }

                _browseStatus = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        public string BrowseTitle
        {
            get => _browseTitle;
            private set
            {
                if (_browseTitle == value)
                {
                    return;
                }

                _browseTitle = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        public string ReaderText
        {
            get => _readerText;
            private set
            {
                if (_readerText == value)
                {
                    return;
                }

                _readerText = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        public TerminalFeedEntry SelectedBrowseFeed
        {
            get => _selectedBrowseFeed;
            set
            {
                if (_selectedBrowseFeed == value)
                {
                    return;
                }

                _selectedBrowseFeed = value;
                OnPropertyChanged();
                LoadBrowseArticles(value);
            }
        }

        public TerminalArticleEntry SelectedBrowseArticle
        {
            get => _selectedBrowseArticle;
            set
            {
                if (_selectedBrowseArticle == value)
                {
                    return;
                }

                _selectedBrowseArticle = value;
                OnPropertyChanged();
                ShowArticleSummary(value);
            }
        }

        private void AddLine(TerminalLine line)
        {
            Lines.Add(line);
        }

        public void ShowNotificationActivation(ImportantNotificationItem item)
        {
            if (item == null)
            {
                return;
            }

            AddLine(TerminalLine.Normal(string.Empty));
            AddLine(TerminalLine.Header("Windows notification clicked"));
            if (!string.IsNullOrWhiteSpace(item.Title))
            {
                AddLine(TerminalLine.Success($"Title: {item.Title.Trim()}"));
            }

            if (!string.IsNullOrWhiteSpace(item.FeedName))
            {
                AddLine(TerminalLine.Normal($"Feed: {item.FeedName.Trim()}"));
            }

            if (!string.IsNullOrWhiteSpace(item.Link))
            {
                AddLine(TerminalLine.Dim($"Link: {item.Link.Trim()}"));
            }

            AddLine(TerminalLine.Normal(string.Empty));
            if (IsBrowseMode)
            {
                BrowseStatus = string.IsNullOrWhiteSpace(item.Title)
                    ? "Notification clicked"
                    : $"Notification: {item.Title.Trim()}";
            }

            OutputChanged?.Invoke();
        }

        public void ShowLatestNotificationsActivation(IEnumerable<ImportantNotificationItem> items)
        {
            var snapshot = (items ?? Enumerable.Empty<ImportantNotificationItem>())
                .Where(item => item != null)
                .Take(8)
                .ToList();

            AddLine(TerminalLine.Normal(string.Empty));
            AddLine(TerminalLine.Header("Windows notification center clicked"));
            if (snapshot.Count == 0)
            {
                AddLine(TerminalLine.Dim("No recent important notifications."));
            }
            else
            {
                for (var i = 0; i < snapshot.Count; i++)
                {
                    var item = snapshot[i];
                    var title = string.IsNullOrWhiteSpace(item.Title) ? "(untitled)" : item.Title.Trim();
                    var feed = string.IsNullOrWhiteSpace(item.FeedName) ? "Feed" : item.FeedName.Trim();
                    AddLine(TerminalLine.Success($"{i + 1}. {title}"));
                    AddLine(TerminalLine.Dim($"   {feed}  {item.ReceivedAt:yyyy-MM-dd HH:mm}"));
                }
            }

            AddLine(TerminalLine.Normal(string.Empty));
            if (IsBrowseMode)
            {
                BrowseStatus = snapshot.Count == 0
                    ? "Notification center clicked"
                    : $"Notification center: {snapshot.Count} recent item(s)";
            }

            OutputChanged?.Invoke();
        }

        private TerminalLineType MapStringToLineType(string lineType)
        {
            return lineType switch
            {
                "Error" => TerminalLineType.Error,
                "Success" => TerminalLineType.Success,
                "Warning" => TerminalLineType.Warning,
                "Command" => TerminalLineType.Command,
                "Header" => TerminalLineType.Header,
                "Dim" => TerminalLineType.Dim,
                _ => TerminalLineType.Normal
            };
        }

        public async Task ExecuteCurrentInputAsync()
        {
            if (IsExecuting)
            {
                return;
            }

            var input = CurrentInput?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(input))
            {
                return;
            }

            AddLine(TerminalLine.Command($"> {input}"));
            if (History.Count == 0 || !string.Equals(History[History.Count - 1], input, StringComparison.Ordinal))
            {
                History.Add(input);
            }
            CurrentInput = string.Empty;

            IsExecuting = true;
            var isReaderCommand = input.StartsWith("read ", StringComparison.OrdinalIgnoreCase) ||
                                  input.StartsWith("reader ", StringComparison.OrdinalIgnoreCase);
            if (isReaderCommand)
            {
                AddLine(TerminalLine.Dim("Loading Reader Mode text..."));
                OutputChanged?.Invoke();
            }

            if (input.StartsWith("bootanim", StringComparison.OrdinalIgnoreCase))
            {
                await ExecuteBootAnimationCommandAsync(input);
                IsExecuting = false;
                OutputChanged?.Invoke();
                return;
            }

            // Handle theme command locally (before calling command service)
            if (input.Equals("theme", StringComparison.OrdinalIgnoreCase) ||
                input.StartsWith("theme ", StringComparison.OrdinalIgnoreCase))
            {
                var themeName = input.Length > 5 ? input.Substring(6).Trim() : string.Empty;
                if (string.IsNullOrWhiteSpace(themeName))
                {
                    // List available themes
                    var themes = string.Join(", ", AvailableThemes);
                    AddLine(TerminalLine.Success($"Available themes: {themes}"));
                    AddLine(TerminalLine.Dim($"Current theme: {CurrentTheme.Name}"));
                    AddLine(TerminalLine.Normal(string.Empty));
                    IsExecuting = false;
                    OutputChanged?.Invoke();
                    return;
                }
                
                if (!SetTheme(themeName))
                {
                    AddLine(TerminalLine.Error($"Unknown theme: {themeName}"));
                    AddLine(TerminalLine.Dim($"Available themes: {string.Join(", ", AvailableThemes)}"));
                    AddLine(TerminalLine.Normal(string.Empty));
                    IsExecuting = false;
                    OutputChanged?.Invoke();
                    return;
                }

                AddLine(TerminalLine.Success($"Theme changed to: {CurrentTheme.Name}"));
                AddLine(TerminalLine.Normal(string.Empty));
                IsExecuting = false;
                OutputChanged?.Invoke();
                return;
            }

            TerminalCommandResult result;
            try
            {
                result = await _commandService.ExecuteAsync(input);
            }
            catch (Exception ex)
            {
                result = TerminalCommandResult.Text($"Command failed: {ex.Message}", isError: true);
            }
            finally
            {
                IsExecuting = false;
            }
            if (result.ClearScreen)
            {
                Lines.Clear();
            }
            else if (result.BrowseRequested)
            {
                EnterBrowseMode();
            }
            else if (!string.IsNullOrWhiteSpace(result.Output))
            {
                var lineType = result.IsError ? TerminalLineType.Error :
                               result.IsSuccess ? TerminalLineType.Success :
                               MapStringToLineType(result.LineType);
                
                foreach (var line in result.Output.Replace("\r\n", "\n").Split('\n'))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        AddLine(new TerminalLine(line, lineType));
                    }
                }
            }

            AddLine(TerminalLine.Normal(string.Empty));
            OutputChanged?.Invoke();

            if (result.CloseRequested)
            {
                CloseRequested?.Invoke();
            }
        }

        private async Task ExecuteBootAnimationCommandAsync(string input)
        {
            var args = TokenizeInput(input);
            var mode = args.Count > 1 ? args[1].ToLowerInvariant() : "status";

            if (mode == "on")
            {
                _bootAnimationEnabled = true;
                SaveBootAnimationSettings();
                AddLine(TerminalLine.Success("Boot animation enabled."));
                AddLine(TerminalLine.Normal(string.Empty));
                return;
            }

            if (mode == "off")
            {
                _bootAnimationEnabled = false;
                SaveBootAnimationSettings();
                AddLine(TerminalLine.Success("Boot animation disabled."));
                AddLine(TerminalLine.Normal(string.Empty));
                return;
            }

            if (mode == "preview")
            {
                await RunBootAnimationAsync();
                AddLine(TerminalLine.Dim("Boot animation preview complete."));
                AddLine(TerminalLine.Normal(string.Empty));
                return;
            }

            AddLine(TerminalLine.Normal($"Boot animation: {(_bootAnimationEnabled ? "on" : "off")}"));
            AddLine(TerminalLine.Dim("Usage: bootanim on | bootanim off | bootanim preview"));
            AddLine(TerminalLine.Normal(string.Empty));
        }

        public void EnterBrowseMode()
        {
            IsBrowseMode = true;
            RefreshBrowseFeeds();
            BrowseStatus = BrowseHelpStatus;
        }

        public void ExitBrowseMode()
        {
            IsBrowseMode = false;
            BrowseStatus = "Command mode.";
            OutputChanged?.Invoke();
        }

        // Methode zum Öffnen eines Artikels im Browser
        public void OpenArticleInBrowser(FeedItem item)
        {
            if (item != null)
            {
                _mainViewModel.TryOpenArticleExternally(item);
            }
        }

        // Methode zum Aktualisieren eines Feeds
        public async Task RefreshFeedAsync(FeedGroupViewModel feed)
        {
            if (feed != null && _mainViewModel.RefreshCommand?.CanExecute(null) == true)
            {
                BrowseStatus = "Refreshing feed...";
                _mainViewModel.RefreshCommand.Execute(null);
                
                // Warte auf Refresh-Fertigstellung durch Beobachtung von IsLoading
                // oder warte maximal 5 Sekunden
                var startTime = DateTime.Now;
                while (_mainViewModel.IsLoading && (DateTime.Now - startTime).TotalSeconds < 5)
                {
                    await Task.Delay(200);
                }
                
                // Setze Status zurück
                BrowseStatus = BrowseHelpStatus;
            }
        }

        // Synchroner Wrapper für Kompatibilität
        public void RefreshFeed(FeedGroupViewModel feed)
        {
            _ = RefreshFeedAsync(feed);
        }

        // Methode zum Markieren eines Artikels als gelesen
        public void MarkRead(FeedItem item)
        {
            if (item != null)
            {
                _mainViewModel.MarkArticleAsReadFromExternalView(item);
                // Aktualisiere die Anzeige
                if (SelectedBrowseArticle?.Item == item)
                {
                    ShowArticleSummary(SelectedBrowseArticle);
                }
            }
        }

        public void MarkUnread(FeedItem item)
        {
            if (item != null && _mainViewModel.MarkUnreadCommand?.CanExecute(item) == true)
            {
                _mainViewModel.MarkUnreadCommand.Execute(item);
                if (SelectedBrowseArticle?.Item == item)
                {
                    ShowArticleSummary(SelectedBrowseArticle);
                }
            }
        }

        public void ToggleReadState(FeedItem item)
        {
            if (item == null)
            {
                return;
            }

            if (item.IsUnread)
            {
                MarkRead(item);
            }
            else
            {
                MarkUnread(item);
            }
        }

        public void MoveBrowseFeed(int delta)
        {
            if (BrowseFeeds.Count == 0)
            {
                return;
            }

            var current = SelectedBrowseFeed == null ? 0 : BrowseFeeds.IndexOf(SelectedBrowseFeed);
            int next;
            
            if (delta == int.MaxValue)
            {
                next = BrowseFeeds.Count - 1; // Letzter Feed
            }
            else if (delta == -int.MaxValue)
            {
                next = 0; // Erster Feed
            }
            else
            {
                next = Math.Max(0, Math.Min(BrowseFeeds.Count - 1, current + delta));
            }
            
            SelectedBrowseFeed = BrowseFeeds[next];
        }

        public void MoveBrowseArticle(int delta)
        {
            if (BrowseArticles.Count == 0)
            {
                return;
            }

            var current = SelectedBrowseArticle == null ? 0 : BrowseArticles.IndexOf(SelectedBrowseArticle);
            int next;
            
            if (delta == int.MaxValue)
            {
                next = BrowseArticles.Count - 1; // Letzter Artikel
            }
            else if (delta == -int.MaxValue)
            {
                next = 0; // Erster Artikel
            }
            else
            {
                next = Math.Max(0, Math.Min(BrowseArticles.Count - 1, current + delta));
            }
            
            SelectedBrowseArticle = BrowseArticles[next];
        }

        public async Task LoadSelectedBrowseReaderAsync()
        {
            if (IsExecuting || SelectedBrowseArticle?.Item == null)
            {
                return;
            }

            IsExecuting = true;
            BrowseStatus = "Loading Reader Mode text...";
            try
            {
                var item = SelectedBrowseArticle.Item;
                _mainViewModel.OnArticleSelected(item, enableAutoScroll: false, preserveOpenWebViews: true);
                MarkRead(item);
                var text = await LoadReaderTextAsync(item);
                if (string.IsNullOrWhiteSpace(text))
                {
                    text = StripAndCollapse(item.Description);
                }

                BrowseTitle = $"{SelectedBrowseArticle.Number}. {item.Title}";
                ReaderText = BuildReaderPane(item, text, readerMode: true);
                BrowseStatus = "Reader loaded. Up/Down: article  Enter: reload reader  Esc/q: command";
            }
            finally
            {
                IsExecuting = false;
            }
        }

        private void RefreshBrowseFeeds()
        {
            BrowseFeeds.Clear();
            var feeds = (_mainViewModel.CategoryGroups ?? Enumerable.Empty<CategoryGroupViewModel>())
                .SelectMany(category => category.Feeds ?? Enumerable.Empty<FeedGroupViewModel>())
                .Where(feed => feed != null)
                .OrderBy(feed => feed.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(feed => feed.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var i = 0; i < feeds.Count; i++)
            {
                BrowseFeeds.Add(new TerminalFeedEntry(i + 1, feeds[i]));
            }

            SelectedBrowseFeed = BrowseFeeds.FirstOrDefault();
        }

        private void LoadBrowseArticles(TerminalFeedEntry feedEntry)
        {
            BrowseArticles.Clear();
            ReaderText = string.Empty;

            if (feedEntry?.Feed == null)
            {
                BrowseTitle = "No feed selected";
                BrowseStatus = "No feeds loaded. Try refresh first.";
                return;
            }

            var items = (feedEntry.Feed.Items ?? Enumerable.Empty<FeedItem>())
                .Where(item => item != null)
                .OrderByDescending(item => item.PublicationDate)
                .Take(200)
                .ToList();

            for (var i = 0; i < items.Count; i++)
            {
                BrowseArticles.Add(new TerminalArticleEntry(i + 1, items[i]));
            }

            BrowseTitle = $"{feedEntry.Feed.Name} [{feedEntry.Feed.Category}]";
            SelectedBrowseArticle = BrowseArticles.FirstOrDefault();
            if (BrowseArticles.Count == 0)
            {
                ReaderText = "No articles loaded for this feed.";
            }
        }

        private void ShowArticleSummary(TerminalArticleEntry articleEntry)
        {
            if (articleEntry?.Item == null)
            {
                ReaderText = string.Empty;
                return;
            }

            var item = articleEntry.Item;
            BrowseTitle = $"{articleEntry.Number}. {item.Title}";
            var summary = StripAndCollapse(item.Description);
            ReaderText = BuildReaderPane(item, summary, readerMode: false);
        }

        private async Task<string> LoadReaderTextAsync(FeedItem item)
        {
            if (string.IsNullOrWhiteSpace(item?.Link))
            {
                return string.Empty;
            }

            try
            {
                // Prüfe zuerst den in-Memory Cache
                if (!_readerContentCache.TryGetValue(item.Link, out var readerContent))
                {
                    // Versuche, aus der persistenten Cache-Datei zu laden
                    readerContent = await LoadFromPersistentCache(item.Link);
                    
                    // Wenn nicht in persistentem Cache, von Web laden
                    if (readerContent == null)
                    {
                        readerContent = await _articleReaderService.ExtractAsync(item.Link);
                        if (readerContent != null)
                        {
                            _readerContentCache[item.Link] = readerContent;
                            // Speichere in persistentem Cache
                            await SaveToPersistentCache(item.Link, readerContent);
                        }
                    }
                    else
                    {
                        _readerContentCache[item.Link] = readerContent;
                    }
                }

                if (readerContent == null)
                {
                    return string.Empty;
                }

                return string.IsNullOrWhiteSpace(readerContent.PlainText)
                    ? StripAndCollapse(readerContent.HtmlContent)
                    : readerContent.PlainText;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private async Task<ReaderArticleContent> LoadFromPersistentCache(string url)
        {
            try
            {
                var cache = await LoadPersistentReaderCacheAsync();
                if (cache.TryGetValue(url, out var entry) && !IsExpiredPersistentReaderCacheEntry(entry))
                {
                    return entry.Content;
                }
            }
            catch (Exception)
            {
                // Ignorieren
            }
            return null;
        }

        private async Task SaveToPersistentCache(string url, ReaderArticleContent content)
        {
            try
            {
                var cache = await LoadPersistentReaderCacheAsync();
                cache[url] = new TerminalReaderCacheEntry
                {
                    Content = content,
                    CachedAtUtc = DateTime.UtcNow
                };

                cache = TrimPersistentReaderCache(cache);
                var directory = System.IO.Path.GetDirectoryName(_cacheFilePath);
                if (!System.IO.Directory.Exists(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                }

                var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                await System.IO.File.WriteAllTextAsync(_cacheFilePath, System.Text.Json.JsonSerializer.Serialize(cache, options));
            }
            catch (Exception)
            {
                // Ignorieren
            }
        }

        private static async Task<Dictionary<string, TerminalReaderCacheEntry>> LoadPersistentReaderCacheAsync()
        {
            var cache = new Dictionary<string, TerminalReaderCacheEntry>(StringComparer.OrdinalIgnoreCase);
            if (!System.IO.File.Exists(_cacheFilePath))
            {
                return cache;
            }

            var json = await System.IO.File.ReadAllTextAsync(_cacheFilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return cache;
            }

            try
            {
                var currentCache = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, TerminalReaderCacheEntry>>(json);
                if (currentCache != null && (currentCache.Count == 0 || currentCache.Any(entry => entry.Value?.Content != null)))
                {
                    return currentCache
                        .Where(entry => !string.IsNullOrWhiteSpace(entry.Key) && entry.Value?.Content != null)
                        .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (JsonException)
            {
                // Try the previous cache shape below.
            }

            var legacyCache = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, ReaderArticleContent>>(json);
            if (legacyCache == null)
            {
                return cache;
            }

            var cachedAtUtc = DateTime.UtcNow;
            foreach (var entry in legacyCache.Where(entry => !string.IsNullOrWhiteSpace(entry.Key) && entry.Value != null))
            {
                cache[entry.Key] = new TerminalReaderCacheEntry
                {
                    Content = entry.Value,
                    CachedAtUtc = cachedAtUtc
                };
            }

            return cache;
        }

        private static Dictionary<string, TerminalReaderCacheEntry> TrimPersistentReaderCache(Dictionary<string, TerminalReaderCacheEntry> cache)
        {
            var cutoffUtc = DateTime.UtcNow.Subtract(PersistentReaderCacheRetention);
            return (cache ?? new Dictionary<string, TerminalReaderCacheEntry>(StringComparer.OrdinalIgnoreCase))
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Key) &&
                                entry.Value?.Content != null &&
                                entry.Value.CachedAtUtc >= cutoffUtc)
                .OrderByDescending(entry => entry.Value.CachedAtUtc)
                .Take(MaxPersistentReaderCacheEntries)
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsExpiredPersistentReaderCacheEntry(TerminalReaderCacheEntry entry)
        {
            return entry == null ||
                   entry.Content == null ||
                   entry.CachedAtUtc < DateTime.UtcNow.Subtract(PersistentReaderCacheRetention);
        }

        public static void ClearPersistentReaderCache()
        {
            try
            {
                if (System.IO.File.Exists(_cacheFilePath))
                {
                    System.IO.File.Delete(_cacheFilePath);
                }
            }
            catch (Exception)
            {
                // Ignore cache cleanup failures.
            }
        }

        private static readonly string _cacheFilePath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MyNewsFeeder", "TerminalReaderCache.json");

        private sealed class TerminalReaderCacheEntry
        {
            public ReaderArticleContent Content { get; set; }
            public DateTime CachedAtUtc { get; set; }
        }

        private static string BuildReaderPane(FeedItem item, string body, bool readerMode)
        {
            var mode = readerMode ? "READER" : "SUMMARY";
            var link = string.IsNullOrWhiteSpace(item.Link) ? string.Empty : $"\nLINK: {item.Link}";
            return $"{item.Title}\n{new string('=', Math.Min(80, Math.Max(8, item.Title?.Length ?? 8)))}\nFEED: {item.FeedName}\nDATE: {item.PublicationDate:yyyy-MM-dd HH:mm}\nMODE: {mode}{link}\n\n{body}";
        }

        private static string StripAndCollapse(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var withoutTags = Regex.Replace(value, "<.*?>", " ");
            var decoded = WebUtility.HtmlDecode(withoutTags);
            return Regex.Replace(decoded, "\\s+", " ").Trim();
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Tab-Completion: Rückgabe aller möglichen Vervollständigungen für den aktuellen Input
        // Unterstützt: Befehle, Befehle mit Parametern, Feed-Namen
        // Linux-Terminal-Feeling: Intelligente Vervollständigung basierend auf Kontext
        public List<string> GetCompletions(string input)
        {
            var result = new List<string>();
            
            if (string.IsNullOrWhiteSpace(input))
            {
                // Leere Eingabe: Zeige alle Hauptbefehle
                return GetAllBaseCommands();
            }

            // Parse Input in Befehl und Argumente
            var parts = TokenizeInput(input);
            var command = parts.Count > 0 ? parts[0] : string.Empty;
            var args = parts.Count > 1 ? string.Join(" ", parts.Skip(1)) : string.Empty;

            // Fall 1: Nur Befehl (ohne Leerzeichen am Ende)
            if (parts.Count == 1 && !input.EndsWith(" "))
            {
                // Befehl-Vervollständigung
                result.AddRange(GetCommandCompletions(command));
                
                // Feed-Namen Vervollständigung (falls Befehl "feed" oder leer)
                if (string.IsNullOrWhiteSpace(command) || command == "feed")
                {
                    result.AddRange(GetFeedNameCompletions(args));
                }
                
                // Thema-Namen Vervollständigung (falls Befehl "theme")
                if (command == "theme")
                {
                    result.AddRange(GetThemeCompletions(args));
                }

                if (command == "bootanim")
                {
                    result.AddRange(GetBootAnimationCompletions(args));
                }
            }
            // Fall 2: Befehl + Leerzeichen (Parameter werden erwartet)
            else if (input.EndsWith(" "))
            {
                // Zeige mögliche Parameter für den Befehl
                result.AddRange(GetCommandParameters(command).Select(parameter => BuildInputCompletion(command, parameter)));
            }
            // Fall 3: Befehl + Teil eines Parameters
            else if (parts.Count > 1)
            {
                // Vervollständige den Parameter basierend auf dem Befehl
                result.AddRange(GetParameterCompletions(command, args).Select(parameter => BuildInputCompletion(command, parameter)));
            }

            return result.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string BuildInputCompletion(string command, string parameter)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return parameter ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(parameter))
            {
                return command;
            }

            return $"{command} {parameter}";
        }

        private List<string> GetAllBaseCommands()
        {
            return new List<string>
            {
                "help", "browse", "read", "reader", "open", "unread", "search",
                "list", "feed", "refresh", "clear", "close", "exit", "quit", "?", "cls", "theme", "startup", "bootanim"
            };
        }

        private List<string> GetCommandCompletions(string partialCommand)
        {
            var allCommands = GetAllBaseCommands();
            return allCommands.Where(cmd => cmd.StartsWith(partialCommand, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private List<string> GetCommandParameters(string command)
        {
            return command.ToLower() switch
            {
                "list" => new List<string> { "feeds", "articles", "unread" },
                "refresh" => GetRefreshCompletions(string.Empty),
                "feed" => GetFeedNameCompletions(string.Empty),
                "theme" => GetThemeCompletions(string.Empty),
                "startup" => GetStartupCompletions(string.Empty),
                "bootanim" => GetBootAnimationCompletions(string.Empty),
                "open" => new List<string> { "<number>" },
                "read" => new List<string> { "<number>" },
                "reader" => new List<string> { "<number>" },
                "unread" => new List<string> { "<number>" },
                "search" => new List<string> { "<text>" },
                _ => new List<string>()
            };
        }

        private List<string> GetParameterCompletions(string command, string partialParam)
        {
            return command.ToLower() switch
            {
                "list" => GetListParameterCompletions(partialParam),
                "refresh" => GetRefreshCompletions(partialParam),
                "feed" => GetFeedNameCompletions(partialParam),
                "theme" => GetThemeCompletions(partialParam),
                "startup" => GetStartupCompletions(partialParam),
                "bootanim" => GetBootAnimationCompletions(partialParam),
                _ => new List<string>()
            };
        }

        private List<string> GetListParameterCompletions(string partialParam)
        {
            var paramsList = new[] { "feeds", "articles", "unread" };
            return paramsList.Where(p => p.StartsWith(partialParam, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private static List<string> GetRefreshCompletions(string partialParam)
        {
            var options = new[] { "status", "help", "auto", "interval", "live" };
            return options.Where(option => option.StartsWith(partialParam, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private List<string> GetFeedNameCompletions(string partialName)
        {
            var feeds = (_mainViewModel.CategoryGroups ?? Enumerable.Empty<CategoryGroupViewModel>())
                .SelectMany(category => category.Feeds ?? Enumerable.Empty<FeedGroupViewModel>())
                .Select(feed => feed?.Name ?? string.Empty)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();
            
            return feeds.Where(f => f.StartsWith(partialName, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private List<string> GetThemeCompletions(string partialName)
        {
            var themes = AvailableThemes;
            return themes.Where(t => t.StartsWith(partialName, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private static List<string> GetStartupCompletions(string partialName)
        {
            var modes = new[] { "terminal", "gui" };
            return modes.Where(mode => mode.StartsWith(partialName, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private static List<string> GetBootAnimationCompletions(string partialName)
        {
            var modes = new[] { "on", "off", "preview" };
            return modes.Where(mode => mode.StartsWith(partialName, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private static List<string> TokenizeInput(string input)
        {
            // Einfaches Tokenizing - ähnlich wie in TerminalCommandService
            var matches = System.Text.RegularExpressions.Regex.Matches(input ?? string.Empty, "\"([^\"]*)\"|\\S+");
            return matches
                .Cast<System.Text.RegularExpressions.Match>()
                .Select(match => match.Groups[1].Success ? match.Groups[1].Value : match.Value)
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .ToList();
        }
    }

    public class TerminalFeedEntry : INotifyPropertyChanged
    {
        public TerminalFeedEntry(int number, FeedGroupViewModel feed)
        {
            Number = number;
            Feed = feed;
            if (Feed != null)
            {
                Feed.PropertyChanged += Feed_PropertyChanged;
            }
        }

        public int Number { get; }
        public FeedGroupViewModel Feed { get; }
        public event PropertyChangedEventHandler PropertyChanged;
        public string Display => $"{Number,3}. {Feed?.Name} [{Feed?.UnreadCount}/{Feed?.ItemCount}]";

        private void Feed_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FeedGroupViewModel.Name) ||
                e.PropertyName == nameof(FeedGroupViewModel.UnreadCount) ||
                e.PropertyName == nameof(FeedGroupViewModel.ItemCount))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Display)));
            }
        }
    }

    public class TerminalArticleEntry : INotifyPropertyChanged
    {
        public TerminalArticleEntry(int number, FeedItem item)
        {
            Number = number;
            Item = item;
            if (Item != null)
            {
                Item.PropertyChanged += Item_PropertyChanged;
            }
        }

        public int Number { get; }
        public FeedItem Item { get; }
        public event PropertyChangedEventHandler PropertyChanged;
        public string Display
        {
            get
            {
                var state = Item?.IsUnread == true ? "*" : " ";
                return $"{Number,3}. [{state}] {Item?.Title}";
            }
        }

        private void Item_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FeedItem.IsUnread) ||
                e.PropertyName == nameof(FeedItem.IsRead) ||
                e.PropertyName == nameof(FeedItem.Title))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Display)));
            }
        }
    }
}
