using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MyNewsFeeder.Models;

namespace MyNewsFeeder.Services
{
    public class SettingsService
    {
        private const string SettingsFileName = "settings.json";
        private const string FeedsFileName = "feeds.json";

        public AppSettings LoadSettings()
        {
            if (File.Exists(SettingsFileName))
            {
                var json = File.ReadAllText(SettingsFileName);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            return new AppSettings();
        }

        public void SaveSettings(AppSettings settings)
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFileName, json);
        }

        public List<Feed> LoadFeeds()
        {
            if (File.Exists(FeedsFileName))
            {
                var json = File.ReadAllText(FeedsFileName);
                return JsonSerializer.Deserialize<List<Feed>>(json) ?? new List<Feed>();
            }
            return new List<Feed>();
        }

        public void SaveFeeds(List<Feed> feeds)
        {
            try
            {
                var json = JsonSerializer.Serialize(feeds, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FeedsFileName, json);
                System.Diagnostics.Debug.WriteLine($"Successfully saved {feeds.Count} feeds to {FeedsFileName}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving feeds: {ex.Message}");
                throw;
            }
        }
    }
}