using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MyNewsFeeder.Models;

namespace MyNewsFeeder.Services
{
    public sealed class HelpContentService
    {
        private const string IndexFileName = "help-index.json";
        private readonly string _helpRoot;

        public HelpContentService(string baseDirectory = null)
        {
            var root = string.IsNullOrWhiteSpace(baseDirectory) ? AppContext.BaseDirectory : baseDirectory;
            _helpRoot = Path.Combine(root, "Help");
        }

        public IReadOnlyList<HelpTopic> LoadTopics()
        {
            try
            {
                var indexPath = Path.Combine(_helpRoot, IndexFileName);
                if (!File.Exists(indexPath))
                {
                    return Array.Empty<HelpTopic>();
                }

                var json = File.ReadAllText(indexPath);
                var parsed = JsonSerializer.Deserialize<HelpIndexDocument>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (parsed?.Topics == null || parsed.Topics.Count == 0)
                {
                    return Array.Empty<HelpTopic>();
                }

                return parsed.Topics
                    .Where(topic => topic != null &&
                                    !string.IsNullOrWhiteSpace(topic.Id) &&
                                    !string.IsNullOrWhiteSpace(topic.Title) &&
                                    !string.IsNullOrWhiteSpace(topic.File))
                    .Select(topic => new HelpTopic
                    {
                        Id = topic.Id.Trim(),
                        Title = topic.Title.Trim(),
                        File = topic.File.Trim()
                    })
                    .ToList();
            }
            catch
            {
                return Array.Empty<HelpTopic>();
            }
        }

        public string LoadTopicMarkdown(HelpTopic topic)
        {
            if (topic == null || string.IsNullOrWhiteSpace(topic.File))
            {
                return string.Empty;
            }

            try
            {
                var filePath = Path.Combine(_helpRoot, topic.File);
                if (!File.Exists(filePath))
                {
                    return string.Empty;
                }

                return File.ReadAllText(filePath);
            }
            catch
            {
                return string.Empty;
            }
        }

        private sealed class HelpIndexDocument
        {
            public List<HelpIndexEntry> Topics { get; set; } = new List<HelpIndexEntry>();
        }

        private sealed class HelpIndexEntry
        {
            public string Id { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string File { get; set; } = string.Empty;
        }
    }
}