using MSCS.Enums;
using MSCS.Helpers;
using MSCS.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MSCS.Services
{
    public class UserSettings
    {
        private readonly string _settingsPath;
        private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();
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

        public AppTheme AppTheme
        {
            get => _data.AppTheme;
            set
            {
                if (_data.AppTheme == value)
                {
                    return;
                }

                _data.AppTheme = value;
                SaveInternal();
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }


        public long? LastSeenUpdateId
        {
            get => _data.LastSeenUpdateId;
            set
            {
                if (_data.LastSeenUpdateId == value)
                {
                    return;
                }

                _data.LastSeenUpdateId = value;
                SaveInternal();
            }
        }

        public DateTimeOffset? LastSeenUpdateTimestamp
        {
            get => _data.LastSeenUpdateTimestamp;
            set
            {
                if (_data.LastSeenUpdateTimestamp == value)
                {
                    return;
                }

                _data.LastSeenUpdateTimestamp = value;
                SaveInternal();
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
                var status = AniListFormatting.FromApiValue(stored.Status);
                trackingInfo = new AniListTrackingInfo(
                    stored.MediaId,
                    stored.Title ?? mangaTitle,
                    stored.CoverImageUrl,
                    status,
                    stored.Progress,
                    stored.Score,
                    stored.TotalChapters,
                    stored.SiteUrl,
                    stored.UpdatedAt,
                    stored.MediaListEntryId);
                return true;
            }

            return false;
        }

        public ReaderProfile GetReaderProfile(string? mangaTitle)
        {
            var baseProfile = ConvertToProfile(_data.DefaultReaderProfile);
            if (string.IsNullOrWhiteSpace(mangaTitle))
            {
                return baseProfile;
            }

            if (_data.ReaderProfiles != null &&
                _data.ReaderProfiles.TryGetValue(mangaTitle, out var stored) && stored != null)
            {
                return ConvertToProfile(stored);
            }

            return baseProfile;
        }

        public ReaderProfile GetDefaultReaderProfile()
        {
            return ConvertToProfile(_data.DefaultReaderProfile);
        }

        public void SetReaderProfile(string? mangaTitle, ReaderProfile profile)
        {
            if (profile == null)
            {
                return;
            }

            var data = ConvertToData(profile);

            if (string.IsNullOrWhiteSpace(mangaTitle))
            {
                _data.DefaultReaderProfile = data;
            }
            else
            {
                _data.ReaderProfiles[mangaTitle] = data;
            }

            SaveInternal();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ClearReaderProfile(string mangaTitle)
        {
            if (string.IsNullOrWhiteSpace(mangaTitle) || _data.ReaderProfiles == null)
            {
                return;
            }

            if (_data.ReaderProfiles.Remove(mangaTitle))
            {
                SaveInternal();
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
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
                CoverImageUrl = info.CoverImageUrl,
                Status = info.Status?.ToApiValue(),
                Progress = info.Progress,
                Score = info.Score,
                TotalChapters = info.TotalChapters,
                SiteUrl = info.SiteUrl,
                UpdatedAt = info.UpdatedAt,
                MediaListEntryId = info.MediaListEntryId
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

        public void ClearAniListTracking()
        {
            if (_data.AniListTrackedSeries.Count == 0)
            {
                return;
            }

            _data.AniListTrackedSeries.Clear();
            SaveInternal();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ClearAniListAuthentication()
        {
            var changed = false;

            if (!string.IsNullOrEmpty(_data.AniListAccessToken) ||
                _data.AniListAccessTokenExpiry.HasValue ||
                !string.IsNullOrEmpty(_data.AniListUserName))
            {
                _data.AniListAccessToken = null;
                _data.AniListAccessTokenExpiry = null;
                _data.AniListUserName = null;
                changed = true;
            }

            if (changed)
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
                    stored.ScrollOffset);
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
                ScrollOffset = progress.ScrollOffset.HasValue
                    ? Math.Max(0, progress.ScrollOffset.Value)
                    : null,
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
                    stored.ScrollOffset);
                results.Add(new KeyValuePair<string, MangaReadingProgress>(entry.Key, record));
            }

            return results;
        }


        private static ReaderProfile ConvertToProfile(ReaderProfileData? data)
        {
            if (data == null)
            {
                return ReaderProfile.CreateDefault();
            }

            return new ReaderProfile
            {
                Theme = data.Theme,
                WidthFactor = data.WidthFactor,
                MaxPageWidth = data.MaxPageWidth,
                ScrollPageFraction = data.ScrollPageFraction,
                ScrollDurationMs = data.ScrollDurationMs
            };
        }

        private static ReaderProfileData ConvertToData(ReaderProfile profile)
        {
            return new ReaderProfileData
            {
                Theme = profile.Theme,
                WidthFactor = profile.WidthFactor,
                MaxPageWidth = profile.MaxPageWidth,
                ScrollPageFraction = profile.ScrollPageFraction,
                ScrollDurationMs = profile.ScrollDurationMs
            };
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
                var data = JsonSerializer.Deserialize<SettingsData>(json, SerializerOptions) ?? new SettingsData();
                data.AniListTrackedSeries ??= new Dictionary<string, TrackedSeriesData>();
                data.ReadingProgress ??= new Dictionary<string, ReadingProgressData>();
                data.ReaderProfiles ??= new Dictionary<string, ReaderProfileData>();
                data.DefaultReaderProfile ??= new ReaderProfileData();
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

                var json = JsonSerializer.Serialize(_data, SerializerOptions);

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

        private static JsonSerializerOptions CreateSerializerOptions()
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }

        private class SettingsData
        {
            public string? LocalLibraryPath { get; set; }
            public string? AniListAccessToken { get; set; }
            public DateTimeOffset? AniListAccessTokenExpiry { get; set; }
            public string? AniListUserName { get; set; }
            public AppTheme AppTheme { get; set; } = AppTheme.Dark;
            public long? LastSeenUpdateId { get; set; }
            public DateTimeOffset? LastSeenUpdateTimestamp { get; set; }
            public Dictionary<string, TrackedSeriesData> AniListTrackedSeries { get; set; } = new();
            public Dictionary<string, ReadingProgressData> ReadingProgress { get; set; } = new();
            public ReaderProfileData? DefaultReaderProfile { get; set; } = new();
            public Dictionary<string, ReaderProfileData> ReaderProfiles { get; set; } = new();
        }

        private class TrackedSeriesData
        {
            public int MediaId { get; set; }
            public string? Title { get; set; }
            public string? CoverImageUrl { get; set; }
            public string? Status { get; set; }
            public int? Progress { get; set; }
            public double? Score { get; set; }
            public int? TotalChapters { get; set; }
            public string? SiteUrl { get; set; }
            public DateTimeOffset? UpdatedAt { get; set; }
            public int? MediaListEntryId { get; set; }
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
            public double? ScrollOffset { get; internal set; }
        }

        private class ReaderProfileData
        {
            public ReaderTheme Theme { get; set; } = ReaderTheme.Midnight;
            public double WidthFactor { get; set; } = Constants.DefaultWidthFactor;
            public double MaxPageWidth { get; set; } = Constants.DefaultMaxPageWidth;
            public double ScrollPageFraction { get; set; } = Constants.DefaultSmoothScrollPageFraction;
            public int ScrollDurationMs { get; set; } = Constants.DefaultSmoothScrollDuration;
        }
    }
}