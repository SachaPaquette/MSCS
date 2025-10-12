using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MSCS.Enums;
using MSCS.Models;

namespace MSCS.Interfaces
{
    public interface IAniListService
    {
        bool IsAuthenticated { get; }
        string? UserName { get; }
        event EventHandler? AuthenticationChanged;
        event EventHandler<AniListTrackingChangedEventArgs>? TrackingChanged;
        Task<bool> AuthenticateAsync(Window? owner);
        Task<IReadOnlyList<AniListMedia>> SearchSeriesAsync(string query, CancellationToken cancellationToken = default);
        Task<IReadOnlyDictionary<AniListMediaListStatus, IReadOnlyList<AniListMedia>>> GetUserListsAsync(CancellationToken cancellationToken = default);
        Task LogoutAsync();
        Task<AniListTrackingInfo> TrackSeriesAsync(
            string mangaTitle,
            AniListMedia media,
            AniListMediaListStatus? status = null,
            int? progress = null,
            double? score = null,
            CancellationToken cancellationToken = default);
        Task UpdateProgressAsync(string mangaTitle, int progress, CancellationToken cancellationToken = default);
        Task<AniListTrackingInfo?> UpdateTrackingAsync(
            string mangaTitle,
            AniListMediaListStatus? status = null,
            int? progress = null,
            double? score = null,
            CancellationToken cancellationToken = default);
        Task<AniListTrackingInfo?> RefreshTrackingAsync(string mangaTitle, CancellationToken cancellationToken = default);
        Task<bool> UntrackSeriesAsync(string mangaTitle, CancellationToken cancellationToken = default);
        bool TryGetTracking(string mangaTitle, out AniListTrackingInfo? trackingInfo);
    }
}