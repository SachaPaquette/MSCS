using System;

namespace MSCS.ViewModels
{
    public class TrackingLibraryEntryViewModel : BaseViewModel
    {
        public TrackingLibraryEntryViewModel(
            object media,
            object? trackingInfo,
            string trackingKey,
            string title,
            string? statusText,
            string? progressText,
            string? scoreText,
            string? secondaryScoreText,
            string? updatedText,
            string? chaptersText,
            string? coverImageUrl,
            string? siteUrl,
            DateTimeOffset? updatedAt,
            object? statusValue)
        {
            Media = media ?? throw new ArgumentNullException(nameof(media));
            TrackingInfo = trackingInfo;
            TrackingKey = trackingKey ?? throw new ArgumentNullException(nameof(trackingKey));
            Title = title ?? throw new ArgumentNullException(nameof(title));
            StatusText = statusText;
            ProgressText = progressText;
            ScoreText = scoreText;
            SecondaryScoreText = secondaryScoreText;
            UpdatedText = updatedText;
            ChaptersText = chaptersText;
            CoverImageUrl = coverImageUrl;
            SiteUrl = siteUrl;
            UpdatedAt = updatedAt;
            StatusValue = statusValue;
        }

        public object Media { get; }

        public object? TrackingInfo { get; }

        public string TrackingKey { get; }

        public string Title { get; }

        public string? StatusText { get; }

        public string? ProgressText { get; }

        public string? ScoreText { get; }

        public string? SecondaryScoreText { get; }

        public string? UpdatedText { get; }

        public string? ChaptersText { get; }

        public string? CoverImageUrl { get; }

        public string? SiteUrl { get; }

        public DateTimeOffset? UpdatedAt { get; }

        public object? StatusValue { get; }
    }
}