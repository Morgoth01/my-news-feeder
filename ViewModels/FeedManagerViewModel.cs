using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
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
        private Feed _selectedFeed;
        private Category _selectedCategory;
        private string _newCategoryName;
        private AppSettings _settings;

        public ObservableCollection<Feed> Feeds { get; set; }
        public ObservableCollection<Category> Categories { get; set; }
        public ObservableCollection<string> CategoryNames { get; set; }

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

        // Commands
        public ICommand AddFeedCommand { get; }
        public ICommand RemoveFeedCommand { get; }
        public ICommand AddCategoryCommand { get; }
        public ICommand RemoveCategoryCommand { get; }
        public ICommand ImportCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand CloseCommand { get; }

        public FeedManagerViewModel(SettingsService settingsService)
        {
            _settingsService = settingsService;
            _settings = _settingsService.LoadSettings();

            // Initialize collections
            var feedList = _settingsService.LoadFeeds();
            Feeds = new ObservableCollection<Feed>(feedList);

            // Initialize categories
            Categories = new ObservableCollection<Category>();
            CategoryNames = new ObservableCollection<string>();

            LoadCategories();

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
        }

        private void LoadCategories()
        {
            Categories.Clear();
            CategoryNames.Clear();

            // Ensure Default category always exists
            if (!_settings.Categories.Contains("Default"))
            {
                _settings.Categories.Insert(0, "Default");
            }

            foreach (var categoryName in _settings.Categories)
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

        private void Feed_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Feed.IsEnabled) || e.PropertyName == nameof(Feed.Category))
            {
                // Auto-save when IsEnabled or Category changes
                SaveFeeds();
                System.Diagnostics.Debug.WriteLine($"Feed {e.PropertyName} changed, auto-saving...");
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
            SelectedFeed = newFeed;
            SaveFeeds();
        }

        private void RemoveFeed()
        {
            if (SelectedFeed == null) return;

            var feedToRemove = SelectedFeed;
            Feeds.Remove(feedToRemove);
            SelectedFeed = Feeds.FirstOrDefault();
            SaveFeeds();
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
            SaveCategories();
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
            SaveCategories();
            SaveFeeds();

            MessageBox.Show($"Category '{categoryName}' removed. {feedsToMove.Count} feeds moved to 'Default' category.",
                "Category Removed", MessageBoxButton.OK, MessageBoxImage.Information);
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

                        SaveFeeds();

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

                if (!string.IsNullOrEmpty(feed.Url))
                {
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
            return feeds ?? new List<Feed>();
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
                SaveFeeds();
                SaveCategories();
                window.Close();
            }
        }

        private void SaveFeeds()
        {
            try
            {
                var feedList = new List<Feed>(Feeds);
                _settingsService.SaveFeeds(feedList);
                System.Diagnostics.Debug.WriteLine($"Saved {feedList.Count} feeds to storage.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving feeds: {ex.Message}", "Error");
            }
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void ReorderFeed(Feed draggedFeed, Feed targetFeed)
        {
            try
            {
                var draggedIndex = Feeds.IndexOf(draggedFeed);
                var targetIndex = Feeds.IndexOf(targetFeed);

                if (draggedIndex >= 0 && targetIndex >= 0 && draggedIndex != targetIndex)
                {
                    // Remove from old position
                    Feeds.RemoveAt(draggedIndex);

                    // Insert at new position
                    if (draggedIndex < targetIndex)
                    {
                        // Moving down, adjust target index
                        Feeds.Insert(targetIndex - 1, draggedFeed);
                    }
                    else
                    {
                        // Moving up
                        Feeds.Insert(targetIndex, draggedFeed);
                    }

                    // Update selection
                    SelectedFeed = draggedFeed;

                    // Save changes
                    SaveFeeds();

                    System.Diagnostics.Debug.WriteLine($"Moved feed '{draggedFeed.Name}' from position {draggedIndex} to {Feeds.IndexOf(draggedFeed)}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reordering feeds: {ex.Message}");
            }
        }
    }
}
