using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace MyNewsFeeder.Services
{
    public class UpdateCheckResult
    {
        public bool Success { get; set; }
        public bool UpdateAvailable { get; set; }
        public Version LatestVersion { get; set; }
        public string DownloadUrl { get; set; }
        public string CurrentVersion { get; set; }
        public string Error { get; set; }
    }

    public static class UpdateChecker
    {
        private static readonly HttpClient Client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        private static bool _interactivePromptShown;
        private static bool _interactiveInProgress;
        private static readonly object _lock = new object();

        // Keep repository in one place for About + auto checks.
        private const string LatestReleaseEndpoint = "https://api.github.com/repos/Morgoth01/My-News-Feeder/releases/latest";

        public static async Task<UpdateCheckResult> CheckAsync(Version currentVersion = null)
        {
            var effectiveCurrentVersion = currentVersion ?? Assembly.GetEntryAssembly()?.GetName()?.Version ?? new Version(1, 0, 0, 0);
            var result = new UpdateCheckResult
            {
                CurrentVersion = FormatVersion(effectiveCurrentVersion)
            };

            try
            {
                Client.DefaultRequestHeaders.UserAgent.ParseAdd($"MyNewsFeeder/{result.CurrentVersion}");
                var json = await Client.GetStringAsync(LatestReleaseEndpoint);
                using var doc = JsonDocument.Parse(json);
                var release = doc.RootElement;

                var tag = release.GetProperty("tag_name").GetString() ?? "";
                if (tag.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                    tag = tag.Substring(1);
                if (!Version.TryParse(tag, out var latestVersion))
                {
                    result.Error = $"Could not parse version: {tag}";
                    return result;
                }

                result.LatestVersion = latestVersion;
                var current = Version.Parse(result.CurrentVersion);
                result.UpdateAvailable = latestVersion > current;

                foreach (var asset in release.GetProperty("assets").EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
                    {
                        result.DownloadUrl = asset.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            return result;
        }

        public static async Task<bool> RunInteractiveCheckAsync(Window owner, bool showUpToDateMessage = true, bool showFailureMessage = true)
        {
            // prevent overlapping calls and double prompts (especially from silent auto-check)
            lock (_lock)
            {
                if (_interactiveInProgress)
                {
                    return false;
                }
                if (!showUpToDateMessage && !showFailureMessage && _interactivePromptShown)
                {
                    return false;
                }
                _interactiveInProgress = true;
            }

            var currentVersion = Assembly.GetEntryAssembly()?.GetName()?.Version ?? new Version(1, 0, 0, 0);
            var result = await CheckAsync(currentVersion);

            void ClearInProgress()
            {
                lock (_lock)
                {
                    _interactiveInProgress = false;
                }
            }

            if (!result.Success)
            {
                if (showFailureMessage)
                {
                    ShowMessage(
                        owner,
                        "Update check failed.\nVisit https://github.com/Morgoth01/My-News-Feeder/releases/latest",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                ClearInProgress();
                return false;
            }

            if (!result.UpdateAvailable)
            {
                if (showUpToDateMessage)
                {
                    ShowMessage(owner, $"You are running the latest version ({result.CurrentVersion}).",
                        "Up to Date", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                ClearInProgress();
                return false;
            }

            lock (_lock)
            {
                _interactivePromptShown = true;
            }

            if (ShowMessage(
                    owner,
                    $"A new version {FormatVersion(result.LatestVersion)} is available.\nYou have {result.CurrentVersion}.\n\nDownload now?",
                    "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Question)
                != MessageBoxResult.Yes)
            {
                ClearInProgress();
                return false;
            }

            if (string.IsNullOrWhiteSpace(result.DownloadUrl))
            {
                if (showFailureMessage)
                {
                    ShowMessage(owner, "No ZIP or 7z asset found in the latest release.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                ClearInProgress();
                return false;
            }

            var fileName = Path.GetFileName(new Uri(result.DownloadUrl).LocalPath);
            var savePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads", fileName);

            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(20);
                client.DefaultRequestHeaders.UserAgent.ParseAdd($"MyNewsFeeder/{result.CurrentVersion}");
                using var resp = await client.GetAsync(result.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                resp.EnsureSuccessStatusCode();
                await using var fs = File.Create(savePath);
                await resp.Content.CopyToAsync(fs);
            }
            catch (Exception ex)
            {
                if (showFailureMessage)
                {
                    ShowMessage(owner, $"Download failed: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                ClearInProgress();
                return false;
            }

            ShowMessage(
                owner,
                $"Downloaded to:\n{savePath}\n\nPlease unzip and replace the old files.\nVisit https://github.com/Morgoth01/My-News-Feeder/releases/latest for release notes.",
                "Download Complete", MessageBoxButton.OK, MessageBoxImage.Information);

            ClearInProgress();
            return true;
        }

        private static MessageBoxResult ShowMessage(
            Window owner,
            string message,
            string caption,
            MessageBoxButton buttons,
            MessageBoxImage image)
        {
            return owner != null
                ? MessageBox.Show(owner, message, caption, buttons, image)
                : MessageBox.Show(message, caption, buttons, image);
        }

        private static string FormatVersion(Version version)
        {
            if (version == null)
            {
                return "1.0.0";
            }

            if (version.Build >= 0)
            {
                return $"{version.Major}.{version.Minor}.{version.Build}";
            }

            return $"{version.Major}.{version.Minor}.0";
        }
    }
}
