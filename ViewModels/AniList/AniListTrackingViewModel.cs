using MSCS.Commands;
using MSCS.Enums;
using MSCS.Helpers;
using MSCS.Interfaces;
using MSCS.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace MSCS.ViewModels
{
    public class AniListTrackingViewModel : BaseViewModel, IDisposable
    {
        private readonly IAniListService _aniListService;
        private readonly string _mangaTitle;
        private readonly CancellationTokenSource _cts = new();
        private readonly AniListTrackingInfo? _existingTracking;
        private readonly int? _suggestedProgress;
        private AniListMedia? _selectedMedia;
        private string _searchQuery;
        private bool _isBusy;
        private string? _statusMessage;
        private bool _disposed;
        private AniListMediaListStatus _selectedStatus;
        private bool _applySuggestedProgress;
        private bool _useScore;
        private double _score;

        public AniListTrackingViewModel(
            IAniListService aniListService,
            string mangaTitle,
            string initialQuery,
            AniListTrackingInfo? existingTracking,
            int? suggestedProgress)
        {
            _aniListService = aniListService ?? throw new ArgumentNullException(nameof(aniListService));
            _mangaTitle = mangaTitle ?? throw new ArgumentNullException(nameof(mangaTitle));
            _existingTracking = existingTracking;
            _suggestedProgress = suggestedProgress;
            _searchQuery = initialQuery ?? string.Empty;

            Results = new ObservableCollection<AniListMedia>();
            StatusOptions = BuildStatusOptions();
            _selectedStatus = existingTracking?.Status ?? AniListMediaListStatus.Current;
            _applySuggestedProgress = suggestedProgress.HasValue && (existingTracking?.Progress ?? 0) < suggestedProgress.Value;
            if (existingTracking?.Score is > 0)
            {
                _useScore = true;
                _score = existingTracking.Score.Value;
            }
            else
            {
                _useScore = false;
                _score = 0;
            }

            SearchCommand = new AsyncRelayCommand(_ => SearchAsync(), _ => !IsBusy && !string.IsNullOrWhiteSpace(SearchQuery));
            ConfirmCommand = new AsyncRelayCommand(_ => ConfirmAsync(), _ => !IsBusy && SelectedMedia != null);
            CancelCommand = new RelayCommand(_ => CloseRequested?.Invoke(this, false));

            if (!string.IsNullOrWhiteSpace(initialQuery))
            {
                _ = SearchAsync();
            }
        }

        public ObservableCollection<AniListMedia> Results { get; }

        public IReadOnlyList<StatusOption> StatusOptions { get; }

        public AniListMedia? SelectedMedia
        {
            get => _selectedMedia;
            set
            {
                if (SetProperty(ref _selectedMedia, value))
                {
                    UpdateSelectionDefaults();
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }


        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (SetProperty(ref _searchQuery, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string? StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public AniListTrackingInfo? TrackingInfo { get; private set; }

        public AniListMediaListStatus SelectedStatus
        {
            get => _selectedStatus;
            set
            {
                if (SetProperty(ref _selectedStatus, value))
                {
                    OnPropertyChanged(nameof(SelectedStatusDisplay));
                }
            }
        }

        public string SelectedStatusDisplay => SelectedStatus.ToDisplayString();

        public bool CanApplySuggestedProgress => _suggestedProgress.HasValue;

        public bool ApplySuggestedProgress
        {
            get => _applySuggestedProgress;
            set => SetProperty(ref _applySuggestedProgress, value);
        }

        public int? SuggestedProgress => _suggestedProgress;

        public bool UseScore
        {
            get => _useScore;
            set
            {
                if (SetProperty(ref _useScore, value))
                {
                    OnPropertyChanged(nameof(ScoreDisplay));
                }
            }
        }

        public double Score
        {
            get => _score;
            set
            {
                if (SetProperty(ref _score, Math.Clamp(value, 0, 100)))
                {
                    OnPropertyChanged(nameof(ScoreDisplay));
                }
            }
        }

        public string ScoreDisplay => Score.ToString("0", System.Globalization.CultureInfo.CurrentCulture);

        public string? ExistingEntrySummary
        {
            get
            {
                if (_existingTracking == null)
                {
                    return null;
                }

                var parts = new List<string>();
                if (_existingTracking.Status != null)
                {
                    parts.Add(_existingTracking.Status.Value.ToDisplayString());
                }

                if (_existingTracking.Progress is > 0)
                {
                    var total = _existingTracking.TotalChapters;
                    var progressText = total.HasValue
                        ? $"Progress {_existingTracking.Progress}/{total}"
                        : $"Progress {_existingTracking.Progress}";
                    parts.Add(progressText);
                }

                if (_existingTracking.Score is > 0)
                {
                    parts.Add($"Score {_existingTracking.Score:0}");
                }

                return parts.Count == 0 ? null : string.Join(" • ", parts);
            }
        }
        public ICommand SearchCommand { get; }
        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }

        public event EventHandler<bool>? CloseRequested;

        private async Task SearchAsync()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery))
            {
                return;
            }

            try
            {
                IsBusy = true;
                StatusMessage = "Searching AniList...";
                Results.Clear();
                var results = await _aniListService.SearchSeriesAsync(SearchQuery, _cts.Token).ConfigureAwait(true);
                foreach (var media in results)
                {
                    Results.Add(media);
                }

                StatusMessage = Results.Count == 0 ? "No results found." : null;
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Search cancelled.";
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ConfirmAsync()
        {
            if (SelectedMedia == null)
            {
                return;
            }

            try
            {
                IsBusy = true;
                StatusMessage = "Saving tracking...";
                var desiredProgress = ApplySuggestedProgress ? _suggestedProgress : null;
                var desiredScore = UseScore ? Score : (double?)null;
                TrackingInfo = await _aniListService.TrackSeriesAsync(
                    _mangaTitle,
                    SelectedMedia,
                    SelectedStatus,
                    desiredProgress,
                    desiredScore,
                    _cts.Token).ConfigureAwait(true);
                CloseRequested?.Invoke(this, true);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Operation cancelled.";
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
                System.Windows.MessageBox.Show(ex.Message, "AniList", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }


        private void UpdateSelectionDefaults()
        {
            if (SelectedMedia == null)
            {
                return;
            }

            var statusFromSelection = SelectedMedia.UserStatus ?? _existingTracking?.Status;
            if (statusFromSelection.HasValue)
            {
                SelectedStatus = statusFromSelection.Value;
            }

            if (SelectedMedia.UserScore is > 0)
            {
                UseScore = true;
                Score = SelectedMedia.UserScore.Value;
            }
            else if (_existingTracking?.Score is > 0)
            {
                UseScore = true;
                Score = _existingTracking.Score.Value;
            }
            else if (!UseScore)
            {
                Score = 0;
            }

            if (SelectedMedia.UserProgress.HasValue && _suggestedProgress.HasValue)
            {
                ApplySuggestedProgress = SelectedMedia.UserProgress.Value < _suggestedProgress.Value;
            }
            else if (!_suggestedProgress.HasValue)
            {
                ApplySuggestedProgress = false;
            }

            OnPropertyChanged(nameof(SelectedStatusDisplay));
            OnPropertyChanged(nameof(ScoreDisplay));
        }

        private static IReadOnlyList<StatusOption> BuildStatusOptions()
        {
            var statuses = Enum.GetValues<AniListMediaListStatus>();
            var options = new List<StatusOption>(statuses.Length);
            foreach (var status in statuses)
            {
                options.Add(new StatusOption(status, status.ToDisplayString()));
            }

            return options;
        }


        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _cts.Cancel();
            _cts.Dispose();
        }

        public sealed record StatusOption(AniListMediaListStatus Value, string Display);

    }
}