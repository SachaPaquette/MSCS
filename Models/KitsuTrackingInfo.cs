using System;
using MSCS.Enums;

namespace MSCS.Models
{
    public class KitsuTrackingInfo
    {
        public KitsuTrackingInfo(
            string mediaId,
            string title,
            string? coverImageUrl,
            KitsuLibraryStatus status,
            int? progress,
            double? score,
            int? totalChapters,
            string? siteUrl,
            DateTimeOffset? updatedAt)
        {
            MediaId = mediaId;
            Title = title;
            CoverImageUrl = coverImageUrl;
            Status = status;
            Progress = progress;
            Score = score;
            TotalChapters = totalChapters;
            SiteUrl = siteUrl;
            UpdatedAt = updatedAt;
        }

        public string MediaId { get; }
        public string Title { get; }
        public string? CoverImageUrl { get; }
        public KitsuLibraryStatus Status { get; }
        public int? Progress { get; }
        public double? Score { get; }
        public int? TotalChapters { get; }
        public string? SiteUrl { get; }
        public DateTimeOffset? UpdatedAt { get; }

        public KitsuTrackingInfo With(
            KitsuLibraryStatus? status = null,
            int? progress = null,
            double? score = null,
            int? totalChapters = null,
            string? coverImageUrl = null,
            string? siteUrl = null,
            DateTimeOffset? updatedAt = null)
        {
            return new KitsuTrackingInfo(
                MediaId,
                Title,
                coverImageUrl ?? CoverImageUrl,
                status ?? Status,
                progress ?? Progress,
                score ?? Score,
                totalChapters ?? TotalChapters,
                siteUrl ?? SiteUrl,
                updatedAt ?? UpdatedAt);
        }
    }
}