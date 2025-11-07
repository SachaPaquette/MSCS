using MSCS.Enums;
using MSCS.Interfaces;
using MSCS.Models;
using MSCS.Services.MyAnimeList;
using MSCS.Views;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using static MSCS.ViewModels.Library.TrackingLibraryStatisticsViewModel;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace MSCS.ViewModels
{
    public class MyAnimeListTrackingLibraryViewModel : TrackingLibraryViewModelBase<MyAnimeListMedia, MyAnimeListTrackingInfo, MyAnimeListStatus>
    {
        private readonly MyAnimeListService _service;

        public MyAnimeListTrackingLibraryViewModel(MyAnimeListService service)
            : base(service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        protected override IEnumerable<MyAnimeListStatus> GetOrderedStatuses()
        {
            return new[]
            {
                MyAnimeListStatus.Reading,
                MyAnimeListStatus.PlanToRead,
                MyAnimeListStatus.Completed,
                MyAnimeListStatus.OnHold,
                MyAnimeListStatus.Dropped
            };
        }

        protected override string GetStatusDisplayName(MyAnimeListStatus status)
        {
            return status switch
            {
                MyAnimeListStatus.PlanToRead => "Plan to read",
                MyAnimeListStatus.OnHold => "On hold",
                MyAnimeListStatus.Reading => "Reading",
                _ => status.ToString()
            };
        }

        protected override TrackingLibraryEntryViewModel CreateEntryViewModel(MyAnimeListMedia media, MyAnimeListStatus status)
        {
            _service.TryGetTracking(media.Title, out var trackingInfo);

            var effectiveStatus = trackingInfo?.Status ?? status;
            var statusDisplay = GetStatusDisplayName(effectiveStatus);

            string? progressText = null;
            if (trackingInfo?.Progress is > 0)
            {
                progressText = trackingInfo.TotalChapters.HasValue && trackingInfo.TotalChapters.Value > 0
                    ? string.Format(CultureInfo.CurrentCulture, "{0}/{1}", trackingInfo.Progress, trackingInfo.TotalChapters)
                    : trackingInfo.Progress.Value.ToString(CultureInfo.CurrentCulture);
            }

            string? scoreText = trackingInfo?.Score is > 0
                ? string.Format(CultureInfo.CurrentCulture, "Score: {0:0.#}", trackingInfo.Score)
                : null;

            string? averageScore = media.Score is > 0
                ? string.Format(CultureInfo.CurrentCulture, "Average score: {0:0.#}", media.Score)
                : null;

            string? updatedText = trackingInfo?.UpdatedAt.HasValue == true
                ? trackingInfo.UpdatedAt.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
                : null;

            string? chaptersText = media.Chapters.HasValue && media.Chapters.Value > 0
                ? string.Format(CultureInfo.CurrentCulture, "Chapters: {0}", media.Chapters.Value)
                : null;

            return new TrackingLibraryEntryViewModel(
                media,
                trackingInfo,
                media.Title,
                media.Title,
                statusDisplay,
                progressText,
                scoreText,
                averageScore,
                updatedText,
                chaptersText,
                trackingInfo?.CoverImageUrl ?? media.CoverImageUrl,
                trackingInfo?.SiteUrl ?? media.SiteUrl,
                trackingInfo?.UpdatedAt,
                effectiveStatus);
        }

        protected override string GetTrackingKey(MyAnimeListMedia media)
        {
            return media.Title;
        }

        protected override TrackingLibraryStatisticsSummary CreateStatisticsSummary(IReadOnlyDictionary<MyAnimeListStatus, IReadOnlyList<MyAnimeListMedia>> lists)
        {
            if (lists == null || lists.Count == 0)
            {
                return default;
            }

            var trackingInfos = new List<MyAnimeListTrackingInfo>();

            foreach (var kvp in lists)
            {
                foreach (var media in kvp.Value)
                {
                    if (_service.TryGetTracking(media.Title, out var info) && info != null)
                    {
                        trackingInfos.Add(info);
                    }
                    else
                    {
                        trackingInfos.Add(new MyAnimeListTrackingInfo(
                            media.Id,
                            media.Title,
                            media.CoverImageUrl,
                            kvp.Key,
                            null,
                            null,
                            media.Chapters,
                            media.SiteUrl,
                            null));
                    }
                }
            }

            if (trackingInfos.Count == 0)
            {
                return default;
            }

            var totalSeries = trackingInfos.Count;
            var completed = trackingInfos.Count(info => info.Status == MyAnimeListStatus.Completed);
            var inProgress = trackingInfos.Count(info => info.Status == MyAnimeListStatus.Reading);
            var planned = trackingInfos.Count(info => info.Status == MyAnimeListStatus.PlanToRead);
            var paused = trackingInfos.Count(info => info.Status == MyAnimeListStatus.OnHold);
            var dropped = trackingInfos.Count(info => info.Status == MyAnimeListStatus.Dropped);
            var chaptersRead = trackingInfos.Sum(info => info.Progress ?? 0);

            var scoredEntries = trackingInfos
                .Select(info => info.Score)
                .Where(score => score.HasValue && score.Value > 0)
                .Select(score => score!.Value)
                .ToList();

            double? averageScore = scoredEntries.Count > 0
                ? scoredEntries.Average()
                : null;

            DateTimeOffset? lastUpdated = null;
            var updates = trackingInfos
                .Select(info => info.UpdatedAt)
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

            if (entry.Media is not MyAnimeListMedia media)
            {
                return;
            }

            var trackingKey = media.Title;
            MyAnimeListTrackingViewModel? trackingViewModel = null;

            try
            {
                _service.TryGetTracking(trackingKey, out var existingTracking);
                trackingViewModel = new MyAnimeListTrackingViewModel(
                    _service,
                    trackingKey,
                    media.Title,
                    existingTracking,
                    existingTracking?.Progress);

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
                System.Windows.MessageBox.Show(ex.Message, ServiceDisplayName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to open {ServiceDisplayName} tracking editor: {ex}");
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