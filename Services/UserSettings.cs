using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MSCS.Models;

namespace MSCS.Services
{
    public class UserSettings
    {
        private readonly string _settingsPath;
        private readonly object _syncLock = new();
        private SettingsData _data;

        public event EventHandler? SettingsChanged;
        public event EventHandler? ReadingProgressChanged;

        public UserSettings()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var folder = Path.Combine(appData, "MSCS");
            _settingsPath = Path.Combine(folder, "settings.json");
            _data = LoadInternal();
        }

        public string? LocalLibraryPath
        {
            get => _data.LocalLibraryPath;
            set
            {
                var sanitized = string.IsNullOrWhiteSpace(value) ? null : value?.Trim();
                if (string.Equals(_data.LocalLibraryPath, sanitized, StringComparison.Ordinal))
                {
                    return;
                }

                _data.LocalLibraryPath = sanitized;
                SaveInternal();
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public string? AniListAccessToken
        {
            get => _data.AniListAccessToken;
            set
            {
                if (string.Equals(_data.AniListAccessToken, value, StringComparison.Ordinal))
                {
                    return;
                }

                _data.AniListAccessToken = value;
                SaveInternal();
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public DateTimeOffset? AniListAccessTokenExpiry
        {
            get => _data.AniListAccessTokenExpiry;
            set
            {
                if (_data.AniListAccessTokenExpiry == value)
                {
                    return;
                }

                _data.AniListAccessTokenExpiry = value;
                SaveInternal();
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public string? AniListUserName
        {
            get => _data.AniListUserName;
            set
            {
                if (string.Equals(_data.AniListUserName, value, StringComparison.Ordinal))
                {
                    return;
                }

                _data.AniListUserName = value;
                SaveInternal();
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool TryGetAniListTracking(string mangaTitle, out AniListTrackingInfo? trackingInfo)
        {
            trackingInfo = null;
            if (string.IsNullOrWhiteSpace(mangaTitle))
            {
                return false;
            }

            if (_data.AniListTrackedSeries != null &&
                _data.AniListTrackedSeries.TryGetValue(mangaTitle, out var stored))
            {
                trackingInfo = new AniListTrackingInfo(stored.MediaId, stored.Title ?? mangaTitle, stored.CoverImageUrl);
                return true;
            }

            return false;
        }

        public void SetAniListTracking(string mangaTitle, AniListTrackingInfo info)
        {
            if (string.IsNullOrWhiteSpace(mangaTitle) || info == null)
            {
                return;
            }

            _data.AniListTrackedSeries[mangaTitle] = new TrackedSeriesData
            {
                MediaId = info.MediaId,
                Title = info.Title,
                CoverImageUrl = info.CoverImageUrl
            };

            SaveInternal();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void RemoveAniListTracking(string mangaTitle)
        {
            if (string.IsNullOrWhiteSpace(mangaTitle))
            {
                return;
            }

            if (_data.AniListTrackedSeries.Remove(mangaTitle))
            {
                SaveInternal();
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool TryGetReadingProgress(string mangaTitle, out MangaReadingProgress? progress)
        {
            progress = null;
            if (string.IsNullOrWhiteSpace(mangaTitle))
            {
                return false;
            }

            if (_data.ReadingProgress != null &&
                _data.ReadingProgress.TryGetValue(mangaTitle, out var stored))
            {
                progress = new MangaReadingProgress(
                    stored.ChapterIndex,
                    stored.ChapterTitle,
                    stored.ScrollProgress,
                    stored.LastUpdatedUtc,
                    stored.MangaUrl,
                    stored.SourceKey,
                    stored.CoverImageUrl);
                return true;
            }

            return false;
        }

        public void SetReadingProgress(string mangaTitle, MangaReadingProgress progress)
        {
            if (string.IsNullOrWhiteSpace(mangaTitle) || progress == null)
            {
                return;
            }

            _data.ReadingProgress[mangaTitle] = new ReadingProgressData
            {
                ChapterIndex = progress.ChapterIndex,
                ChapterTitle = progress.ChapterTitle,
                ScrollProgress = Math.Clamp(progress.ScrollProgress, 0.0, 1.0),
                LastUpdatedUtc = progress.LastUpdatedUtc,
                MangaUrl = string.IsNullOrWhiteSpace(progress.MangaUrl) ? null : progress.MangaUrl,
                SourceKey = string.IsNullOrWhiteSpace(progress.SourceKey) ? null : progress.SourceKey,
                CoverImageUrl = string.IsNullOrWhiteSpace(progress.CoverImageUrl) ? null : progress.CoverImageUrl
            };

            SaveInternal();
            ReadingProgressChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ClearReadingProgress(string mangaTitle)
        {
            if (string.IsNullOrWhiteSpace(mangaTitle))
            {
                return;
            }

            if (_data.ReadingProgress.Remove(mangaTitle))
            {
                SaveInternal();
                ReadingProgressChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public IReadOnlyList<KeyValuePair<string, MangaReadingProgress>> GetAllReadingProgress()
        {
            if (_data.ReadingProgress == null || _data.ReadingProgress.Count == 0)
            {
                return Array.Empty<KeyValuePair<string, MangaReadingProgress>>();
            }

            var results = new List<KeyValuePair<string, MangaReadingProgress>>(_data.ReadingProgress.Count);
            foreach (var entry in _data.ReadingProgress)
            {
                var stored = entry.Value;
                var record = new MangaReadingProgress(
                    stored.ChapterIndex,
                    stored.ChapterTitle,
                    stored.ScrollProgress,
                    stored.LastUpdatedUtc,
                    stored.MangaUrl,
                    stored.SourceKey,
                    stored.CoverImageUrl);
                results.Add(new KeyValuePair<string, MangaReadingProgress>(entry.Key, record));
            }

            return results;
        }

        private SettingsData LoadInternal()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                {
                    return new SettingsData();
                }

                var json = File.ReadAllText(_settingsPath);
                var data = JsonSerializer.Deserialize<SettingsData>(json) ?? new SettingsData();
                data.AniListTrackedSeries ??= new Dictionary<string, TrackedSeriesData>();
                data.ReadingProgress ??= new Dictionary<string, ReadingProgressData>();
                return data;
            }
            catch
            {
                return new SettingsData();
            }
        }

        private void SaveInternal()
        {
            try
            {
                var directory = Path.GetDirectoryName(_settingsPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });

                lock (_syncLock)
                {
                    File.WriteAllText(_settingsPath, json);
                }
            }
            catch
            {
                // Swallow IO issues – we do not want settings persistence failures to crash the app.
            }
        }

        private class SettingsData
        {
            public string? LocalLibraryPath { get; set; }
            public string? AniListAccessToken { get; set; }
            public DateTimeOffset? AniListAccessTokenExpiry { get; set; }
            public string? AniListUserName { get; set; }
            public Dictionary<string, TrackedSeriesData> AniListTrackedSeries { get; set; } = new();
            public Dictionary<string, ReadingProgressData> ReadingProgress { get; set; } = new();
        }

        private class TrackedSeriesData
        {
            public int MediaId { get; set; }
            public string? Title { get; set; }
            public string? CoverImageUrl { get; set; }
        }

        private class ReadingProgressData
        {
            public int ChapterIndex { get; set; }
            public string? ChapterTitle { get; set; }
            public double ScrollProgress { get; set; }
            public DateTimeOffset LastUpdatedUtc { get; set; }
            public string? MangaUrl { get; set; }
            public string? SourceKey { get; set; }
            public string? CoverImageUrl { get; set; }
        }
    }
}