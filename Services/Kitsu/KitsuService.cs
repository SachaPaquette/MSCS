using MSCS.Enums;
using MSCS.Interfaces;
using MSCS.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MSCS.Services.Kitsu
{
    public class KitsuService : IMediaTrackingService<KitsuMedia, KitsuTrackingInfo, KitsuLibraryStatus>
    {
        private const string BaseUrl = "https://kitsu.io/api/edge";
        private static readonly HttpClient HttpClient = CreateHttpClient();

        private readonly Dictionary<string, KitsuTrackingInfo> _trackedSeries = new(StringComparer.OrdinalIgnoreCase);
        private readonly UserSettings _userSettings;
        private bool _isAuthenticated;

        public KitsuService(UserSettings userSettings)
        {
            _userSettings = userSettings ?? throw new ArgumentNullException(nameof(userSettings));

            _isAuthenticated = _userSettings.IsTrackingProviderConnected(ServiceId);

            var snapshot = _userSettings.GetExternalTrackingSnapshot(ServiceId);
            foreach (var kvp in snapshot)
            {
                var info = ConvertFromEntry(kvp.Key, kvp.Value);
                if (info != null)
                {
                    _trackedSeries[kvp.Key] = info;
                }
            }
        }

        public string ServiceId => "Kitsu";

        public string DisplayName => "Kitsu";

        public bool IsAuthenticated => _isAuthenticated;

        public string? UserName => null;

        public event EventHandler? AuthenticationChanged;

        public event EventHandler<MediaTrackingChangedEventArgs<KitsuTrackingInfo>>? MediaTrackingChanged;

        public Task<bool> AuthenticateAsync(System.Windows.Window? owner)
        {
            if (_isAuthenticated)
            {
                return Task.FromResult(true);
            }

            _isAuthenticated = true;
            _userSettings.SetTrackingProviderConnection(ServiceId, true);
            AuthenticationChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(true);
        }

        public Task LogoutAsync()
        {
            if (!_isAuthenticated)
            {
                return Task.CompletedTask;
            }

            _isAuthenticated = false;
            _userSettings.SetTrackingProviderConnection(ServiceId, false);
            AuthenticationChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public async Task<IReadOnlyList<KitsuMedia>> SearchSeriesAsync(string query, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Array.Empty<KitsuMedia>();
            }

            try
            {
                var requestUri = $"{BaseUrl}/manga?filter[text]={Uri.EscapeDataString(query)}&page[limit]=20";
                using var response = await HttpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return Array.Empty<KitsuMedia>();
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!document.RootElement.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Array)
                {
                    return Array.Empty<KitsuMedia>();
                }

                var results = new List<KitsuMedia>();
                foreach (var item in dataElement.EnumerateArray())
                {
                    var media = ParseMedia(item);
                    if (media != null)
                    {
                        results.Add(media);
                    }
                }

                return new ReadOnlyCollection<KitsuMedia>(results);
            }
            catch
            {
                return Array.Empty<KitsuMedia>();
            }
        }

        public Task<IReadOnlyDictionary<KitsuLibraryStatus, IReadOnlyList<KitsuMedia>>> GetUserListsAsync(CancellationToken cancellationToken = default)
        {
            var groups = new Dictionary<KitsuLibraryStatus, List<KitsuMedia>>();
            foreach (var status in Enum.GetValues<KitsuLibraryStatus>())
            {
                groups[status] = new List<KitsuMedia>();
            }

            foreach (var tracking in _trackedSeries.Values)
            {
                if (!groups.TryGetValue(tracking.Status, out var bucket))
                {
                    bucket = new List<KitsuMedia>();
                    groups[tracking.Status] = bucket;
                }

                bucket.Add(new KitsuMedia(
                    tracking.MediaId,
                    tracking.Title,
                    null,
                    tracking.CoverImageUrl,
                    tracking.TotalChapters,
                    tracking.Score,
                    tracking.SiteUrl));
            }

            var result = new Dictionary<KitsuLibraryStatus, IReadOnlyList<KitsuMedia>>(groups.Count);
            foreach (var kvp in groups)
            {
                result[kvp.Key] = new ReadOnlyCollection<KitsuMedia>(kvp.Value);
            }

            return Task.FromResult<IReadOnlyDictionary<KitsuLibraryStatus, IReadOnlyList<KitsuMedia>>>(result);
        }

        public Task UpdateProgressAsync(string seriesTitle, int progress, CancellationToken cancellationToken = default)
        {
            return UpdateTrackingInternalAsync(seriesTitle, progress: progress, cancellationToken: cancellationToken);
        }

        public Task<KitsuTrackingInfo> TrackSeriesAsync(
            string seriesTitle,
            KitsuMedia media,
            KitsuLibraryStatus? status = null,
            int? progress = null,
            double? score = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(seriesTitle))
            {
                throw new ArgumentException("Series title cannot be empty.", nameof(seriesTitle));
            }

            if (media == null)
            {
                throw new ArgumentNullException(nameof(media));
            }

            var selectedStatus = status ?? KitsuLibraryStatus.Current;
            var normalizedProgress = NormalizeProgress(progress);
            var normalizedScore = ClampScore(score ?? media.AverageRating);
            var info = new KitsuTrackingInfo(
                media.Id,
                media.Title,
                media.CoverImageUrl,
                selectedStatus,
                normalizedProgress,
                normalizedScore,
                media.ChapterCount,
                media.SiteUrl,
                DateTimeOffset.UtcNow);

            SaveTracking(seriesTitle, info);
            RaiseTrackingChanged(seriesTitle, info);
            return Task.FromResult(info);
        }

        public Task<KitsuTrackingInfo?> UpdateTrackingAsync(
            string seriesTitle,
            KitsuLibraryStatus? status = null,
            int? progress = null,
            double? score = null,
            CancellationToken cancellationToken = default)
        {
            return UpdateTrackingInternalAsync(seriesTitle, status, progress, score, cancellationToken);
        }

        public async Task<KitsuTrackingInfo?> RefreshTrackingAsync(string seriesTitle, CancellationToken cancellationToken = default)
        {
            if (!_trackedSeries.TryGetValue(seriesTitle, out var existing))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(existing.MediaId))
            {
                return existing;
            }

            var refreshedMedia = await FetchMediaByIdAsync(existing.MediaId, cancellationToken).ConfigureAwait(false);
            if (refreshedMedia == null)
            {
                return existing;
            }

            var refreshed = existing.With(
                totalChapters: refreshedMedia.ChapterCount ?? existing.TotalChapters,
                coverImageUrl: string.IsNullOrWhiteSpace(refreshedMedia.CoverImageUrl) ? existing.CoverImageUrl : refreshedMedia.CoverImageUrl,
                siteUrl: string.IsNullOrWhiteSpace(refreshedMedia.SiteUrl) ? existing.SiteUrl : refreshedMedia.SiteUrl,
                updatedAt: DateTimeOffset.UtcNow);

            SaveTracking(seriesTitle, refreshed);
            RaiseTrackingChanged(seriesTitle, refreshed);
            return refreshed;
        }

        public Task<bool> UntrackSeriesAsync(string seriesTitle, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(seriesTitle))
            {
                return Task.FromResult(false);
            }

            if (_trackedSeries.Remove(seriesTitle))
            {
                _userSettings.RemoveExternalTracking(ServiceId, seriesTitle);
                RaiseTrackingChanged(seriesTitle, null);
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }

        public bool TryGetTracking(string seriesTitle, out KitsuTrackingInfo? trackingInfo)
        {
            return _trackedSeries.TryGetValue(seriesTitle, out trackingInfo);
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MSCS", "1.0"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));
            return client;
        }

        private static double? ClampScore(double? score)
        {
            if (!score.HasValue)
            {
                return null;
            }

            return Math.Clamp(score.Value, 0, 100);
        }

        private static int? NormalizeProgress(int? value)
        {
            if (!value.HasValue)
            {
                return null;
            }

            return Math.Max(0, value.Value);
        }

        private Task<KitsuTrackingInfo?> UpdateTrackingInternalAsync(
            string seriesTitle,
            KitsuLibraryStatus? status = null,
            int? progress = null,
            double? score = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(seriesTitle) || !_trackedSeries.TryGetValue(seriesTitle, out var existing))
            {
                return Task.FromResult<KitsuTrackingInfo?>(null);
            }

            var normalizedProgress = progress.HasValue ? NormalizeProgress(progress) : existing.Progress;
            var updated = existing.With(
                status: status ?? existing.Status,
                progress: normalizedProgress ?? existing.Progress,
                score: ClampScore(score ?? existing.Score),
                updatedAt: DateTimeOffset.UtcNow);

            SaveTracking(seriesTitle, updated);
            RaiseTrackingChanged(seriesTitle, updated);
            return Task.FromResult<KitsuTrackingInfo?>(updated);
        }

        private void SaveTracking(string seriesTitle, KitsuTrackingInfo info)
        {
            _trackedSeries[seriesTitle] = info;
            var entry = new MediaTrackingEntry(
                info.MediaId,
                info.Title,
                info.CoverImageUrl,
                FormatStatus(info.Status),
                info.Progress,
                info.Score,
                info.TotalChapters,
                info.SiteUrl,
                info.UpdatedAt);
            _userSettings.SetExternalTracking(ServiceId, seriesTitle, entry);
        }

        private static string FormatStatus(KitsuLibraryStatus status)
        {
            return status switch
            {
                KitsuLibraryStatus.Planned => "planned",
                KitsuLibraryStatus.OnHold => "on_hold",
                KitsuLibraryStatus.Current => "current",
                KitsuLibraryStatus.Completed => "completed",
                KitsuLibraryStatus.Dropped => "dropped",
                _ => status.ToString()
            };
        }

        private static KitsuTrackingInfo? ConvertFromEntry(string seriesTitle, MediaTrackingEntry entry)
        {
            var status = ParseStatus(entry.Status);
            if (status == null)
            {
                return null;
            }

            return new KitsuTrackingInfo(
                string.IsNullOrWhiteSpace(entry.MediaId) ? string.Empty : entry.MediaId,
                entry.Title ?? seriesTitle,
                entry.CoverImageUrl,
                status.Value,
                entry.Progress,
                entry.Score,
                entry.TotalChapters,
                entry.SiteUrl,
                entry.UpdatedAt);
        }

        private static KitsuLibraryStatus? ParseStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return null;
            }

            return status.Trim().ToLowerInvariant() switch
            {
                "current" => KitsuLibraryStatus.Current,
                "completed" => KitsuLibraryStatus.Completed,
                "on_hold" => KitsuLibraryStatus.OnHold,
                "onhold" => KitsuLibraryStatus.OnHold,
                "dropped" => KitsuLibraryStatus.Dropped,
                "planned" => KitsuLibraryStatus.Planned,
                _ => null
            };
        }

        private static KitsuMedia? ParseMedia(JsonElement element)
        {
            if (!element.TryGetProperty("id", out var idElement))
            {
                return null;
            }

            var id = idElement.GetString();
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            if (!element.TryGetProperty("attributes", out var attributes) || attributes.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var title = attributes.TryGetProperty("canonicalTitle", out var titleElement) ? titleElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            string? cover = null;
            if (attributes.TryGetProperty("posterImage", out var posterImage) && posterImage.ValueKind == JsonValueKind.Object)
            {
                if (posterImage.TryGetProperty("original", out var original) && original.ValueKind == JsonValueKind.String)
                {
                    cover = original.GetString();
                }
                else if (posterImage.TryGetProperty("large", out var large) && large.ValueKind == JsonValueKind.String)
                {
                    cover = large.GetString();
                }
            }

            var synopsis = attributes.TryGetProperty("synopsis", out var synopsisElement) ? synopsisElement.GetString() : null;
            int? chapterCount = null;
            if (attributes.TryGetProperty("chapterCount", out var chapterElement) && chapterElement.ValueKind == JsonValueKind.Number)
            {
                chapterCount = chapterElement.GetInt32();
            }

            double? rating = null;
            if (attributes.TryGetProperty("averageRating", out var ratingElement) && ratingElement.ValueKind == JsonValueKind.String)
            {
                if (double.TryParse(ratingElement.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedRating))
                {
                    rating = parsedRating;
                }
            }

            string? slug = attributes.TryGetProperty("slug", out var slugElement) ? slugElement.GetString() : null;
            var siteUrl = !string.IsNullOrWhiteSpace(slug)
                ? $"https://kitsu.io/manga/{slug}"
                : null;

            return new KitsuMedia(
                id,
                title!,
                synopsis,
                cover,
                chapterCount,
                rating,
                siteUrl);
        }

        private async Task<KitsuMedia?> FetchMediaByIdAsync(string mediaId, CancellationToken cancellationToken)
        {
            try
            {
                using var response = await HttpClient.GetAsync($"{BaseUrl}/manga/{mediaId}", cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!document.RootElement.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                return ParseMedia(dataElement);
            }
            catch
            {
                return null;
            }
        }

        private void RaiseTrackingChanged(string? seriesTitle, KitsuTrackingInfo? trackingInfo)
        {
            MediaTrackingChanged?.Invoke(this, new MediaTrackingChangedEventArgs<KitsuTrackingInfo>(seriesTitle, trackingInfo));
        }
    }
}