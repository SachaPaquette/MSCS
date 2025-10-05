using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MSCS.Models;

namespace MSCS.Interfaces
{
    public interface IAniListService
    {
        bool IsAuthenticated { get; }
        string? UserName { get; }
        event EventHandler? AuthenticationChanged;
        event EventHandler? TrackingChanged;
        Task<bool> AuthenticateAsync(Window? owner);
        Task<IReadOnlyList<AniListMedia>> SearchSeriesAsync(string query, CancellationToken cancellationToken = default);
        Task<AniListTrackingInfo> TrackSeriesAsync(string mangaTitle, AniListMedia media, CancellationToken cancellationToken = default);
        Task UpdateProgressAsync(string mangaTitle, int progress, CancellationToken cancellationToken = default);
        bool TryGetTracking(string mangaTitle, out AniListTrackingInfo? trackingInfo);
    }
}