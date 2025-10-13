namespace MSCS.Models
{
    public class KitsuMedia
    {
        public KitsuMedia(
            string id,
            string title,
            string? synopsis,
            string? coverImageUrl,
            int? chapterCount,
            double? averageRating,
            string? siteUrl)
        {
            Id = id;
            Title = title;
            Synopsis = synopsis;
            CoverImageUrl = coverImageUrl;
            ChapterCount = chapterCount;
            AverageRating = averageRating;
            SiteUrl = siteUrl;
        }

        public string Id { get; }
        public string Title { get; }
        public string? Synopsis { get; }
        public string? CoverImageUrl { get; }
        public int? ChapterCount { get; }
        public double? AverageRating { get; }
        public string? SiteUrl { get; }
    }
}