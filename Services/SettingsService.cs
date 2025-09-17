using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using MyNewsFeeder.Models;

namespace MyNewsFeeder.Services
{
    public class SettingsService
    {
        private const string SettingsFileName = "settings.json";
        private const string FeedsFileName = "feeds.json";
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };
        private const int MaxWriteRetries = 3;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);

        public AppSettings LoadSettings()
        {
            if (File.Exists(SettingsFileName))
            {
                using var stream = new FileStream(SettingsFileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return JsonSerializer.Deserialize<AppSettings>(stream) ?? new AppSettings();
            }
            return new AppSettings();
        }

        public void SaveSettings(AppSettings settings)
        {
            SaveToFile(SettingsFileName, settings);
        }

        public List<Feed> LoadFeeds()
        {
            if (File.Exists(FeedsFileName))
            {
                using var stream = new FileStream(FeedsFileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return JsonSerializer.Deserialize<List<Feed>>(stream) ?? new List<Feed>();
            }
            return new List<Feed>();
        }

        public void SaveFeeds(List<Feed> feeds)
        {
            SaveToFile(FeedsFileName, feeds ?? new List<Feed>());
        }

        private static void SaveToFile<T>(string path, T value)
        {
            var tempPath = path + ".tmp";

            for (int attempt = 0; attempt < MaxWriteRetries; attempt++)
            {
                try
                {
                    using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        JsonSerializer.Serialize(stream, value, JsonOptions);
                    }

                    if (File.Exists(path))
                    {
                        try
                        {
                            File.Replace(tempPath, path, null, true);
                        }
                        catch (IOException)
                        {
                            File.Copy(tempPath, path, true);
                            File.Delete(tempPath);
                        }
                    }
                    else
                    {
#if NET7_0_OR_GREATER
                        File.Move(tempPath, path, true);
#else
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                        }
                        File.Move(tempPath, path);
#endif
                    }

                    return;
                }
                catch (IOException)
                {
                    if (attempt == MaxWriteRetries - 1)
                    {
                        throw;
                    }
                    Thread.Sleep(RetryDelay);
                }
            }
        }
    }
}