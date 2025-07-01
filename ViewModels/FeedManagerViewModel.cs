using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Input;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;
using MyNewsFeeder.Models;
using MyNewsFeeder.Services;

namespace MyNewsFeeder.ViewModels
{
    public class FeedManagerViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private readonly SettingsService _settingsService;
        private Feed _selectedFeed;

        public ObservableCollection<Feed> Feeds { get; set; }

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

        // Commands
        public ICommand AddFeedCommand { get; }
        public ICommand RemoveFeedCommand { get; }
        public ICommand ImportCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand CloseCommand { get; }

        public FeedManagerViewModel(SettingsService settingsService)
        {
            _settingsService = settingsService;

            var feedList = _settingsService.LoadFeeds();
            Feeds = new ObservableCollection<Feed>(feedList);

            // Subscribe to PropertyChanged events for auto-save
            foreach (var feed in Feeds)
            {
                feed.PropertyChanged += Feed_PropertyChanged;
            }

            // Initialize commands
            AddFeedCommand = new RelayCommand(_ => AddFeed());
            RemoveFeedCommand = new RelayCommand(_ => RemoveFeed(), _ => CanRemove());
            ImportCommand = new RelayCommand(_ => ImportFeeds());
            ExportCommand = new RelayCommand(_ => ExportFeeds());
            CloseCommand = new RelayCommand(param => CloseWindow(param));
        }

        private void Feed_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Feed.IsEnabled))
            {
                // Auto-save when IsEnabled changes
                SaveFeeds();
                System.Diagnostics.Debug.WriteLine("Feed IsEnabled changed, auto-saving...");
            }
        }

        private bool CanRemove()
        {
            return SelectedFeed != null;
        }

        private void AddFeed()
        {
            var newFeed = new Feed
            {
                Name = "New Feed",
                Url = "https://example.com/rss",
                IsEnabled = true
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
                            // Check for duplicates by URL
                            if (!Feeds.Any(f => f.Url.Equals(feed.Url, StringComparison.OrdinalIgnoreCase)))
                            {
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
                        System.Windows.MessageBox.Show(message, "Import Successful",
                            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("No feeds found in the selected file.", "Import Warning",
                            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error importing feeds: {ex.Message}", "Import Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void ExportFeeds()
        {
            try
            {
                if (Feeds.Count == 0)
                {
                    System.Windows.MessageBox.Show("No feeds to export.", "Export Warning",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
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

                    System.Windows.MessageBox.Show($"Successfully exported {Feeds.Count} feeds to:\n{filePath}",
                        "Export Successful", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error exporting feeds: {ex.Message}", "Export Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        // OPML Import
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
                    IsEnabled = true
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

        // OPML Export
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
            if (parameter is System.Windows.Window window)
            {
                SaveFeeds();
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
                System.Windows.MessageBox.Show($"Error saving feeds: {ex.Message}", "Error");
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
