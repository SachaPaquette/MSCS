using MSCS.Models;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;

namespace MSCS.ViewModels
{
    public partial class ReaderViewModel
    {
        private static readonly ObservableCollection<ReaderTrackingCoordinator.TrackingProvider> EmptyTrackingProviders = new();

        private void InitializeTrackingProviders()
        {
            _trackingCoordinator?.InitializeTrackingProviders();
        }

        private void OnTrackingCoordinatorStateChanged(object? sender, EventArgs e)
        {
            OnPropertyChanged(nameof(TrackingProviders));
            OnPropertyChanged(nameof(ActiveTrackingProvider));
            OnPropertyChanged(nameof(HasTrackingProviders));
            OnPropertyChanged(nameof(HasMultipleTrackingProviders));
            OnPropertyChanged(nameof(IsTrackingAvailable));
            OnPropertyChanged(nameof(ActiveTrackerName));
            OnPropertyChanged(nameof(IsTracked));
            OnPropertyChanged(nameof(TrackCommand));
            OnPropertyChanged(nameof(OpenInBrowserCommand));
            OnPropertyChanged(nameof(RemoveTrackingCommand));
            OnPropertyChanged(nameof(TrackButtonText));
            OnPropertyChanged(nameof(OpenTrackerButtonText));
            OnPropertyChanged(nameof(RemoveTrackerButtonText));
            OnPropertyChanged(nameof(TrackingStatusDisplay));
            OnPropertyChanged(nameof(TrackingProgressDisplay));
            OnPropertyChanged(nameof(TrackingScoreDisplay));
            OnPropertyChanged(nameof(TrackingUpdatedDisplay));
            OnPropertyChanged(nameof(CanOpenTracker));
            CommandManager.InvalidateRequerySuggested();
        }

        public ObservableCollection<ReaderTrackingCoordinator.TrackingProvider> TrackingProviders =>
            _trackingCoordinator?.TrackingProviders ?? EmptyTrackingProviders;

        public ReaderTrackingCoordinator.TrackingProvider? ActiveTrackingProvider
        {
            get => _trackingCoordinator?.ActiveProvider;
            set
            {
                if (_trackingCoordinator != null)
                {
                    _trackingCoordinator.ActiveProvider = value;
                }
            }
        }

        public bool HasTrackingProviders => _trackingCoordinator?.HasTrackingProviders ?? false;
        public bool HasMultipleTrackingProviders => _trackingCoordinator?.HasMultipleTrackingProviders ?? false;
        public bool IsTrackingAvailable => _trackingCoordinator?.IsTrackingAvailable ?? false;
        public string? ActiveTrackerName => _trackingCoordinator?.ActiveTrackerName;
        public bool IsTracked => _trackingCoordinator?.IsTracked ?? false;
        public ICommand TrackCommand => _trackingCoordinator?.TrackCommand ?? DisabledCommand;
        public ICommand OpenInBrowserCommand => _trackingCoordinator?.OpenInBrowserCommand ?? DisabledCommand;
        public ICommand RemoveTrackingCommand => _trackingCoordinator?.RemoveTrackingCommand ?? DisabledCommand;
        public string TrackButtonText => _trackingCoordinator?.TrackButtonText ?? "Track";
        public string OpenTrackerButtonText => _trackingCoordinator?.OpenTrackerButtonText ?? "Open tracker";
        public string RemoveTrackerButtonText => _trackingCoordinator?.RemoveTrackerButtonText ?? "Remove tracking";
        public string? TrackingStatusDisplay => _trackingCoordinator?.TrackingStatusDisplay;
        public string? TrackingProgressDisplay => _trackingCoordinator?.TrackingProgressDisplay;
        public string? TrackingScoreDisplay => _trackingCoordinator?.TrackingScoreDisplay;
        public string? TrackingUpdatedDisplay => _trackingCoordinator?.TrackingUpdatedDisplay;
        public bool CanOpenTracker => _trackingCoordinator?.CanOpenTracker ?? false;

        private Task UpdateTrackingProgressAsync()
        {
            return _trackingCoordinator?.UpdateTrackingProgressAsync() ?? Task.CompletedTask;
        }

        internal bool CanShowTrackingDialog(ReaderTrackingCoordinator.TrackingProvider? provider)
        {
            return _trackingCoordinator?.CanShowTrackingDialog(provider) ?? false;
        }

        internal Task ShowTrackingDialogAsync(ReaderTrackingCoordinator.TrackingProvider initiatingProvider)
        {
            return _trackingCoordinator?.ShowTrackingDialogAsync(initiatingProvider) ?? Task.CompletedTask;
        }

        internal int GetProgressForChapter(Chapter? chapter)
        {
            if (chapter == null)
            {
                return 0;
            }

            if (chapter.Number > 0)
            {
                var rounded = (int)Math.Round(chapter.Number, MidpointRounding.AwayFromZero);
                return Math.Max(1, rounded);
            }

            if (_chapterListViewModel != null)
            {
                var index = _chapterListViewModel.Chapters.IndexOf(chapter);
                if (index >= 0)
                {
                    return index + 1;
                }
            }

            return _currentChapterIndex + 1;
        }
    }
}