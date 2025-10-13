namespace MSCS.Models
{
    public class MyAnimeListMedia
    {
        public MyAnimeListMedia(
            int id,
            string title,
            string? synopsis,
            string? coverImageUrl,
            int? chapters,
            double? score,
            string? siteUrl)
        {
            Id = id;
            Title = title;
            Synopsis = synopsis;
            CoverImageUrl = coverImageUrl;
            Chapters = chapters;
            Score = score;
            SiteUrl = siteUrl;
        }

        public int Id { get; }
        public string Title { get; }
        public string? Synopsis { get; }
        public string? CoverImageUrl { get; }
        public int? Chapters { get; }
        public double? Score { get; }
        public string? SiteUrl { get; }
    }
}