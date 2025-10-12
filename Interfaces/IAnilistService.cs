using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MSCS.Enums;
using MSCS.Models;

namespace MSCS.Interfaces
{
    public interface IAniListService : IMediaTrackingService<AniListMedia, AniListTrackingInfo, AniListMediaListStatus>
    {
        new event EventHandler<AniListTrackingChangedEventArgs>? TrackingChanged;

        Task<IReadOnlyList<AniListMedia>> GetTopSeriesAsync(
            AniListRecommendationCategory category,
            int perPage = 12,
            CancellationToken cancellationToken = default);
    }
}