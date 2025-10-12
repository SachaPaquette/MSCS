using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MSCS.Enums;
using MSCS.Models;

namespace MSCS.ViewModels
{
    public class AniListStatisticsViewModel : BaseViewModel
    {
        private int _totalSeries;
        private int _completedSeries;
        private int _inProgressSeries;
        private int _plannedSeries;
        private int _pausedSeries;
        private int _droppedSeries;
        private int _chaptersRead;
        private double? _averageScore;
        private DateTimeOffset? _lastUpdated;
        private bool _hasData;

        public int TotalSeries
        {
            get => _totalSeries;
            private set => SetProperty(ref _totalSeries, value);
        }

        public int CompletedSeries
        {
            get => _completedSeries;
            private set => SetProperty(ref _completedSeries, value);
        }

        public int InProgressSeries
        {
            get => _inProgressSeries;
            private set => SetProperty(ref _inProgressSeries, value);
        }

        public int PlannedSeries
        {
            get => _plannedSeries;
            private set => SetProperty(ref _plannedSeries, value);
        }

        public int PausedSeries
        {
            get => _pausedSeries;
            private set => SetProperty(ref _pausedSeries, value);
        }

        public int DroppedSeries
        {
            get => _droppedSeries;
            private set => SetProperty(ref _droppedSeries, value);
        }

        public int ChaptersRead
        {
            get => _chaptersRead;
            private set => SetProperty(ref _chaptersRead, value);
        }

        public double? AverageScore
        {
            get => _averageScore;
            private set
            {
                if (SetProperty(ref _averageScore, value))
                {
                    OnPropertyChanged(nameof(AverageScoreDisplay));
                }
            }
        }

        public DateTimeOffset? LastUpdated
        {
            get => _lastUpdated;
            private set
            {
                if (SetProperty(ref _lastUpdated, value))
                {
                    OnPropertyChanged(nameof(LastUpdatedDisplay));
                }
            }
        }

        public bool HasData
        {
            get => _hasData;
            private set => SetProperty(ref _hasData, value);
        }

        public string AverageScoreDisplay => AverageScore.HasValue
            ? string.Format(CultureInfo.CurrentCulture, "{0:0.0}", AverageScore.Value)
            : "—";

        public string LastUpdatedDisplay => LastUpdated.HasValue
            ? LastUpdated.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
            : "—";

        public void Update(IReadOnlyDictionary<AniListMediaListStatus, IReadOnlyList<AniListMedia>>? lists)
        {
            if (lists == null)
            {
                Reset();
                return;
            }

            var allEntries = lists.Values
                .Where(static group => group != null)
                .SelectMany(static group => group!)
                .Where(static media => media != null)
                .ToList();

            if (allEntries.Count == 0)
            {
                Reset();
                return;
            }

            TotalSeries = allEntries.Count;
            CompletedSeries = CountByStatus(allEntries, AniListMediaListStatus.Completed);
            InProgressSeries = CountByStatus(allEntries, AniListMediaListStatus.Current) +
                               CountByStatus(allEntries, AniListMediaListStatus.Repeating);
            PlannedSeries = CountByStatus(allEntries, AniListMediaListStatus.Planning);
            PausedSeries = CountByStatus(allEntries, AniListMediaListStatus.Paused);
            DroppedSeries = CountByStatus(allEntries, AniListMediaListStatus.Dropped);
            ChaptersRead = allEntries.Sum(static media => media.UserProgress ?? 0);

            var scoredEntries = allEntries
                .Select(static media => media.UserScore)
                .Where(static score => score.HasValue)
                .Select(static score => score!.Value)
                .ToList();

            AverageScore = scoredEntries.Count > 0
                ? scoredEntries.Average()
                : null;

            LastUpdated = allEntries
                .Select(static media => media.UserUpdatedAt)
                .Where(static updated => updated.HasValue)
                .Select(static updated => updated!.Value)
                .DefaultIfEmpty()
                .Max();

            HasData = true;
        }

        public void Reset()
        {
            TotalSeries = 0;
            CompletedSeries = 0;
            InProgressSeries = 0;
            PlannedSeries = 0;
            PausedSeries = 0;
            DroppedSeries = 0;
            ChaptersRead = 0;
            AverageScore = null;
            LastUpdated = null;
            HasData = false;
        }

        private static int CountByStatus(IEnumerable<AniListMedia> items, AniListMediaListStatus status)
        {
            return items.Count(media => media.UserStatus == status);
        }
    }
}