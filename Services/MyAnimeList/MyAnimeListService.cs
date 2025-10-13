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

namespace MSCS.Services.MyAnimeList
{
    public class MyAnimeListService : IMediaTrackingService<MyAnimeListMedia, MyAnimeListTrackingInfo, MyAnimeListStatus>
    {
        private const string BaseUrl = "https://api.jikan.moe/v4";
        private static readonly HttpClient HttpClient = CreateHttpClient();

        private readonly Dictionary<string, MyAnimeListTrackingInfo> _trackedSeries = new(StringComparer.OrdinalIgnoreCase);
        private readonly UserSettings _userSettings;
        private bool _isAuthenticated;

        public MyAnimeListService(UserSettings userSettings)
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

        public string ServiceId => "MyAnimeList";

        public string DisplayName => "MyAnimeList";

        public bool IsAuthenticated => _isAuthenticated;

        public string? UserName => null;

        public event EventHandler? AuthenticationChanged;

        public event EventHandler<MediaTrackingChangedEventArgs<MyAnimeListTrackingInfo>>? MediaTrackingChanged;

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

        public async Task<IReadOnlyList<MyAnimeListMedia>> SearchSeriesAsync(string query, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Array.Empty<MyAnimeListMedia>();
            }

            try
            {
                using var response = await HttpClient.GetAsync($"{BaseUrl}/manga?q={Uri.EscapeDataString(query)}&sfw", cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return Array.Empty<MyAnimeListMedia>();
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!document.RootElement.TryGetProperty("data", out var dataArray) || dataArray.ValueKind != JsonValueKind.Array)
                {
                    return Array.Empty<MyAnimeListMedia>();
                }

                var results = new List<MyAnimeListMedia>();
                foreach (var element in dataArray.EnumerateArray())
                {
                    var media = ParseMedia(element);
                    if (media != null)
                    {
                        results.Add(media);
                    }
                }

                return new ReadOnlyCollection<MyAnimeListMedia>(results);
            }
            catch
            {
                return Array.Empty<MyAnimeListMedia>();
            }
        }

        public Task<IReadOnlyDictionary<MyAnimeListStatus, IReadOnlyList<MyAnimeListMedia>>> GetUserListsAsync(CancellationToken cancellationToken = default)
        {
            var groups = new Dictionary<MyAnimeListStatus, List<MyAnimeListMedia>>();
            foreach (var status in Enum.GetValues<MyAnimeListStatus>())
            {
                groups[status] = new List<MyAnimeListMedia>();
            }

            foreach (var tracking in _trackedSeries.Values)
            {
                if (!groups.TryGetValue(tracking.Status, out var bucket))
                {
                    bucket = new List<MyAnimeListMedia>();
                    groups[tracking.Status] = bucket;
                }

                bucket.Add(new MyAnimeListMedia(
                    tracking.MediaId,
                    tracking.Title,
                    null,
                    tracking.CoverImageUrl,
                    tracking.TotalChapters,
                    tracking.Score,
                    tracking.SiteUrl));
            }

            var result = new Dictionary<MyAnimeListStatus, IReadOnlyList<MyAnimeListMedia>>(groups.Count);
            foreach (var kvp in groups)
            {
                result[kvp.Key] = new ReadOnlyCollection<MyAnimeListMedia>(kvp.Value);
            }

            return Task.FromResult<IReadOnlyDictionary<MyAnimeListStatus, IReadOnlyList<MyAnimeListMedia>>>(result);
        }

        public Task UpdateProgressAsync(string seriesTitle, int progress, CancellationToken cancellationToken = default)
        {
            return UpdateTrackingInternalAsync(seriesTitle, progress: progress, cancellationToken: cancellationToken);
        }

        public Task<MyAnimeListTrackingInfo> TrackSeriesAsync(
            string seriesTitle,
            MyAnimeListMedia media,
            MyAnimeListStatus? status = null,
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

            var selectedStatus = status ?? MyAnimeListStatus.Reading;
            var sanitizedScore = ClampScore(score ?? media.Score);
            var normalizedProgress = NormalizeProgress(progress);
            var info = new MyAnimeListTrackingInfo(
                media.Id,
                media.Title,
                media.CoverImageUrl,
                selectedStatus,
                normalizedProgress,
                sanitizedScore,
                media.Chapters,
                media.SiteUrl,
                DateTimeOffset.UtcNow);

            SaveTracking(seriesTitle, info);
            RaiseTrackingChanged(seriesTitle, info);
            return Task.FromResult(info);
        }

        public Task<MyAnimeListTrackingInfo?> UpdateTrackingAsync(
            string seriesTitle,
            MyAnimeListStatus? status = null,
            int? progress = null,
            double? score = null,
            CancellationToken cancellationToken = default)
        {
            return UpdateTrackingInternalAsync(seriesTitle, status, progress, score, cancellationToken);
        }

