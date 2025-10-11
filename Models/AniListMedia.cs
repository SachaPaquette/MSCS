using System;
using System.Globalization;
using MSCS.Enums;
using MSCS.Helpers;

namespace MSCS.Models
{
    public class AniListMedia
    {
        public int Id { get; init; }
        public string? RomajiTitle { get; init; }
        public string? EnglishTitle { get; init; }
        public string? NativeTitle { get; init; }
        public string DisplayTitle => !string.IsNullOrWhiteSpace(EnglishTitle)
            ? EnglishTitle!
            : !string.IsNullOrWhiteSpace(RomajiTitle)
                ? RomajiTitle!
                : NativeTitle ?? string.Empty;

        public string? Status { get; init; }
        public string? CoverImageUrl { get; init; }
        public string? BannerImageUrl { get; init; }
        public string? StartDateText { get; init; }
        public string? Format { get; init; }
        public int? Chapters { get; init; }
        public string? SiteUrl { get; init; }
        public double? AverageScore { get; init; }
        public double? MeanScore { get; init; }

        public AniListMediaListStatus? UserStatus { get; init; }
        public int? UserProgress { get; init; }
        public double? UserScore { get; init; }
        public DateTimeOffset? UserUpdatedAt { get; init; }

        public string? FormatDisplay => AniListFormatting.ToDisplayTitle(Format);
        public string? AverageScoreDisplay => AverageScore.HasValue
            ? string.Format(CultureInfo.CurrentCulture, "{0:0}", AverageScore.Value)
            : null;
        public string? MeanScoreDisplay => MeanScore.HasValue
            ? string.Format(CultureInfo.CurrentCulture, "{0:0}", MeanScore.Value)
            : null;
        public string? UserStatusDisplay => UserStatus?.ToDisplayString();
        public string? UserScoreDisplay => UserScore.HasValue
            ? string.Format(CultureInfo.CurrentCulture, "{0:0.#}", UserScore.Value)
            : null;
        public string? UserProgressDisplay
        {
            get
            {
                if (!UserProgress.HasValue && !Chapters.HasValue)
                {
                    return null;
                }

                var progressValue = UserProgress ?? 0;
                return Chapters.HasValue
                    ? string.Format(CultureInfo.CurrentCulture, "{0}/{1}", progressValue, Chapters.Value)
                    : progressValue.ToString(CultureInfo.CurrentCulture);
            }
        }

        public string? UserUpdatedAtDisplay => UserUpdatedAt.HasValue
            ? UserUpdatedAt.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
            : null;
    }
}