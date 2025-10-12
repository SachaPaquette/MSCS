using System;

namespace MSCS.Models
{
    public sealed class AniListTrackingChangedEventArgs : MediaTrackingChangedEventArgs<AniListTrackingInfo>
    {
        public AniListTrackingChangedEventArgs(string? mangaTitle, int mediaId, AniListTrackingInfo? trackingInfo)
            : base(mangaTitle, trackingInfo)
        {
            MediaId = mediaId;
        }

        public string? MangaTitle => SeriesTitle;

        public int MediaId { get; }

        public new AniListTrackingInfo? TrackingInfo => base.TrackingInfo;
    }
}