        public async Task<MyAnimeListTrackingInfo?> RefreshTrackingAsync(string seriesTitle, CancellationToken cancellationToken = default)
        {
            if (!_trackedSeries.TryGetValue(seriesTitle, out var existing))
            {
                return null;
            }

            if (existing.MediaId <= 0)
            {
                return existing;
            }

            var refreshedMedia = await FetchMediaByIdAsync(existing.MediaId, cancellationToken).ConfigureAwait(false);
            if (refreshedMedia == null)
            {
                return existing;
            }

            var refreshed = existing.With(
                totalChapters: refreshedMedia.Chapters ?? existing.TotalChapters,
                coverImageUrl: string.IsNullOrWhiteSpace(refreshedMedia.CoverImageUrl) ? existing.CoverImageUrl : refreshedMedia.CoverImageUrl,
                siteUrl: string.IsNullOrWhiteSpace(refreshedMedia.SiteUrl) ? existing.SiteUrl : refreshedMedia.SiteUrl,
                score: existing.Score,
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

        public bool TryGetTracking(string seriesTitle, out MyAnimeListTrackingInfo? trackingInfo)
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
            return client;
        }

        private static double? ClampScore(double? score)
        {
            if (!score.HasValue)
            {
                return null;
            }

            return Math.Clamp(score.Value, 0, 10);
        }

        private static int? NormalizeProgress(int? value)
        {
            if (!value.HasValue)
            {
                return null;
            }

            return Math.Max(0, value.Value);
        }

        private Task<MyAnimeListTrackingInfo?> UpdateTrackingInternalAsync(
            string seriesTitle,
            MyAnimeListStatus? status = null,
            int? progress = null,
            double? score = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(seriesTitle) || !_trackedSeries.TryGetValue(seriesTitle, out var existing))
            {
                return null;
            }

            var normalizedProgress = progress.HasValue ? NormalizeProgress(progress) : existing.Progress;
            var updated = existing.With(
                status: status ?? existing.Status,
                progress: normalizedProgress ?? existing.Progress,
                score: ClampScore(score ?? existing.Score),
                updatedAt: DateTimeOffset.UtcNow);

            SaveTracking(seriesTitle, updated);
            RaiseTrackingChanged(seriesTitle, updated);
            return Task.FromResult<MyAnimeListTrackingInfo?>(updated);
        }

        private void SaveTracking(string seriesTitle, MyAnimeListTrackingInfo info)
        {
            _trackedSeries[seriesTitle] = info;
            var entry = new MediaTrackingEntry(
                SerializeMediaId(info.MediaId),
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

        private static string FormatStatus(MyAnimeListStatus status)
        {
            return status switch
            {
                MyAnimeListStatus.PlanToRead => "PlanToRead",
                _ => status.ToString()
            };
        }

        private static string? SerializeMediaId(int mediaId)
        {
            return mediaId > 0
                ? mediaId.ToString(CultureInfo.InvariantCulture)
                : null;
        }

        private static int ParseMediaId(string? mediaId)
        {
            if (int.TryParse(mediaId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }

            return 0;
        }

        private static MyAnimeListTrackingInfo? ConvertFromEntry(string seriesTitle, MediaTrackingEntry entry)
        {
            var status = ParseStatus(entry.Status);
            if (status == null)
            {
                return null;
            }

            return new MyAnimeListTrackingInfo(
                ParseMediaId(entry.MediaId),
                entry.Title ?? seriesTitle,
                entry.CoverImageUrl,
                status.Value,
                entry.Progress,
                entry.Score,
                entry.TotalChapters,
                entry.SiteUrl,
                entry.UpdatedAt);
        }

        private static MyAnimeListStatus? ParseStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return null;
            }

            return status.Trim().ToLowerInvariant() switch
            {
                "reading" => MyAnimeListStatus.Reading,
                "completed" => MyAnimeListStatus.Completed,
                "onhold" => MyAnimeListStatus.OnHold,
                "on_hold" => MyAnimeListStatus.OnHold,
                "dropped" => MyAnimeListStatus.Dropped,
                "plantoread" => MyAnimeListStatus.PlanToRead,
                "plan_to_read" => MyAnimeListStatus.PlanToRead,
                _ => null
            };
        }

        private static MyAnimeListMedia? ParseMedia(JsonElement element)
        {
            if (!element.TryGetProperty("mal_id", out var idElement) || idElement.ValueKind != JsonValueKind.Number)
            {
                return null;
            }

            var id = idElement.GetInt32();
            var title = element.TryGetProperty("title", out var titleElement) ? titleElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            string? coverImage = null;
            if (element.TryGetProperty("images", out var imagesElement) && imagesElement.ValueKind == JsonValueKind.Object)
            {
                if (imagesElement.TryGetProperty("jpg", out var jpgElement) && jpgElement.ValueKind == JsonValueKind.Object)
                {
                    if (jpgElement.TryGetProperty("large_image_url", out var largeImage) && largeImage.ValueKind == JsonValueKind.String)
                    {
                        coverImage = largeImage.GetString();
                    }
                    else if (jpgElement.TryGetProperty("image_url", out var image) && image.ValueKind == JsonValueKind.String)
                    {
                        coverImage = image.GetString();
                    }
                }
            }

            var synopsis = element.TryGetProperty("synopsis", out var synopsisElement) ? synopsisElement.GetString() : null;
            int? chapters = element.TryGetProperty("chapters", out var chaptersElement) && chaptersElement.ValueKind == JsonValueKind.Number
                ? chaptersElement.GetInt32()
                : null;
            double? score = element.TryGetProperty("score", out var scoreElement) && scoreElement.ValueKind == JsonValueKind.Number
                ? scoreElement.GetDouble()
                : null;
            var siteUrl = element.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : null;

            return new MyAnimeListMedia(
                id,
                title!,
                synopsis,
                coverImage,
                chapters,
                score,
                siteUrl);
        }

        private async Task<MyAnimeListMedia?> FetchMediaByIdAsync(int mediaId, CancellationToken cancellationToken)
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

        private void RaiseTrackingChanged(string? seriesTitle, MyAnimeListTrackingInfo? trackingInfo)
        {
            MediaTrackingChanged?.Invoke(this, new MediaTrackingChangedEventArgs<MyAnimeListTrackingInfo>(seriesTitle, trackingInfo));
        }
    }
}