using System;

namespace MSCS.Models
{
    /// <summary>
    /// Generic event arguments used when a media tracking provider updates a tracked series.
    /// </summary>
    /// <typeparam name="TTrackingInfo">The type of tracking payload returned by the provider.</typeparam>
    public class MediaTrackingChangedEventArgs<TTrackingInfo> : EventArgs
    {
        public MediaTrackingChangedEventArgs(string? seriesTitle, TTrackingInfo? trackingInfo)
        {
            SeriesTitle = seriesTitle;
            TrackingInfo = trackingInfo;
        }

        /// <summary>
        /// Gets the local series title associated with the update.
        /// </summary>
        public string? SeriesTitle { get; }

        /// <summary>
        /// Gets the provider specific tracking payload. This value is null when the series is no longer tracked.
        /// </summary>
        public TTrackingInfo? TrackingInfo { get; }
    }
}