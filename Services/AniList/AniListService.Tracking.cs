using MSCS.Enums;
using MSCS.Helpers;
using MSCS.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MSCS.Services
{
    public partial class AniListService
    {
        public async Task<AniListTrackingInfo> TrackSeriesAsync(
            string mangaTitle,
            AniListMedia media,
            AniListMediaListStatus? status = null,
            int? progress = null,
            double? score = null,
            CancellationToken cancellationToken = default)
        {
            if (media == null) throw new ArgumentNullException(nameof(media));
            EnsureAuthenticated();

            var desiredStatus = status ?? AniListMediaListStatus.Current;
            var info = await SaveMediaListEntryAsync(
                mangaTitle,
                media.Id,
                media.DisplayTitle,
                media.CoverImageUrl,
                desiredStatus,
                progress,
                score,
                cancellationToken).ConfigureAwait(false);

            if (info != null)
            {
                return info;
            }

            var fallback = new AniListTrackingInfo(
                media.Id,
                media.DisplayTitle,
                media.CoverImageUrl,
                desiredStatus,
                progress,
                score,
                media.Chapters,
                media.SiteUrl,
                DateTimeOffset.UtcNow,
                null);
            _userSettings.SetAniListTracking(mangaTitle, fallback);
            RaiseTrackingChanged(new AniListTrackingChangedEventArgs(mangaTitle, fallback.MediaId, fallback));
            return fallback;
        }
        public async Task UpdateProgressAsync(string mangaTitle, int progress, CancellationToken cancellationToken = default)
        {
            if (progress <= 0)
            {
                return;
            }

            if (!_userSettings.TryGetAniListTracking(mangaTitle, out var trackingInfo) || trackingInfo == null)
            {
                return;
            }

            var updated = await SaveMediaListEntryAsync(
                mangaTitle,
                trackingInfo.MediaId,
                trackingInfo.Title,
                trackingInfo.CoverImageUrl,
                null,
                progress,
                null,
                cancellationToken).ConfigureAwait(false);

            if (updated == null)
            {
                var fallback = new AniListTrackingInfo(
                    trackingInfo.MediaId,
                    trackingInfo.Title,
                    trackingInfo.CoverImageUrl,
                    trackingInfo.Status,
                    progress,
                    trackingInfo.Score,
                    trackingInfo.TotalChapters,
                    trackingInfo.SiteUrl,
                    DateTimeOffset.UtcNow,
                    trackingInfo.MediaListEntryId);
                _userSettings.SetAniListTracking(mangaTitle, fallback);
                RaiseTrackingChanged(new AniListTrackingChangedEventArgs(mangaTitle, fallback.MediaId, fallback));
            }
        }
        public bool TryGetTracking(string mangaTitle, out AniListTrackingInfo? trackingInfo)
        {
            return _userSettings.TryGetAniListTracking(mangaTitle, out trackingInfo);
        }
        public async Task<AniListTrackingInfo?> UpdateTrackingAsync(
            string mangaTitle,
            AniListMediaListStatus? status = null,
            int? progress = null,
            double? score = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(mangaTitle))
            {
                return null;
            }

            if (!_userSettings.TryGetAniListTracking(mangaTitle, out var trackingInfo) || trackingInfo == null)
            {
                return null;
            }


            var updated = await SaveMediaListEntryAsync(
                mangaTitle,
                trackingInfo.MediaId,
                trackingInfo.Title,
                trackingInfo.CoverImageUrl,
                status,
                progress,
                score,
                cancellationToken).ConfigureAwait(false);

            if (updated != null)
            {
                return updated;
            }

            var fallback = new AniListTrackingInfo(
                trackingInfo.MediaId,
                trackingInfo.Title,
                trackingInfo.CoverImageUrl,
                status ?? trackingInfo.Status,
                progress ?? trackingInfo.Progress,
                score ?? trackingInfo.Score,
                trackingInfo.TotalChapters,
                trackingInfo.SiteUrl,
                DateTimeOffset.UtcNow,
                trackingInfo.MediaListEntryId);
            _userSettings.SetAniListTracking(mangaTitle, fallback);
            RaiseTrackingChanged(new AniListTrackingChangedEventArgs(mangaTitle, fallback.MediaId, fallback));
            return fallback;
        }
        public async Task<bool> UntrackSeriesAsync(string mangaTitle, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(mangaTitle))
            {
                return false;
            }

            if (!_userSettings.TryGetAniListTracking(mangaTitle, out var trackingInfo) || trackingInfo == null)
            {
                return false;
            }

            EnsureAuthenticated();
            var entryId = trackingInfo.MediaListEntryId;
            if (entryId == null)
            {
                var refreshed = await RefreshTrackingAsync(mangaTitle, cancellationToken).ConfigureAwait(false);
                entryId = refreshed?.MediaListEntryId;
                if (entryId == null)
                {
                    _userSettings.RemoveAniListTracking(mangaTitle);
                    RaiseTrackingChanged(new AniListTrackingChangedEventArgs(mangaTitle, trackingInfo.MediaId, null));
                    return true;
                }
            }

            var variables = new
            {
                id = entryId
            };

            using var document = await TrySendGraphQlRequestAsync(DeleteMediaListEntryMutation, variables, cancellationToken).ConfigureAwait(false);
            if (document == null)
            {
                Debug.WriteLine("AniList: Unable to remove tracking entry due to missing connection. Removing local entry only.");
            }
            _userSettings.RemoveAniListTracking(mangaTitle);
            RaiseTrackingChanged(new AniListTrackingChangedEventArgs(mangaTitle, trackingInfo.MediaId, null));
            return true;
        }
        public async Task<AniListTrackingInfo?> RefreshTrackingAsync(string mangaTitle, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(mangaTitle))
            {
                return null;
            }

            if (!_userSettings.TryGetAniListTracking(mangaTitle, out var trackingInfo) || trackingInfo == null)
            {
                return null;
            }

            EnsureAuthenticated();
            var refreshed = await FetchTrackingInfoByMediaIdAsync(
                trackingInfo.MediaId,
                trackingInfo.Title,
                trackingInfo.CoverImageUrl,
                cancellationToken).ConfigureAwait(false);

            if (refreshed != null)
            {
                _userSettings.SetAniListTracking(mangaTitle, refreshed);
                RaiseTrackingChanged(new AniListTrackingChangedEventArgs(mangaTitle, refreshed.MediaId, refreshed));
            }

            return refreshed;
        }
        private async Task<AniListTrackingInfo?> SaveMediaListEntryAsync(
            string mangaTitle,
            int mediaId,
            string fallbackTitle,
            string? fallbackCoverImage,
            AniListMediaListStatus? status,
            int? progress,
            double? score,
            CancellationToken cancellationToken)
        {
            EnsureAuthenticated();

            var variables = new Dictionary<string, object?>
            {
                ["mediaId"] = mediaId
            };

            if (status.HasValue)
            {
                variables["status"] = status.Value.ToApiValue();
            }

            if (progress.HasValue)
            {
                variables["progress"] = progress.Value;
            }

            if (score.HasValue)
            {
                variables["score"] = score.Value;
            }

            using var document = await TrySendGraphQlRequestAsync(SaveMediaListEntryMutation, variables, cancellationToken).ConfigureAwait(false);
            if (document == null)
            {
                return null;
            }

            var parsed = ParseSavedEntryTrackingInfo(document, fallbackTitle, fallbackCoverImage);
            if (parsed != null)
            {
                _userSettings.SetAniListTracking(mangaTitle, parsed);
                RaiseTrackingChanged(new AniListTrackingChangedEventArgs(mangaTitle, parsed.MediaId, parsed));
                return parsed;
            }

            var refreshed = await FetchTrackingInfoByMediaIdAsync(mediaId, fallbackTitle, fallbackCoverImage, cancellationToken).ConfigureAwait(false);
            if (refreshed != null)
            {
                _userSettings.SetAniListTracking(mangaTitle, refreshed);
                RaiseTrackingChanged(new AniListTrackingChangedEventArgs(mangaTitle, refreshed.MediaId, refreshed));
            }

            return refreshed;
        }
        private AniListTrackingInfo? ParseSavedEntryTrackingInfo(JsonDocument document, string fallbackTitle, string? fallbackCoverImage)
        {
            if (!document.RootElement.TryGetProperty("data", out var dataElement) ||
                !dataElement.TryGetProperty("SaveMediaListEntry", out var entryElement) ||
                entryElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!entryElement.TryGetProperty("media", out var mediaElement) ||
                mediaElement.ValueKind != JsonValueKind.Object ||
                !mediaElement.TryGetProperty("id", out var mediaIdElement) ||
                mediaIdElement.ValueKind != JsonValueKind.Number)
            {
                return null;
            }

            var mediaId = mediaIdElement.GetInt32();
            if (mediaId <= 0)
            {
                return null;
            }

            var titleElement = mediaElement.TryGetProperty("title", out var titleJson) ? titleJson : default;
            var title = ResolveTitle(titleElement, fallbackTitle);
            var cover = mediaElement.TryGetProperty("coverImage", out var coverElement) &&
                        coverElement.TryGetProperty("large", out var coverUrl)
                ? coverUrl.GetString()
                : fallbackCoverImage;
            var siteUrl = mediaElement.TryGetProperty("siteUrl", out var siteUrlElement) ? siteUrlElement.GetString() : null;
            var chapters = mediaElement.TryGetProperty("chapters", out var chaptersElement) && chaptersElement.ValueKind == JsonValueKind.Number
                ? chaptersElement.GetInt32()
                : (int?)null;

            var status = AniListFormatting.FromApiValue(entryElement.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null);

            int? progress = null;
            if (entryElement.TryGetProperty("progress", out var progressElement) && progressElement.ValueKind == JsonValueKind.Number)
            {
                var progressValue = progressElement.GetInt32();
                if (progressValue > 0)
                {
                    progress = progressValue;
                }
            }

            double? score = null;
            if (entryElement.TryGetProperty("score", out var scoreElement) && scoreElement.ValueKind == JsonValueKind.Number)
            {
                var scoreValue = scoreElement.GetDouble();
                if (scoreValue > 0)
                {
                    score = scoreValue;
                }
            }

            DateTimeOffset? updatedAt = null;
            if (entryElement.TryGetProperty("updatedAt", out var updatedElement) && updatedElement.ValueKind == JsonValueKind.Number)
            {
                var seconds = updatedElement.GetInt64();
                if (seconds > 0)
                {
                    updatedAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
                }
            }

            var entryId = entryElement.TryGetProperty("id", out var entryIdElement) && entryIdElement.ValueKind == JsonValueKind.Number
                ? entryIdElement.GetInt32()
                : (int?)null;

            return new AniListTrackingInfo(
                mediaId,
                title,
                cover,
                status,
                progress,
                score,
                chapters,
                siteUrl,
                updatedAt,
                entryId);
        }
        private async Task<AniListTrackingInfo?> FetchTrackingInfoByMediaIdAsync(int mediaId, string fallbackTitle, string? fallbackCover, CancellationToken cancellationToken)
        {
            const string query = @"query($mediaId: Int!) {
  Media(id: $mediaId, type: MANGA) {
    id
    title {
      romaji
      english
      native
    }
    coverImage {
      large
    }
    siteUrl
    chapters
    mediaListEntry {
      id
      status
      progress
      score
      updatedAt
    }
  }
}";

            var variables = new
            {
                mediaId
            };

            using var document = await TrySendGraphQlRequestAsync(query, variables, cancellationToken).ConfigureAwait(false);
            if (document == null)
            {
                return null;
            }
            if (!document.RootElement.TryGetProperty("data", out var dataElement) ||
                !dataElement.TryGetProperty("Media", out var mediaElement) ||
                mediaElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var titleElement = mediaElement.TryGetProperty("title", out var titleJson) ? titleJson : default;
            var title = ResolveTitle(titleElement, fallbackTitle);
            var cover = mediaElement.TryGetProperty("coverImage", out var coverElement) &&
                        coverElement.TryGetProperty("large", out var coverUrl)
                ? coverUrl.GetString()
                : fallbackCover;
            var siteUrl = mediaElement.TryGetProperty("siteUrl", out var siteUrlElement) ? siteUrlElement.GetString() : null;
            var chapters = mediaElement.TryGetProperty("chapters", out var chaptersElement) && chaptersElement.ValueKind == JsonValueKind.Number
                ? chaptersElement.GetInt32()
                : (int?)null;

            AniListMediaListStatus? status = null;
            int? progress = null;
            double? score = null;
            DateTimeOffset? updatedAt = null;
            int? entryId = null;
            if (mediaElement.TryGetProperty("mediaListEntry", out var entryElement) && entryElement.ValueKind == JsonValueKind.Object)
            {
                entryId = entryElement.TryGetProperty("id", out var idElement) ? idElement.GetInt32() : (int?)null;
                status = AniListFormatting.FromApiValue(entryElement.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null);
                if (entryElement.TryGetProperty("progress", out var progressElement) && progressElement.ValueKind == JsonValueKind.Number)
                {
                    var progressValue = progressElement.GetInt32();
                    if (progressValue > 0)
                    {
                        progress = progressValue;
                    }
                }

                if (entryElement.TryGetProperty("score", out var scoreElement) && scoreElement.ValueKind == JsonValueKind.Number)
                {
                    var scoreValue = scoreElement.GetDouble();
                    if (scoreValue > 0)
                    {
                        score = scoreValue;
                    }
                }

                if (entryElement.TryGetProperty("updatedAt", out var updatedElement) && updatedElement.ValueKind == JsonValueKind.Number)
                {
                    var seconds = updatedElement.GetInt64();
                    if (seconds > 0)
                    {
                        updatedAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
                    }
                }
            }

            return new AniListTrackingInfo(
                mediaId,
                title,
                cover,
                status,
                progress,
                score,
                chapters,
                siteUrl,
                updatedAt,
                entryId);
        }
    }
}