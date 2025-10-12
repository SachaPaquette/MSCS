using MSCS.Models;
using System.Windows;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MSCS.Models;

namespace MSCS.Interfaces
{
    /// <summary>
    /// Provides access to shared metadata for a tracking provider.
    /// </summary>
    public interface IMediaTrackingService
    {
        /// <summary>
        /// Gets an identifier that uniquely describes the provider (e.g. "AniList").
        /// </summary>
        string ServiceId { get; }

        /// <summary>
        /// Gets a display name suitable for presenting the provider to the user.
        /// </summary>
        string DisplayName { get; }

        bool IsAuthenticated { get; }

        string? UserName { get; }

        event EventHandler? AuthenticationChanged;

        Task<bool> AuthenticateAsync(Window? owner);

        Task LogoutAsync();
    }

    /// <summary>
    /// Represents a catalog and tracking provider capable of synchronising
    /// reading progress with an external service such as AniList, MyAnimeList or MangaUpdates.
    /// </summary>
    /// <typeparam name="TMedia">The media representation returned by the provider.</typeparam>
    /// <typeparam name="TTrackingInfo">The tracking payload used by the provider.</typeparam>
    /// <typeparam name="TStatus">The list status type used by the provider (usually an enum).</typeparam>
    public interface IMediaTrackingService<TMedia, TTrackingInfo, TStatus> : IMediaTrackingService
        where TStatus : struct
    {
        event EventHandler<MediaTrackingChangedEventArgs<TTrackingInfo>>? MediaTrackingChanged;

        Task<IReadOnlyList<TMedia>> SearchSeriesAsync(string query, CancellationToken cancellationToken = default);

        Task<IReadOnlyDictionary<TStatus, IReadOnlyList<TMedia>>> GetUserListsAsync(CancellationToken cancellationToken = default);

        Task<TTrackingInfo> TrackSeriesAsync(
            string seriesTitle,
            TMedia media,
            TStatus? status = null,
            int? progress = null,
            double? score = null,
            CancellationToken cancellationToken = default);

        Task UpdateProgressAsync(string seriesTitle, int progress, CancellationToken cancellationToken = default);

        Task<TTrackingInfo?> UpdateTrackingAsync(
            string seriesTitle,
            TStatus? status = null,
            int? progress = null,
            double? score = null,
            CancellationToken cancellationToken = default);

        Task<TTrackingInfo?> RefreshTrackingAsync(string seriesTitle, CancellationToken cancellationToken = default);

        Task<bool> UntrackSeriesAsync(string seriesTitle, CancellationToken cancellationToken = default);

        bool TryGetTracking(string seriesTitle, out TTrackingInfo? trackingInfo);
    }
}