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
    }
}