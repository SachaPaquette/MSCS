using System;
using MSCS.Enums;
using MSCS.Helpers;

namespace MSCS.Models
{
    public class AniListTrackingInfo
    {
        public AniListTrackingInfo(
            int mediaId,
            string title,
            string? coverImageUrl,
            AniListMediaListStatus? status = null,
            int? progress = null,
            double? score = null,
            int? totalChapters = null,
            string? siteUrl = null,
            DateTimeOffset? updatedAt = null,
            int? mediaListEntryId = null)
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
            MediaListEntryId = mediaListEntryId;
        }

        public int MediaId { get; }
        public string Title { get; }
        public string? CoverImageUrl { get; }
        public AniListMediaListStatus? Status { get; }
        public int? Progress { get; }
        public double? Score { get; }
        public int? TotalChapters { get; }
        public string? SiteUrl { get; }
        public DateTimeOffset? UpdatedAt { get; }
        public int? MediaListEntryId { get; }

        public string? StatusDisplay => Status?.ToDisplayString();
    }
}