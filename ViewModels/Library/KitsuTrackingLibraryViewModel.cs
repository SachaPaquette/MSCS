using MSCS.Enums;
using MSCS.Models;
using MSCS.Services.Kitsu;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using static MSCS.ViewModels.Library.TrackingLibraryStatisticsViewModel;

namespace MSCS.ViewModels
{
    public class KitsuTrackingLibraryViewModel : TrackingLibraryViewModelBase<KitsuMedia, KitsuTrackingInfo, KitsuLibraryStatus>
    {
        private readonly KitsuService _service;

        public KitsuTrackingLibraryViewModel(KitsuService service)
            : base(service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public override bool SupportsTrackingEditor => false;

        protected override IEnumerable<KitsuLibraryStatus> GetOrderedStatuses()
        {
            return new[]
            {
                KitsuLibraryStatus.Current,
                KitsuLibraryStatus.Planned,
                KitsuLibraryStatus.Completed,
                KitsuLibraryStatus.OnHold,
                KitsuLibraryStatus.Dropped
            };
        }

        protected override string GetStatusDisplayName(KitsuLibraryStatus status)
        {
            return status switch
            {
                KitsuLibraryStatus.Current => "Currently reading",
                KitsuLibraryStatus.OnHold => "On hold",
                KitsuLibraryStatus.Planned => "Planned",
                _ => status.ToString()
            };
        }

        protected override TrackingLibraryEntryViewModel CreateEntryViewModel(KitsuMedia media, KitsuLibraryStatus status)
        {
            if (media == null)
            {
                throw new ArgumentNullException(nameof(media));
            }

            var trackingKey = !string.IsNullOrWhiteSpace(media.Title)
                ? media.Title
                : media.Id ?? string.Empty;

            _service.TryGetTracking(trackingKey, out var trackingInfo);

            var effectiveStatus = trackingInfo?.Status ?? status;
            var statusDisplay = GetStatusDisplayName(effectiveStatus);

            string? progressText = null;
            if (trackingInfo?.Progress is > 0)
            {
                progressText = trackingInfo.TotalChapters.HasValue && trackingInfo.TotalChapters.Value > 0
                    ? string.Format(CultureInfo.CurrentCulture, "{0}/{1}", trackingInfo.Progress, trackingInfo.TotalChapters)
                    : trackingInfo.Progress.Value.ToString(CultureInfo.CurrentCulture);
            }

            string? scoreText = trackingInfo?.Score is > 0
                ? string.Format(CultureInfo.CurrentCulture, "Score: {0:0.#}", trackingInfo.Score)
                : null;

            string? averageScore = media.AverageRating is > 0
                ? string.Format(CultureInfo.CurrentCulture, "Average score: {0:0.#}", media.AverageRating)
                : null;

            string? updatedText = trackingInfo?.UpdatedAt.HasValue == true
                ? trackingInfo.UpdatedAt.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
                : null;

            string? chaptersText = trackingInfo?.TotalChapters is > 0
                ? string.Format(CultureInfo.CurrentCulture, "Chapters: {0}", trackingInfo.TotalChapters)
                : media.ChapterCount is > 0
                    ? string.Format(CultureInfo.CurrentCulture, "Chapters: {0}", media.ChapterCount)
                    : null;

            return new TrackingLibraryEntryViewModel(
                media,
                trackingInfo,
                trackingKey,
                !string.IsNullOrWhiteSpace(trackingInfo?.Title)
                    ? trackingInfo.Title
                    : !string.IsNullOrWhiteSpace(media.Title)
                        ? media.Title
                        : media.Id ?? string.Empty,
                statusDisplay,
                progressText,
                scoreText,
                averageScore,
                updatedText,
                chaptersText,
                trackingInfo?.CoverImageUrl ?? media.CoverImageUrl,
                trackingInfo?.SiteUrl ?? media.SiteUrl,
                trackingInfo?.UpdatedAt,
                effectiveStatus);
            
        }

        protected override string GetTrackingKey(KitsuMedia media)
        {
            if (media == null)
            {
                throw new ArgumentNullException(nameof(media));
            }

            return !string.IsNullOrWhiteSpace(media.Title)
                ? media.Title
                : media.Id ?? string.Empty;
        }

        protected override TrackingLibraryStatisticsSummary CreateStatisticsSummary(IReadOnlyDictionary<KitsuLibraryStatus, IReadOnlyList<KitsuMedia>> lists)
        {
            if (lists == null || lists.Count == 0)
            {
                return default;
            }

            var trackingInfos = new List<KitsuTrackingInfo>();
            foreach (var kvp in lists)
            {
                foreach (var media in kvp.Value)
                {
                    var trackingKey = GetTrackingKey(media);

                    if (_service.TryGetTracking(trackingKey, out var info) && info != null)
                    {
                        trackingInfos.Add(info);
                    }
                    else
                    {
                        trackingInfos.Add(new KitsuTrackingInfo(
                            media.Id,
                            media.Title,
                            media.CoverImageUrl,
                            kvp.Key,
                            null,
                            null,
                            media.ChapterCount,
                            media.SiteUrl,
                            null));
                    }
                }
            }

            if (trackingInfos.Count == 0)
            {
                return default;
            }

            var totalSeries = trackingInfos.Count;
            var completed = trackingInfos.Count(info => info.Status == KitsuLibraryStatus.Completed);
            var inProgress = trackingInfos.Count(info => info.Status == KitsuLibraryStatus.Current);
            var planned = trackingInfos.Count(info => info.Status == KitsuLibraryStatus.Planned);
            var paused = trackingInfos.Count(info => info.Status == KitsuLibraryStatus.OnHold);
            var dropped = trackingInfos.Count(info => info.Status == KitsuLibraryStatus.Dropped);
            var chaptersRead = trackingInfos.Sum(info => info.Progress ?? 0);

            var scoredEntries = trackingInfos
                .Select(info => info.Score)
                .Where(score => score.HasValue && score.Value > 0)
                .Select(score => score!.Value)
                .ToList();

            double? averageScore = scoredEntries.Count > 0
                ? scoredEntries.Average()
                : null;

            DateTimeOffset? lastUpdated = null;
            var updates = trackingInfos
                .Select(info => info.UpdatedAt)
                .Where(updated => updated.HasValue)
                .Select(updated => updated!.Value)
                .ToList();
            if (updates.Count > 0)
            {
                lastUpdated = updates.Max();
            }

            return new TrackingLibraryStatisticsSummary(
                totalSeries,
                completed,
                inProgress,
                planned,
                paused,
                dropped,
                chaptersRead,
                averageScore,
                lastUpdated);
        }

        protected override Task OpenTrackingEditorAsync(object? parameter)
        {
            // Kitsu tracking management is handled via the reader flow only.
            return Task.CompletedTask;
        }
    }
}