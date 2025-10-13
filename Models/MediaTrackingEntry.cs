using System;

namespace MSCS.Models
{
    /// <summary>
    /// Represents a serialisable snapshot of tracking information stored in <see cref="Services.UserSettings"/>.
    /// </summary>
    public class MediaTrackingEntry
    {
        public MediaTrackingEntry(
            string? mediaId,
            string? title,
            string? coverImageUrl,
            string? status,
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

        public string? MediaId { get; }
        public string? Title { get; }
        public string? CoverImageUrl { get; }
        public string? Status { get; }
        public int? Progress { get; }
        public double? Score { get; }
        public int? TotalChapters { get; }
        public string? SiteUrl { get; }
        public DateTimeOffset? UpdatedAt { get; }
    }
}