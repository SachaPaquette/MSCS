using System;

namespace MSCS.Models
{
    public sealed class AniListTrackingChangedEventArgs : EventArgs
    {
        public AniListTrackingChangedEventArgs(string? mangaTitle, int mediaId, AniListTrackingInfo? trackingInfo)
        {
            MangaTitle = mangaTitle;
            MediaId = mediaId;
            TrackingInfo = trackingInfo;
        }

        public string? MangaTitle { get; }

        public int MediaId { get; }

        public AniListTrackingInfo? TrackingInfo { get; }
    }
}