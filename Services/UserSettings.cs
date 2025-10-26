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
        private const string TitlePrefix = "title::";
        private const string BookmarkTitlePrefix = "bookmark::title::";
        public event EventHandler? SettingsChanged;
        public event EventHandler? ReadingProgressChanged;
        public event EventHandler? BookmarksChanged;

        public UserSettings()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var folder = Path.Combine(appData, "MSCS");
            _settingsPath = Path.Combine(folder, "settings.json");
            _data = LoadInternal();

            if (!Enum.IsDefined(typeof(AppTheme), _data.AppTheme))
            {
                _data.AppTheme = AppTheme.Dark;
                SaveInternal();
            }
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

        public string? MyAnimeListAccessToken
        {
            get => _data.MyAnimeListAccessToken;
            set
            {
                if (string.Equals(_data.MyAnimeListAccessToken, value, StringComparison.Ordinal))
                {
                    return;
                }

                _data.MyAnimeListAccessToken = value;
                SaveInternal();
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public string? MyAnimeListRefreshToken
        {
            get => _data.MyAnimeListRefreshToken;
            set
            {
                if (string.Equals(_data.MyAnimeListRefreshToken, value, StringComparison.Ordinal))
                {
                    return;
                }

                _data.MyAnimeListRefreshToken = value;
                SaveInternal();
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public DateTimeOffset? MyAnimeListAccessTokenExpiry
        {
            get => _data.MyAnimeListAccessTokenExpiry;
            set
            {
                if (_data.MyAnimeListAccessTokenExpiry == value)
                {
                    return;
                }

                _data.MyAnimeListAccessTokenExpiry = value;
                SaveInternal();
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public string? MyAnimeListUserName
        {
            get => _data.MyAnimeListUserName;
            set
            {
                if (string.Equals(_data.MyAnimeListUserName, value, StringComparison.Ordinal))
                {
                    return;
                }

                _data.MyAnimeListUserName = value;
                SaveInternal();
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool IsTrackingProviderConnected(string serviceId)
        {
            if (string.IsNullOrWhiteSpace(serviceId))
            {
                return false;
            }

            if (_data.TrackingProviderConnections != null &&
                _data.TrackingProviderConnections.TryGetValue(serviceId, out var connected))
            {
                return connected;
            }

            return false;
        }

        public void SetTrackingProviderConnection(string serviceId, bool isConnected)
        {
            if (string.IsNullOrWhiteSpace(serviceId))
            {
                return;
            }

            _data.TrackingProviderConnections ??= new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            if (_data.TrackingProviderConnections.TryGetValue(serviceId, out var existing) && existing == isConnected)
            {
                return;
            }

            _data.TrackingProviderConnections[serviceId] = isConnected;
            SaveInternal();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
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


        public bool TryGetExternalTracking(string serviceId, string mangaTitle, out MediaTrackingEntry? entry)
        {
            entry = null;
            if (string.IsNullOrWhiteSpace(serviceId) || string.IsNullOrWhiteSpace(mangaTitle))
            {
                return false;
            }

            if (_data.ExternalTrackedSeries != null &&
                _data.ExternalTrackedSeries.TryGetValue(serviceId, out var byTitle) &&
                byTitle != null &&
                byTitle.TryGetValue(mangaTitle, out var stored) && stored != null)
            {
                entry = ConvertToEntry(stored, mangaTitle);
                return true;
            }

            return false;
        }

        public IReadOnlyDictionary<string, MediaTrackingEntry> GetExternalTrackingSnapshot(string serviceId)
        {
            if (string.IsNullOrWhiteSpace(serviceId) || _data.ExternalTrackedSeries == null)
            {
                return new Dictionary<string, MediaTrackingEntry>(StringComparer.OrdinalIgnoreCase);
            }

            if (!_data.ExternalTrackedSeries.TryGetValue(serviceId, out var byTitle) || byTitle == null)
            {
                return new Dictionary<string, MediaTrackingEntry>(StringComparer.OrdinalIgnoreCase);
            }

            return byTitle
                .Where(kvp => kvp.Value != null)
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => ConvertToEntry(kvp.Value!, kvp.Key),
                    StringComparer.OrdinalIgnoreCase);
        }

        public void SetExternalTracking(string serviceId, string mangaTitle, MediaTrackingEntry entry)
        {
            if (string.IsNullOrWhiteSpace(serviceId) || string.IsNullOrWhiteSpace(mangaTitle) || entry == null)
            {
                return;
            }

            if (_data.ExternalTrackedSeries == null)
            {
                _data.ExternalTrackedSeries = new Dictionary<string, Dictionary<string, ExternalTrackedSeriesData>>(StringComparer.OrdinalIgnoreCase);
            }

            if (!_data.ExternalTrackedSeries.TryGetValue(serviceId, out var byTitle) || byTitle == null)
            {
                byTitle = new Dictionary<string, ExternalTrackedSeriesData>(StringComparer.OrdinalIgnoreCase);
                _data.ExternalTrackedSeries[serviceId] = byTitle;
            }

            byTitle[mangaTitle] = ConvertFromEntry(entry);
            SaveInternal();
        }

        public bool RemoveExternalTracking(string serviceId, string mangaTitle)
        {
            if (string.IsNullOrWhiteSpace(serviceId) || string.IsNullOrWhiteSpace(mangaTitle) ||
                _data.ExternalTrackedSeries == null ||
                !_data.ExternalTrackedSeries.TryGetValue(serviceId, out var byTitle) ||
                byTitle == null)
            {
                return false;
            }

            var removed = byTitle.Remove(mangaTitle);
            if (removed)
            {
                SaveInternal();
            }

            return removed;
        }

        public void ClearExternalTracking(string serviceId)
        {
            if (string.IsNullOrWhiteSpace(serviceId) || _data.ExternalTrackedSeries == null)
            {
                return;
            }

            if (_data.ExternalTrackedSeries.TryGetValue(serviceId, out var byTitle) && byTitle != null && byTitle.Count > 0)
            {
                byTitle.Clear();
                SaveInternal();
            }
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

        public void ClearMyAnimeListAuthentication()
        {
            var changed = false;

            if (!string.IsNullOrEmpty(_data.MyAnimeListAccessToken) ||
                !string.IsNullOrEmpty(_data.MyAnimeListRefreshToken) ||
                _data.MyAnimeListAccessTokenExpiry.HasValue ||
                !string.IsNullOrEmpty(_data.MyAnimeListUserName))
            {
                _data.MyAnimeListAccessToken = null;
                _data.MyAnimeListRefreshToken = null;
                _data.MyAnimeListAccessTokenExpiry = null;
                _data.MyAnimeListUserName = null;
                changed = true;
            }

            if (changed)
            {
                SaveInternal();
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }


        public IReadOnlyList<BookmarkEntry> GetAllBookmarks()
        {
            if (_data.Bookmarks == null || _data.Bookmarks.Count == 0)
            {
                return Array.Empty<BookmarkEntry>();
            }

            var results = new List<BookmarkEntry>(_data.Bookmarks.Count);
            foreach (var entry in _data.Bookmarks)
            {
                var stored = entry.Value ?? new BookmarkData();
                results.Add(ConvertToBookmark(entry.Key, stored));
            }

            return results;
        }

        public bool TryGetBookmark(BookmarkKey key, out BookmarkEntry? bookmark)
        {
            bookmark = null;
            if (key.IsEmpty)
            {
                return false;
            }

            if (_data.Bookmarks == null || _data.Bookmarks.Count == 0)
            {
                return false;
            }

            var storageKey = CreateBookmarkStorageKey(key);
            if (string.IsNullOrEmpty(storageKey))
            {
                return false;
            }

            if (_data.Bookmarks.TryGetValue(storageKey, out var stored) && stored != null)
            {
                bookmark = ConvertToBookmark(storageKey, stored);
                return true;
            }

            return false;
        }

        public bool HasBookmark(BookmarkKey key)
        {
            return TryGetBookmark(key, out _);
        }

        public BookmarkEntry? AddOrUpdateBookmark(BookmarkKey key, string title, string? coverImageUrl)
        {
            if (key.IsEmpty)
            {
                return null;
            }

            var storageKey = CreateBookmarkStorageKey(key);
            if (string.IsNullOrEmpty(storageKey))
            {
                return null;
            }

            _data.Bookmarks ??= new Dictionary<string, BookmarkData>(StringComparer.OrdinalIgnoreCase);

            if (!_data.Bookmarks.TryGetValue(storageKey, out var stored) || stored == null)
            {
                stored = new BookmarkData
                {
                    AddedUtc = DateTimeOffset.UtcNow
                };
            }

            var sanitizedTitle = string.IsNullOrWhiteSpace(title) ? key.Title : title.Trim();
            stored.Title = string.IsNullOrWhiteSpace(sanitizedTitle) ? null : sanitizedTitle;
            stored.SourceKey = string.IsNullOrWhiteSpace(key.SourceKey) ? null : key.SourceKey;
            stored.MangaUrl = string.IsNullOrWhiteSpace(key.MangaUrl) ? null : key.MangaUrl;
            stored.CoverImageUrl = string.IsNullOrWhiteSpace(coverImageUrl) ? null : coverImageUrl.Trim();
            if (stored.AddedUtc == default)
            {
                stored.AddedUtc = DateTimeOffset.UtcNow;
            }

            _data.Bookmarks[storageKey] = stored;
            SaveInternal();
            BookmarksChanged?.Invoke(this, EventArgs.Empty);
            return ConvertToBookmark(storageKey, stored);
        }

        public bool RemoveBookmark(BookmarkKey key)
        {
            if (key.IsEmpty)
            {
                return false;
            }

            var storageKey = CreateBookmarkStorageKey(key);
            if (string.IsNullOrEmpty(storageKey))
            {
                return false;
            }

            return RemoveBookmark(storageKey);
        }

        public bool RemoveBookmark(string? storageKey)
        {
            if (string.IsNullOrWhiteSpace(storageKey) || _data.Bookmarks == null)
            {
                return false;
            }

            if (_data.Bookmarks.Remove(storageKey))
            {
                SaveInternal();
                BookmarksChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }

            return false;
        }

        public bool TryGetReadingProgress(ReadingProgressKey key, out MangaReadingProgress? progress)
        {
            progress = null;
            if (key.IsEmpty)
            {
                return false;
            }

            if (_data.ReadingProgress == null || _data.ReadingProgress.Count == 0)
            {
                return false;
            }

            var storageKey = CreateStorageKey(key);
            if (!string.IsNullOrEmpty(storageKey) &&
                _data.ReadingProgress.TryGetValue(storageKey, out var stored))
            {
                progress = ConvertToProgress(stored);
                return true;
            }

            if (!string.IsNullOrEmpty(key.Title))
            {
                return TryGetReadingProgressByTitle(key.Title, out progress);
            }

            return false;
        }


        public void SetReadingProgress(ReadingProgressKey key, MangaReadingProgress progress)
        {
            if (key.IsEmpty || progress == null)
            {
                return;
            }

            var storageKey = CreateStorageKey(key);
            if (string.IsNullOrEmpty(storageKey))
            {
                return;
            }

            var normalizedProgress = progress.LegacyScrollProgress;
            if (!normalizedProgress.HasValue && progress.ScrollOffset.HasValue && progress.ScrollableHeight.HasValue && progress.ScrollableHeight.Value > 0)
            {
                var ratio = progress.ScrollOffset.Value / progress.ScrollableHeight.Value;
                normalizedProgress = Math.Clamp(ratio, 0.0, 1.0);
            }

            var sanitizedProgress = new ReadingProgressData
            {
                ChapterIndex = progress.ChapterIndex,
                ChapterTitle = progress.ChapterTitle,
                ScrollProgress = normalizedProgress.HasValue ? Math.Clamp(normalizedProgress.Value, 0.0, 1.0) : null,
                LastUpdatedUtc = progress.LastUpdatedUtc,
                MangaUrl = string.IsNullOrWhiteSpace(progress.MangaUrl) ? null : progress.MangaUrl.Trim(),
                SourceKey = string.IsNullOrWhiteSpace(progress.SourceKey) ? null : progress.SourceKey.Trim(),
                ScrollOffset = progress.ScrollOffset.HasValue
                    ? Math.Max(0, progress.ScrollOffset.Value)
                    : null,
                ScrollableHeight = progress.ScrollableHeight.HasValue
                    ? Math.Max(0, progress.ScrollableHeight.Value)
                    : null,
                AnchorImageUrl = string.IsNullOrWhiteSpace(progress.AnchorImageUrl) ? null : progress.AnchorImageUrl.Trim(),
                AnchorImageProgress = progress.AnchorImageProgress.HasValue
                    ? Math.Clamp(progress.AnchorImageProgress.Value, 0.0, 1.0)
                    : null,
                Title = string.IsNullOrEmpty(key.Title) ? null : key.Title,
            };

            _data.ReadingProgress[storageKey] = sanitizedProgress;

            SaveInternal();
            ReadingProgressChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ClearReadingProgress(ReadingProgressKey key)
        {
            if (key.IsEmpty)
            {
                return;
            }

            var storageKey = CreateStorageKey(key);
            if (!string.IsNullOrEmpty(storageKey) && _data.ReadingProgress.Remove(storageKey))
            {
                SaveInternal();
                ReadingProgressChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (!string.IsNullOrEmpty(key.Title) &&
                TryRemoveReadingProgressByTitle(key.Title))
            {
                SaveInternal();
                ReadingProgressChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void ClearReadingProgress(string storageKey)
        {
            if (string.IsNullOrWhiteSpace(storageKey))
            {
                return;
            }

            if (_data.ReadingProgress.Remove(storageKey))
            {
                SaveInternal();
                ReadingProgressChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public IReadOnlyList<ReadingProgressEntry> GetAllReadingProgress()
        {
            if (_data.ReadingProgress == null || _data.ReadingProgress.Count == 0)
            {
                return Array.Empty<ReadingProgressEntry>();
            }

            var results = new List<ReadingProgressEntry>(_data.ReadingProgress.Count);
            foreach (var entry in _data.ReadingProgress)
            {
                var stored = entry.Value;
                var record = ConvertToProgress(stored);
                var title = !string.IsNullOrWhiteSpace(stored.Title)
                    ? stored.Title!
                    : ExtractTitleFromKey(entry.Key);
                results.Add(new ReadingProgressEntry(entry.Key, title ?? string.Empty, record));
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
                ScrollDurationMs = data.ScrollDurationMs,
                UseTwoPageLayout = data.UseTwoPageLayout,
                EnablePageTransitions = data.EnablePageTransitions,
                AutoAdjustWidth = data.AutoAdjustWidth
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
                ScrollDurationMs = profile.ScrollDurationMs,
                UseTwoPageLayout = profile.UseTwoPageLayout,
                EnablePageTransitions = profile.EnablePageTransitions,
                AutoAdjustWidth = profile.AutoAdjustWidth
            };
        }


        private static MediaTrackingEntry ConvertToEntry(ExternalTrackedSeriesData data, string fallbackTitle)
        {
            return new MediaTrackingEntry(
                data.MediaId,
                string.IsNullOrWhiteSpace(data.Title) ? fallbackTitle : data.Title,
                data.CoverImageUrl,
                data.Status,
                data.Progress,
                data.Score,
                data.TotalChapters,
                data.SiteUrl,
                data.UpdatedAt);
        }

        private static ExternalTrackedSeriesData ConvertFromEntry(MediaTrackingEntry entry)
        {
            return new ExternalTrackedSeriesData
            {
                MediaId = entry.MediaId,
                Title = entry.Title,
                CoverImageUrl = entry.CoverImageUrl,
                Status = entry.Status,
                Progress = entry.Progress,
                Score = entry.Score,
                TotalChapters = entry.TotalChapters,
                SiteUrl = entry.SiteUrl,
                UpdatedAt = entry.UpdatedAt
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
                data.ExternalTrackedSeries ??= new Dictionary<string, Dictionary<string, ExternalTrackedSeriesData>>(StringComparer.OrdinalIgnoreCase);
                data.ReadingProgress ??= new Dictionary<string, ReadingProgressData>();
                data.Bookmarks ??= new Dictionary<string, BookmarkData>(StringComparer.OrdinalIgnoreCase);
                MigrateReadingProgressKeys(data);
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

        private BookmarkEntry ConvertToBookmark(string storageKey, BookmarkData stored)
        {
            if (stored == null)
            {
                stored = new BookmarkData();
            }

            var title = stored.Title;
            if (string.IsNullOrWhiteSpace(title))
            {
                title = ExtractTitleFromBookmarkKey(storageKey) ?? string.Empty;
            }

            var sourceKey = string.IsNullOrWhiteSpace(stored.SourceKey) ? null : stored.SourceKey.Trim();
            var mangaUrl = string.IsNullOrWhiteSpace(stored.MangaUrl) ? null : stored.MangaUrl.Trim();
            var coverImageUrl = string.IsNullOrWhiteSpace(stored.CoverImageUrl) ? null : stored.CoverImageUrl.Trim();
            var addedUtc = stored.AddedUtc == default ? DateTimeOffset.UtcNow : stored.AddedUtc;

            return new BookmarkEntry(storageKey, title ?? string.Empty, sourceKey, mangaUrl, coverImageUrl, addedUtc);
        }

        private static MangaReadingProgress ConvertToProgress(ReadingProgressData stored)
        {
            return new MangaReadingProgress(
                stored.ChapterIndex,
                stored.ChapterTitle,
                stored.ScrollProgress,
                stored.LastUpdatedUtc,
                stored.MangaUrl,
                stored.SourceKey,
                stored.ScrollOffset,
                stored.ScrollableHeight,
                stored.AnchorImageUrl,
                stored.AnchorImageProgress);
        }

        private static void MigrateReadingProgressKeys(SettingsData data)
        {
            if (data.ReadingProgress == null || data.ReadingProgress.Count == 0)
            {
                return;
            }

            var updated = new Dictionary<string, ReadingProgressData>();
            var changed = false;

            foreach (var entry in data.ReadingProgress)
            {
                var stored = entry.Value ?? new ReadingProgressData();
                var normalizedTitle = !string.IsNullOrWhiteSpace(stored.Title)
                    ? stored.Title!.Trim()
                    : entry.Key?.Trim();

                if (!string.IsNullOrWhiteSpace(normalizedTitle))
                {
                    stored.Title = normalizedTitle;
                }
                else
                {
                    stored.Title = null;
                }

                stored.SourceKey = string.IsNullOrWhiteSpace(stored.SourceKey) ? null : stored.SourceKey.Trim();
                stored.MangaUrl = string.IsNullOrWhiteSpace(stored.MangaUrl) ? null : stored.MangaUrl.Trim();

                var newKey = CreateStorageKey(new ReadingProgressKey(stored.Title, stored.SourceKey, stored.MangaUrl));
                if (string.IsNullOrEmpty(newKey))
                {
                    continue;
                }

                if (!string.Equals(newKey, entry.Key, StringComparison.Ordinal))
                {
                    changed = true;
                }

                if (updated.TryGetValue(newKey, out var existing))
                {
                    if (existing != null && existing.LastUpdatedUtc >= stored.LastUpdatedUtc)
                    {
                        continue;
                    }
                }

                updated[newKey] = stored;
            }

            if (changed || updated.Count != data.ReadingProgress.Count)
            {
                data.ReadingProgress = updated;
            }
        }

        private bool TryGetReadingProgressByTitle(string title, out MangaReadingProgress? progress)
        {
            progress = null;
            if (string.IsNullOrWhiteSpace(title) ||
                _data.ReadingProgress == null ||
                _data.ReadingProgress.Count == 0)
            {
                return false;
            }

            var normalizedTitle = title.Trim();
            var dictionary = _data.ReadingProgress;

            if (dictionary.TryGetValue(CreateTitleStorageKey(normalizedTitle), out var storedByTitle))
            {
                progress = ConvertToProgress(storedByTitle);
                return true;
            }

            var legacyKey = CreateLegacyKey(normalizedTitle);
            if (!string.IsNullOrEmpty(legacyKey) &&
                dictionary.TryGetValue(legacyKey, out var legacyStored))
            {
                progress = ConvertToProgress(legacyStored);
                return true;
            }

            return false;
        }

        private static string? ExtractTitleFromKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            if (key.StartsWith(TitlePrefix, StringComparison.Ordinal))
            {
                return key.Substring(TitlePrefix.Length);
            }

            return key;
        }


        private static string CreateBookmarkStorageKey(BookmarkKey key)
        {
            if (key.HasStableIdentifier)
            {
                return $"bookmark::source::{key.SourceKey}::{key.MangaUrl}";
            }

            if (!string.IsNullOrEmpty(key.Title))
            {
                return CreateBookmarkTitleStorageKey(key.Title);
            }

            return string.Empty;
        }

        private static string CreateBookmarkTitleStorageKey(string title)
        {
            return string.IsNullOrWhiteSpace(title)
                ? string.Empty
                : $"{BookmarkTitlePrefix}{title.Trim()}";
        }

        private static string? ExtractTitleFromBookmarkKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            if (key.StartsWith(BookmarkTitlePrefix, StringComparison.Ordinal))
            {
                return key.Substring(BookmarkTitlePrefix.Length);
            }

            return null;
        }

        private static string CreateStorageKey(ReadingProgressKey key)
        {
            if (key.HasStableIdentifier)
            {
                return $"source::{key.SourceKey}::{key.MangaUrl}";
            }

            if (!string.IsNullOrEmpty(key.Title))
            {
                return CreateTitleStorageKey(key.Title);
            }

            return string.Empty;
        }

        private static string CreateTitleStorageKey(string title)
        {
            return string.IsNullOrWhiteSpace(title)
                ? string.Empty
                : $"{TitlePrefix}{title.Trim()}";
        }

        private static string CreateLegacyKey(string title)
        {
            return string.IsNullOrWhiteSpace(title) ? string.Empty : title.Trim();
        }

        private bool TryRemoveReadingProgressByTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title) ||
                _data.ReadingProgress == null ||
                _data.ReadingProgress.Count == 0)
            {
                return false;
            }

            var normalizedTitle = title.Trim();
            if (string.IsNullOrEmpty(normalizedTitle))
            {
                return false;
            }

            var titleKey = CreateTitleStorageKey(normalizedTitle);
            if (!string.IsNullOrEmpty(titleKey) && _data.ReadingProgress.Remove(titleKey))
            {
                return true;
            }

            var legacyKey = CreateLegacyKey(normalizedTitle);
            if (!string.IsNullOrEmpty(legacyKey) && _data.ReadingProgress.Remove(legacyKey))
            {
                return true;
            }

            return false;
        }

        private class SettingsData
        {
            public string? LocalLibraryPath { get; set; }
            public string? AniListAccessToken { get; set; }
            public DateTimeOffset? AniListAccessTokenExpiry { get; set; }
            public string? AniListUserName { get; set; }
            public string? MyAnimeListAccessToken { get; set; }
            public string? MyAnimeListRefreshToken { get; set; }
            public DateTimeOffset? MyAnimeListAccessTokenExpiry { get; set; }
            public string? MyAnimeListUserName { get; set; }
            public AppTheme AppTheme { get; set; } = AppTheme.Dark;
            public long? LastSeenUpdateId { get; set; }
            public DateTimeOffset? LastSeenUpdateTimestamp { get; set; }
            public Dictionary<string, TrackedSeriesData> AniListTrackedSeries { get; set; } = new();
            public Dictionary<string, Dictionary<string, ExternalTrackedSeriesData>> ExternalTrackedSeries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, ReadingProgressData> ReadingProgress { get; set; } = new();
            public Dictionary<string, BookmarkData> Bookmarks { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public ReaderProfileData? DefaultReaderProfile { get; set; } = new();
            public Dictionary<string, ReaderProfileData> ReaderProfiles { get; set; } = new();
            public Dictionary<string, bool> TrackingProviderConnections { get; set; } = new(StringComparer.OrdinalIgnoreCase);
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

        private class ExternalTrackedSeriesData
        {
            public string? MediaId { get; set; }
            public string? Title { get; set; }
            public string? CoverImageUrl { get; set; }
            public string? Status { get; set; }
            public int? Progress { get; set; }
            public double? Score { get; set; }
            public int? TotalChapters { get; set; }
            public string? SiteUrl { get; set; }
            public DateTimeOffset? UpdatedAt { get; set; }
        }

        private class ReadingProgressData
        {
            public int ChapterIndex { get; set; }
            public string? ChapterTitle { get; set; }
            public double? ScrollProgress { get; set; }
            public DateTimeOffset LastUpdatedUtc { get; set; }
            public string? MangaUrl { get; set; }
            public string? SourceKey { get; set; }
            public string? CoverImageUrl { get; set; }
            public double? ScrollOffset { get; set; }
            public double? ScrollableHeight { get; set; }
            public string? AnchorImageUrl { get; set; }
            public double? AnchorImageProgress { get; set; }
            public string? Title { get; set; }
        }

        private class BookmarkData
        {
            public string? Title { get; set; }
            public string? SourceKey { get; set; }
            public string? MangaUrl { get; set; }
            public string? CoverImageUrl { get; set; }
            public DateTimeOffset AddedUtc { get; set; }
        }

        private class ReaderProfileData
        {
            public ReaderTheme Theme { get; set; } = ReaderTheme.Midnight;
            public double WidthFactor { get; set; } = Constants.DefaultWidthFactor;
            public double MaxPageWidth { get; set; } = Constants.DefaultMaxPageWidth;
            public double ScrollPageFraction { get; set; } = Constants.DefaultSmoothScrollPageFraction;
            public int ScrollDurationMs { get; set; } = Constants.DefaultSmoothScrollDuration;
            public bool UseTwoPageLayout { get; set; }
            public bool EnablePageTransitions { get; set; } = true;
            public bool AutoAdjustWidth { get; set; } = true;
        }
    }
}