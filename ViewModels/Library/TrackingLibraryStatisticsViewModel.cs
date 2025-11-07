using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MSCS.Enums;
using MSCS.Models;

namespace MSCS.ViewModels.Library
{
    public class TrackingLibraryStatisticsViewModel : BaseViewModel
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

        public void Update(TrackingLibraryStatisticsSummary summary)
        {
            TotalSeries = summary.TotalSeries;
            CompletedSeries = summary.CompletedSeries;
            InProgressSeries = summary.InProgressSeries;
            PlannedSeries = summary.PlannedSeries;
            PausedSeries = summary.PausedSeries;
            DroppedSeries = summary.DroppedSeries;
            ChaptersRead = summary.ChaptersRead;
            AverageScore = summary.AverageScore;
            LastUpdated = summary.LastUpdated;
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

        public readonly struct TrackingLibraryStatisticsSummary
        {
            public TrackingLibraryStatisticsSummary(
                int totalSeries,
                int completedSeries,
                int inProgressSeries,
                int plannedSeries,
                int pausedSeries,
                int droppedSeries,
                int chaptersRead,
                double? averageScore,
                DateTimeOffset? lastUpdated)
            {
                TotalSeries = totalSeries;
                CompletedSeries = completedSeries;
                InProgressSeries = inProgressSeries;
                PlannedSeries = plannedSeries;
                PausedSeries = pausedSeries;
                DroppedSeries = droppedSeries;
                ChaptersRead = chaptersRead;
                AverageScore = averageScore;
                LastUpdated = lastUpdated;
            }

            public int TotalSeries { get; }
            public int CompletedSeries { get; }
            public int InProgressSeries { get; }
            public int PlannedSeries { get; }
            public int PausedSeries { get; }
            public int DroppedSeries { get; }
            public int ChaptersRead { get; }
            public double? AverageScore { get; }
            public DateTimeOffset? LastUpdated { get; }
        }
    }
}