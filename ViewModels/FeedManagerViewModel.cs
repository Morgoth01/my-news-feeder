using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Xml.Linq;
using Microsoft.Win32;
using MyNewsFeeder.Models;
using MyNewsFeeder.Services;
using System.Windows;

namespace MyNewsFeeder.ViewModels
{
    public class FeedManagerViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private readonly SettingsService _settingsService;
        private readonly FeedService _feedService;
        private Feed _selectedFeed;
        private Category _selectedCategory;
        private string _newCategoryName;
        private AppSettings _settings;
        private bool _groupFeedsByCategory;
        private bool _isDirty;
        public bool WasClosedBySave { get; private set; }

        public ObservableCollection<Feed> Feeds { get; set; }
        public ObservableCollection<Category> Categories { get; set; }
        public ObservableCollection<string> CategoryNames { get; set; }
        public bool IsDirty
        {
            get => _isDirty;
            private set
            {
                if (_isDirty != value)
                {
                    _isDirty = value;
                    OnPropertyChanged(nameof(IsDirty));
                }
            }
        }

        public Feed SelectedFeed
        {
            get => _selectedFeed;
            set
            {
                _selectedFeed = value;
                OnPropertyChanged(nameof(SelectedFeed));
                ((RelayCommand)RemoveFeedCommand).RaiseCanExecuteChanged();
            }
        }

