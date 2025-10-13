using System;
using MSCS.Enums;

namespace MSCS.Models
{
    public class MyAnimeListTrackingInfo
    {
        public MyAnimeListTrackingInfo(
            int mediaId,
            string title,
            string? coverImageUrl,
            MyAnimeListStatus status,
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

        public int MediaId { get; }
        public string Title { get; }
        public string? CoverImageUrl { get; }
        public MyAnimeListStatus Status { get; }
        public int? Progress { get; }
        public double? Score { get; }
        public int? TotalChapters { get; }
        public string? SiteUrl { get; }
        public DateTimeOffset? UpdatedAt { get; }

        public MyAnimeListTrackingInfo With(
            MyAnimeListStatus? status = null,
            int? progress = null,
            double? score = null,
            int? totalChapters = null,
            string? coverImageUrl = null,
            string? siteUrl = null,
            DateTimeOffset? updatedAt = null)
        {
            return new MyAnimeListTrackingInfo(
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