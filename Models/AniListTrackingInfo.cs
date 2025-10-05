namespace MSCS.Models
{
    public class AniListTrackingInfo
    {
        public AniListTrackingInfo(int mediaId, string title, string? coverImageUrl)
        {
            MediaId = mediaId;
            Title = title;
            CoverImageUrl = coverImageUrl;
        }

        public int MediaId { get; }
        public string Title { get; }
        public string? CoverImageUrl { get; }
    }
}