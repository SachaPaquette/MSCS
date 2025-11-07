using MSCS.Enums;
using MSCS.Helpers;
using MSCS.Interfaces;
using MSCS.Models;
using MSCS.Views;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using static MSCS.ViewModels.Library.TrackingLibraryStatisticsViewModel;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace MSCS.ViewModels
{
    public class AniListTrackingLibraryViewModel : TrackingLibraryViewModelBase<AniListMedia, AniListTrackingInfo, AniListMediaListStatus>
    {
        private readonly IAniListService _aniListService;

        public AniListTrackingLibraryViewModel(IAniListService aniListService)
            : base(aniListService)
        {
            _aniListService = aniListService ?? throw new ArgumentNullException(nameof(aniListService));
        }

        protected override IEnumerable<AniListMediaListStatus> GetOrderedStatuses()
        {
            return Enum.GetValues<AniListMediaListStatus>();
        }

        protected override string GetStatusDisplayName(AniListMediaListStatus status)
        {
            return status.ToDisplayString();
        }

        protected override TrackingLibraryEntryViewModel CreateEntryViewModel(AniListMedia media, AniListMediaListStatus status)
        {
            _aniListService.TryGetTracking(GetTrackingKey(media), out var trackingInfo);

            var statusDisplay = media.UserStatusDisplay ?? status.ToDisplayString();
            var progressText = media.UserProgressDisplay;
            var scoreText = media.UserScoreDisplay;
            var secondaryScore = media.MeanScoreDisplay is { Length: > 0 }
                ? $"Average score: {media.MeanScoreDisplay}"
                : media.AverageScoreDisplay is { Length: > 0 }
                    ? $"Average score: {media.AverageScoreDisplay}"
                    : null;
            var updatedText = media.UserUpdatedAtDisplay;
            string? chaptersText = media.Chapters.HasValue && media.Chapters.Value > 0
                ? $"Chapters: {media.Chapters.Value}"
                : null;

            return new TrackingLibraryEntryViewModel(
                media,
                trackingInfo,
                GetTrackingKey(media),
                media.DisplayTitle,
                statusDisplay,
                progressText,
                scoreText,
                secondaryScore,
                updatedText,
                chaptersText,
                media.CoverImageUrl,
                media.SiteUrl,
                media.UserUpdatedAt,
                media.UserStatus ?? status);
        }

        protected override string GetTrackingKey(AniListMedia media)
        {
            if (!string.IsNullOrWhiteSpace(media.EnglishTitle))
            {
                return media.EnglishTitle!;
            }

            if (!string.IsNullOrWhiteSpace(media.RomajiTitle))
            {
                return media.RomajiTitle!;
            }

            if (!string.IsNullOrWhiteSpace(media.NativeTitle))
            {
                return media.NativeTitle!;
            }

            return media.DisplayTitle;
        }

        protected override TrackingLibraryStatisticsSummary CreateStatisticsSummary(IReadOnlyDictionary<AniListMediaListStatus, IReadOnlyList<AniListMedia>> lists)
        {
            if (lists == null || lists.Count == 0)
            {
                return default;
            }

            var allEntries = lists.Values
                .Where(group => group != null)
                .SelectMany(group => group!)
                .Where(media => media != null)
                .ToList();

            if (allEntries.Count == 0)
            {
                return default;
            }

            var totalSeries = allEntries.Count;
            var completed = allEntries.Count(media => media.UserStatus == AniListMediaListStatus.Completed);
            var inProgress = allEntries.Count(media => media.UserStatus == AniListMediaListStatus.Current || media.UserStatus == AniListMediaListStatus.Repeating);
            var planned = allEntries.Count(media => media.UserStatus == AniListMediaListStatus.Planning);
            var paused = allEntries.Count(media => media.UserStatus == AniListMediaListStatus.Paused);
            var dropped = allEntries.Count(media => media.UserStatus == AniListMediaListStatus.Dropped);
            var chaptersRead = allEntries.Sum(media => media.UserProgress ?? 0);

            var scoredEntries = allEntries
                .Select(media => media.UserScore)
                .Where(score => score.HasValue && score.Value > 0)
                .Select(score => score!.Value)
                .ToList();

            double? averageScore = scoredEntries.Count > 0
                ? scoredEntries.Average()
                : null;

            DateTimeOffset? lastUpdated = null;
            var updates = allEntries
                .Select(media => media.UserUpdatedAt)
                .Where(updated => updated.HasValue)
                .Select(updated => updated!.Value)
                .ToList();
            if (updates.Count > 0)
            {
                lastUpdated = updates.Max();
            }

            return new TrackingLibraryStatisticsSummary(
                totalSeries,
                completed,
                inProgress,
                planned,
                paused,
                dropped,
                chaptersRead,
                averageScore,
                lastUpdated);
        }

        protected override async Task OpenTrackingEditorAsync(object? parameter)
        {
            if (parameter is not TrackingLibraryEntryViewModel entry)
            {
                return;
            }

            if (!IsAuthenticated)
            {
                MessageBox.Show(ConnectAccountMessage, ServiceDisplayName, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (entry.Media is not AniListMedia media)
            {
                return;
            }

            var trackingKey = GetTrackingKey(media);
            AniListTrackingViewModel? trackingViewModel = null;

            try
            {
                _aniListService.TryGetTracking(trackingKey, out var existingTracking);
                trackingViewModel = new AniListTrackingViewModel(
                    _aniListService,
                    trackingKey,
                    media.DisplayTitle,
                    existingTracking,
                    null);

                var dialogViewModel = new TrackingWindowViewModel(
                    "Manage Tracking",
                    new ITrackingDialogViewModel[] { trackingViewModel });
                var dialog = new TrackingWindow(dialogViewModel);
                if (Application.Current?.MainWindow != null)
                {
                    dialog.Owner = Application.Current.MainWindow;
                }

                var result = dialog.ShowDialog();
                if (result == true && trackingViewModel.TrackingInfo != null)
                {
                    StatusMessage = $"{ServiceDisplayName} tracking updated.";
                }
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, ServiceDisplayName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to open {ServiceDisplayName} tracking editor: {ex}");
                MessageBox.Show(
                    $"Unable to open the {ServiceDisplayName} tracking editor right now.",
                    ServiceDisplayName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                trackingViewModel?.Dispose();
            }
        }
    }
}