        public Category SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                _selectedCategory = value;
                OnPropertyChanged(nameof(SelectedCategory));
                ((RelayCommand)RemoveCategoryCommand).RaiseCanExecuteChanged();
            }
        }

        public string NewCategoryName
        {
            get => _newCategoryName;
            set
            {
                _newCategoryName = value;
                OnPropertyChanged(nameof(NewCategoryName));
                ((RelayCommand)AddCategoryCommand).RaiseCanExecuteChanged();
            }
        }

        public bool GroupFeedsByCategory
        {
            get => _groupFeedsByCategory;
            set
            {
                if (_groupFeedsByCategory == value)
                {
                    return;
                }

                _groupFeedsByCategory = value;
                OnPropertyChanged(nameof(GroupFeedsByCategory));

                _settings.GroupFeedsByCategory = value;
                ApplyFeedOrdering();
                MarkDirty();
            }
        }

        // Commands
        public ICommand AddFeedCommand { get; }
        public ICommand RemoveFeedCommand { get; }
        public ICommand AddCategoryCommand { get; }
        public ICommand RemoveCategoryCommand { get; }
        public ICommand ImportCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand CloseCommand { get; }
        public ICommand MoveFeedCommand { get; }

        public FeedManagerViewModel(SettingsService settingsService, FeedService feedService = null)
        {
            _settingsService = settingsService;
            _feedService = feedService ?? new FeedService();
            _settings = _settingsService.LoadSettings();

            // Initialize collections
            var feedList = FeedService.NormalizeAndFilterFeeds(_settingsService.LoadFeeds());
            Feeds = new ObservableCollection<Feed>(feedList);
            _groupFeedsByCategory = _settings.GroupFeedsByCategory;
            // Initialize categories
            Categories = new ObservableCollection<Category>();
            CategoryNames = new ObservableCollection<string>();

            LoadCategories();
            if (_groupFeedsByCategory)
            {
                ApplyFeedOrdering();
            }


            OnPropertyChanged(nameof(GroupFeedsByCategory));

            // Subscribe to PropertyChanged events for auto-save
            foreach (var feed in Feeds)
            {
                feed.PropertyChanged += Feed_PropertyChanged;
            }

            // Initialize commands
            AddFeedCommand = new RelayCommand(_ => AddFeed());
            RemoveFeedCommand = new RelayCommand(_ => RemoveFeed(), _ => CanRemove());
            AddCategoryCommand = new RelayCommand(_ => AddCategory(), _ => CanAddCategory());
            RemoveCategoryCommand = new RelayCommand(_ => RemoveCategory(), _ => CanRemoveCategory());
            ImportCommand = new RelayCommand(_ => ImportFeeds());
            ExportCommand = new RelayCommand(_ => ExportFeeds());
            CloseCommand = new RelayCommand(param => CloseWindow(param));
            MoveFeedCommand = new RelayCommand(param => MoveFeed(param as FeedMoveRequest));
        }

        private void LoadCategories()
        {
            Categories.Clear();
            CategoryNames.Clear();

            var originalCategories = _settings.Categories != null
                ? new List<string>(_settings.Categories)
                : new List<string>();

            var merged = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddCategoryName(string name)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    return;
                }
                if (seen.Add(name))
                {
                    merged.Add(name);
                }
            }

            // Ensure Default category always exists
            AddCategoryName("Default");

            // Existing categories from settings (preserve order)
            foreach (var category in _settings.Categories)
            {
                AddCategoryName(category);
            }

            // Merge in categories found in feeds (so Feed Manager shows them after settings reset)
            foreach (var feed in Feeds)
            {
                AddCategoryName(string.IsNullOrWhiteSpace(feed.Category) ? "Default" : feed.Category);
            }

            // Persist merged list back to settings
            _settings.Categories = merged;
            var categoriesChanged = !originalCategories.SequenceEqual(merged, StringComparer.OrdinalIgnoreCase);
            if (categoriesChanged)
            {
                MarkDirty();
            }

            foreach (var categoryName in merged)
            {
                var category = new Category
                {
                    Name = categoryName,
                    Description = $"Category: {categoryName}",
                    IsExpanded = _settings.CategoryExpandedStates.ContainsKey(categoryName)
                        ? _settings.CategoryExpandedStates[categoryName]
                        : true
                };
                Categories.Add(category);
                CategoryNames.Add(categoryName);
            }
        }

        private void SaveCategories()
        {
            _settings.Categories = Categories.Select(c => c.Name).ToList();

            // Save expanded states
            foreach (var category in Categories)
            {
                _settings.CategoryExpandedStates[category.Name] = category.IsExpanded;
            }

            _settingsService.SaveSettings(_settings);
        }
        private void ApplyFeedOrdering()
        {
            if (!_groupFeedsByCategory || Feeds == null || Feeds.Count <= 1)
            {
                return;
            }

            var previousSelection = SelectedFeed;

            var sortedFeeds = Feeds
                .Select((feed, originalIndex) => new { feed, originalIndex })
                .OrderBy(x => GetCategoryOrder(x.feed.Category))
                .ThenBy(x => x.originalIndex)
                .Select(x => x.feed)
                .ToList();

            for (int targetIndex = 0; targetIndex < sortedFeeds.Count; targetIndex++)
            {
                var desired = sortedFeeds[targetIndex];
                if (!ReferenceEquals(Feeds[targetIndex], desired))
                {
                    var currentIndex = Feeds.IndexOf(desired);
                    if (currentIndex >= 0)
                    {
                        Feeds.Move(currentIndex, targetIndex);
                    }
                }
            }

            if (previousSelection != null && Feeds.Contains(previousSelection))
            {
                SelectedFeed = previousSelection;
            }
        }

        private int GetCategoryOrder(string category)
        {
            var normalized = NormalizeCategoryName(category);
            var categories = _settings.Categories ?? new List<string>();
            var index = categories.FindIndex(c => string.Equals(c, normalized, StringComparison.OrdinalIgnoreCase));
            return index >= 0 ? index : int.MaxValue;
        }

        private static string NormalizeCategoryName(string category)
        {
            return string.IsNullOrWhiteSpace(category) ? "Default" : category;
        }

        private void TrySaveSettings()
        {
            try
            {
                _settingsService.SaveSettings(_settings);
            }
            catch (Exception)
            {
                // Ignore persistence errors to keep UI responsive.
            }
        }

        private void Feed_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Feed.IsEnabled) || e.PropertyName == nameof(Feed.Category) || e.PropertyName == nameof(Feed.Name) || e.PropertyName == nameof(Feed.Url))
            {
                if (e.PropertyName == nameof(Feed.Url))
                {
                    _ = TryAutoFillFeedNameAsync(sender as Feed);
                }

                if (_groupFeedsByCategory && e.PropertyName == nameof(Feed.Category))
                {
                    ApplyFeedOrdering();
                }

                MarkDirty();
            }
        }

        private bool CanRemove()
        {
            return SelectedFeed != null;
        }

        private bool CanAddCategory()
        {
            return !string.IsNullOrWhiteSpace(NewCategoryName) &&
                   !CategoryNames.Contains(NewCategoryName.Trim());
        }

        private bool CanRemoveCategory()
        {
            return SelectedCategory != null && SelectedCategory.Name != "Default";
        }

        private void AddFeed()
        {
            var newFeed = new Feed
            {
                Name = "New Feed",
                Url = "https://example.com/rss",
                IsEnabled = true,
                Category = CategoryNames.FirstOrDefault() ?? "Default"
            };

            // Subscribe to PropertyChanged
            newFeed.PropertyChanged += Feed_PropertyChanged;

            Feeds.Add(newFeed);
            if (_groupFeedsByCategory)
            {
                ApplyFeedOrdering();
            }

            SelectedFeed = newFeed;
            MarkDirty();
        }

        private void RemoveFeed()
        {
            if (SelectedFeed == null) return;

            var feedToRemove = SelectedFeed;
            Feeds.Remove(feedToRemove);
            SelectedFeed = Feeds.FirstOrDefault();
            MarkDirty();
        }

        private void AddCategory()
        {
            if (!CanAddCategory()) return;

            var categoryName = NewCategoryName.Trim();
            var newCategory = new Category
            {
                Name = categoryName,
                Description = $"Category: {categoryName}",
                IsExpanded = true
            };

            Categories.Add(newCategory);
            CategoryNames.Add(categoryName);

            NewCategoryName = string.Empty;
            MarkDirty();
        }

        private void RemoveCategory()
        {
            if (!CanRemoveCategory()) return;

            var categoryToRemove = SelectedCategory;
            var categoryName = categoryToRemove.Name;

            // Move feeds from this category to Default
            var feedsToMove = Feeds.Where(f => f.Category == categoryName).ToList();
            foreach (var feed in feedsToMove)
            {
                feed.Category = "Default";
            }

            Categories.Remove(categoryToRemove);
            CategoryNames.Remove(categoryName);

            SelectedCategory = Categories.FirstOrDefault();
            MarkDirty();

            MessageBox.Show($"Category '{categoryName}' removed. {feedsToMove.Count} feeds moved to 'Default' category.",
                "Category Removed", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public void ReorderCategory(Category draggedCategory, Category targetCategory)
        {
            try
            {
                var draggedIndex = Categories.IndexOf(draggedCategory);
                var targetIndex = Categories.IndexOf(targetCategory);

                if (draggedIndex >= 0 && targetIndex >= 0 && draggedIndex != targetIndex)
                {
                    // Remove from old position
                    Categories.RemoveAt(draggedIndex);

                    // Insert at new position
                    if (draggedIndex < targetIndex)
                    {
                        // Moving down, adjust target index
                        Categories.Insert(targetIndex - 1, draggedCategory);
                    }
                    else
                    {
                        // Moving up
                        Categories.Insert(targetIndex, draggedCategory);
                    }

                    // Update selection
                    SelectedCategory = draggedCategory;

                    // Save changes
                    if (_groupFeedsByCategory)
                    {
                        ApplyFeedOrdering();
                    }
                    MarkDirty();
                }
            }
            catch (Exception)
            {
                // Ignore reorder failures; UI remains unchanged.
            }
        }

        private void ImportFeeds()
        {
            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Title = "Import Feeds",
                    Filter = "OPML Files (*.opml)|*.opml|JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                    FilterIndex = 1
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    var filePath = openFileDialog.FileName;
                    var extension = Path.GetExtension(filePath).ToLowerInvariant();

                    List<Feed> importedFeeds;

                    switch (extension)
                    {
                        case ".opml":
                            importedFeeds = ImportFromOpml(filePath);
                            break;
                        case ".json":
                            importedFeeds = ImportFromJson(filePath);
                            break;
                        default:
                            // Try to detect format by content
                            var content = File.ReadAllText(filePath);
                            if (content.TrimStart().StartsWith("<?xml") || content.Contains("<opml"))
                            {
                                importedFeeds = ImportFromOpml(filePath);
                            }
                            else if (content.TrimStart().StartsWith("[") || content.TrimStart().StartsWith("{"))
                            {
                                importedFeeds = ImportFromJson(filePath);
                            }
                            else
                            {
                                throw new NotSupportedException("Unsupported file format. Please use OPML or JSON files.");
                            }
                            break;
                    }

                    if (importedFeeds.Count > 0)
                    {
                        var duplicateCount = 0;
                        var addedCount = 0;

                        foreach (var feed in importedFeeds)
                        {
                            // Ensure feed has a valid category
                            if (string.IsNullOrWhiteSpace(feed.Category) || !CategoryNames.Contains(feed.Category))
                            {
                                feed.Category = "Default";
                            }

                            // Check for duplicates by URL
                            if (!Feeds.Any(f => f.Url.Equals(feed.Url, StringComparison.OrdinalIgnoreCase)))
                            {
                                feed.PropertyChanged += Feed_PropertyChanged;
                                Feeds.Add(feed);
                                addedCount++;
                            }
                            else
                            {
                                duplicateCount++;
                            }
                        }

                        if (_groupFeedsByCategory)
                        {
                            ApplyFeedOrdering();
                        }
                        MarkDirty();

                        var message = $"Import completed!\n\nAdded: {addedCount} feeds\nSkipped duplicates: {duplicateCount} feeds";
                        MessageBox.Show(message, "Import Successful",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("No feeds found in the selected file.", "Import Warning",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error importing feeds: {ex.Message}", "Import Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportFeeds()
        {
            try
            {
                if (Feeds.Count == 0)
                {
                    MessageBox.Show("No feeds to export.", "Export Warning",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var saveFileDialog = new SaveFileDialog
                {
                    Title = "Export Feeds",
                    Filter = "OPML Files (*.opml)|*.opml|JSON Files (*.json)|*.json",
                    FilterIndex = 1,
                    FileName = $"MyNewsFeeder_Feeds_{DateTime.Now:yyyy-MM-dd}"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    var filePath = saveFileDialog.FileName;
                    var extension = Path.GetExtension(filePath).ToLowerInvariant();

                    switch (extension)
                    {
                        case ".opml":
                            ExportToOpml(filePath);
                            break;
                        case ".json":
                            ExportToJson(filePath);
                            break;
                        default:
                            throw new NotSupportedException("Unsupported file format.");
                    }

                    MessageBox.Show($"Successfully exported {Feeds.Count} feeds to:\n{filePath}",
                        "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting feeds: {ex.Message}", "Export Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // OPML Import with category support
        private List<Feed> ImportFromOpml(string filePath)
        {
            var feeds = new List<Feed>();

            var doc = XDocument.Load(filePath);
            var outlines = doc.Descendants("outline")
                .Where(o => o.Attribute("xmlUrl") != null);

            foreach (var outline in outlines)
            {
                var feed = new Feed
                {
                    Name = outline.Attribute("title")?.Value ??
                           outline.Attribute("text")?.Value ??
                           "Unnamed Feed",
                    Url = outline.Attribute("xmlUrl")?.Value ?? string.Empty,
                    IsEnabled = true,
                    Category = outline.Attribute("category")?.Value ?? "Default"
                };

                if (FeedService.TryNormalizeFeedUrl(feed.Url, out var normalizedUrl))
                {
                    feed.Url = normalizedUrl;
                    feeds.Add(feed);
                }
            }

            return feeds;
        }

        // JSON Import
        private List<Feed> ImportFromJson(string filePath)
        {
            var json = File.ReadAllText(filePath);
            var feeds = JsonSerializer.Deserialize<List<Feed>>(json);
            if (feeds == null)
            {
                return new List<Feed>();
            }

            var validFeeds = new List<Feed>();

            foreach (var feed in feeds)
            {
                if (FeedService.TryNormalizeFeedUrl(feed.Url, out var normalizedUrl))
                {
                    feed.Url = normalizedUrl;
                    validFeeds.Add(feed);
                }
            }

            return validFeeds;
        }

        // OPML Export with category support
        private void ExportToOpml(string filePath)
        {
            var doc = new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement("opml",
                    new XAttribute("version", "1.0"),
                    new XElement("head",
                        new XElement("title", "MyNewsFeeder Feeds"),
                        new XElement("dateCreated", DateTime.Now.ToString("R")),
                        new XElement("ownerName", "MyNewsFeeder")
                    ),
                    new XElement("body",
                        Feeds.Select(feed =>
                            new XElement("outline",
                                new XAttribute("type", "rss"),
                                new XAttribute("text", feed.Name),
                                new XAttribute("title", feed.Name),
                                new XAttribute("xmlUrl", feed.Url),
                                new XAttribute("category", feed.Category),
                                new XAttribute("isEnabled", feed.IsEnabled.ToString().ToLower())
                            )
                        )
                    )
                )
            );

            doc.Save(filePath);
        }

        // JSON Export
        private void ExportToJson(string filePath)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(Feeds.ToList(), options);
            File.WriteAllText(filePath, json);
        }

        private void CloseWindow(object parameter)
        {
            if (parameter is Window window)
            {
                WasClosedBySave = true;

                if (IsDirty)
                {
                    SaveFeeds();
                    SaveCategories();
                    TrySaveSettings();
                }

                try
                {
                    if (window.IsLoaded)
                    {
                        // Signal that the Save button was used (even if nothing changed) so callers can decide to reload.
                        window.DialogResult = true;
                    }
                }
                catch (InvalidOperationException)
                {
                    // In case the window was not shown modally, just close without setting DialogResult.
                }

                window.Close();
            }
        }

        private void SaveFeeds()
        {
            try
            {
                if (!ConfirmInsecureFeeds())
                {
                    return;
                }

                var normalizedFeeds = FeedService.NormalizeAndFilterFeeds(Feeds);

                if (normalizedFeeds.Count != Feeds.Count)
                {
                    Feeds.Clear();
                    foreach (var feed in normalizedFeeds)
                    {
                        feed.PropertyChanged -= Feed_PropertyChanged;
                        feed.PropertyChanged += Feed_PropertyChanged;
                        Feeds.Add(feed);
                    }
                }
                else
                {
                    for (int i = 0; i < Feeds.Count; i++)
                    {
                        var normalizedFeed = normalizedFeeds[i];
                        var existingFeed = Feeds[i];
                        if (!string.Equals(existingFeed.Url, normalizedFeed.Url, StringComparison.OrdinalIgnoreCase))
                        {
                            existingFeed.PropertyChanged -= Feed_PropertyChanged;
                            existingFeed.Url = normalizedFeed.Url;
                            existingFeed.PropertyChanged += Feed_PropertyChanged;
                        }
                    }
                }

                _settingsService.SaveFeeds(normalizedFeeds);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving feeds: {ex.Message}", "Error");
            }
        }

        private bool ConfirmInsecureFeeds()
        {
            var insecureFeeds = Feeds
                .Where(f =>
                {
                    if (string.IsNullOrWhiteSpace(f?.Url))
                    {
                        return false;
                    }
                    if (!Uri.TryCreate(f.Url.Trim(), UriKind.Absolute, out var uri))
                    {
                        return false;
                    }
                    return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
                })
                .ToList();

            if (!insecureFeeds.Any())
            {
                return true;
            }

            var feedList = string.Join("\n", insecureFeeds.Select(f => $"- {f.Name} ({f.Url})"));
            var message = "HTTP feeds are blocked. Please use HTTPS URLs.\n\n" +
                          $"{feedList}";

            MessageBox.Show(
                message,
                "HTTP feeds blocked",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            return false;
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void ReorderFeed(Feed draggedFeed, Feed targetFeed)
        {
            try
            {
                if (draggedFeed == null || targetFeed == null)
                {
                    return;
                }

                if (_groupFeedsByCategory &&
                    !string.Equals(NormalizeCategoryName(draggedFeed.Category),
                                   NormalizeCategoryName(targetFeed.Category),
                                   StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var draggedIndex = Feeds.IndexOf(draggedFeed);
                var targetIndex = Feeds.IndexOf(targetFeed);

                if (draggedIndex >= 0 && targetIndex >= 0 && draggedIndex != targetIndex)
                {
                    Feeds.RemoveAt(draggedIndex);

                    var insertionIndex = draggedIndex < targetIndex ? targetIndex - 1 : targetIndex;
                    if (insertionIndex < 0)
                    {
                        insertionIndex = 0;
                    }
                    if (insertionIndex > Feeds.Count)
                    {
                        insertionIndex = Feeds.Count;
                    }

                    Feeds.Insert(insertionIndex, draggedFeed);

                    SelectedFeed = draggedFeed;

                    MarkDirty();
                }
            }
            catch (Exception)
            {
                // Ignore reorder failures; UI remains unchanged.
            }
        }

        public void MoveFeed(FeedMoveRequest request)
        {
            try
            {
                if (request == null || request.Feed == null || Feeds == null || Feeds.Count == 0)
                {
                    return;
                }

                var currentIndex = Feeds.IndexOf(request.Feed);
                if (currentIndex < 0)
                {
                    return;
                }

                var targetIndex = request.Direction == FeedMoveDirection.Up
                    ? currentIndex - 1
                    : currentIndex + 1;

                if (targetIndex < 0 || targetIndex >= Feeds.Count)
                {
                    return;
                }

                Feeds.Move(currentIndex, targetIndex);
                SelectedFeed = request.Feed;
                MarkDirty();
            }
            catch (Exception)
            {
                // Ignore move failures; keep UI stable.
            }
        }

        private void MarkDirty()
        {
            IsDirty = true;
        }

        private async Task TryAutoFillFeedNameAsync(Feed feed)
        {
            try
            {
                if (feed == null)
                {
                    return;
                }

                var currentName = feed.Name?.Trim();
                if (!string.IsNullOrWhiteSpace(currentName) &&
                    !string.Equals(currentName, "New Feed", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(currentName, feed.Url, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var detectedTitle = await _feedService.TryDetectFeedTitleAsync(feed.Url);
                if (!string.IsNullOrWhiteSpace(detectedTitle))
                {
                    feed.Name = detectedTitle;
                }
            }
            catch
            {
                // Ignore detection errors to keep UI responsive.
            }
        }
    }
}