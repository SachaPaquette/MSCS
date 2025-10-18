using MSCS.Commands;
using MSCS.Enums;
using MSCS.Interfaces;
using MSCS.Models;
using MSCS.Services.MyAnimeList;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using MessageBox = System.Windows.MessageBox;

namespace MSCS.ViewModels
{
    public class MyAnimeListTrackingViewModel : BaseViewModel, ITrackingDialogViewModel
    {
        private readonly MyAnimeListService _service;
        private readonly string _seriesTitle;
        private readonly CancellationTokenSource _cts = new();
        private readonly MyAnimeListTrackingInfo? _existingTracking;
        private readonly int? _suggestedProgress;
        private MyAnimeListMedia? _selectedMedia;
        private string _searchQuery;
        private bool _isBusy;
        private string? _statusMessage;
        private bool _disposed;
        private MyAnimeListStatus _selectedStatus;
        private bool _useProgress;
        private string _progressText;
        private bool _useScore;
        private double _score;

        public MyAnimeListTrackingViewModel(
            MyAnimeListService service,
            string seriesTitle,
            string initialQuery,
            MyAnimeListTrackingInfo? existingTracking,
            int? suggestedProgress)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _seriesTitle = seriesTitle ?? throw new ArgumentNullException(nameof(seriesTitle));
            _existingTracking = existingTracking;
            _suggestedProgress = suggestedProgress;
            _searchQuery = initialQuery ?? string.Empty;

            Results = new ObservableCollection<MyAnimeListMedia>();
            StatusOptions = BuildStatusOptions();
            _selectedStatus = existingTracking?.Status ?? MyAnimeListStatus.Reading;

            if (existingTracking?.Progress is > 0)
            {
                _useProgress = true;
                _progressText = existingTracking.Progress.Value.ToString(CultureInfo.CurrentCulture);
            }
            else if (suggestedProgress.HasValue)
            {
                _useProgress = true;
                _progressText = suggestedProgress.Value.ToString(CultureInfo.CurrentCulture);
            }
            else
            {
                _useProgress = false;
                _progressText = string.Empty;
            }

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

        public string ProviderId => _service.ServiceId;

        public string DisplayName => _service.DisplayName;

        public ObservableCollection<MyAnimeListMedia> Results { get; }

        public IReadOnlyList<StatusOption> StatusOptions { get; }

        public MyAnimeListMedia? SelectedMedia
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

        public MyAnimeListTrackingInfo? TrackingInfo { get; private set; }

        public MyAnimeListStatus SelectedStatus
        {
            get => _selectedStatus;
            set => SetProperty(ref _selectedStatus, value);
        }

        public bool UseProgress
        {
            get => _useProgress;
            set => SetProperty(ref _useProgress, value);
        }

        public string ProgressText
        {
            get => _progressText;
            set => SetProperty(ref _progressText, value);
        }

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
                var clamped = Math.Clamp(value, 0, 10);
                if (SetProperty(ref _score, clamped))
                {
                    OnPropertyChanged(nameof(ScoreDisplay));
                }
            }
        }

        public string ScoreDisplay => Score.ToString("0.#", CultureInfo.CurrentCulture);

        public string? ExistingEntrySummary
        {
            get
            {
                if (_existingTracking == null)
                {
                    return null;
                }

                var parts = new List<string>();
                parts.Add(ToDisplayString(_existingTracking.Status));

                if (_existingTracking.Progress is > 0)
                {
                    var total = _existingTracking.TotalChapters;
                    parts.Add(total.HasValue
                        ? $"Progress {_existingTracking.Progress}/{total}"
                        : $"Progress {_existingTracking.Progress}");
                }

                if (_existingTracking.Score is > 0)
                {
                    parts.Add($"Score {_existingTracking.Score:0.#}");
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
                StatusMessage = "Searching MyAnimeList...";
                Results.Clear();

                var results = await _service.SearchSeriesAsync(SearchQuery, _cts.Token).ConfigureAwait(true);
                foreach (var media in results)
                {
                    Results.Add(media);
                }

                if (Results.Count == 0)
                {
                    SelectedMedia = null;
                    StatusMessage = "No results found.";
                }
                else
                {
                    StatusMessage = null;
                    var match = Results.FirstOrDefault(r => _existingTracking != null && r.Id == _existingTracking.MediaId)
                        ?? Results.FirstOrDefault(r => string.Equals(r.Title, _seriesTitle, StringComparison.OrdinalIgnoreCase))
                        ?? Results.FirstOrDefault();
                    SelectedMedia = match;
                }
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

            if (UseProgress && GetProgressValue() is null && !string.IsNullOrWhiteSpace(ProgressText))
            {
                StatusMessage = "Enter a valid progress value.";
                return;
            }

            try
            {
                IsBusy = true;
                StatusMessage = "Saving tracking...";

                var desiredProgress = GetProgressValue();
                var desiredScore = UseScore ? Score : (double?)null;
                MyAnimeListTrackingInfo? info;

                if (_existingTracking != null && SelectedMedia.Id == _existingTracking.MediaId)
                {
                    info = await _service
                        .UpdateTrackingAsync(
                            _seriesTitle,
                            SelectedStatus,
                            desiredProgress,
                            desiredScore,
                            _cts.Token)
                        .ConfigureAwait(true);

                    info ??= _existingTracking.With(
                        status: SelectedStatus,
                        progress: desiredProgress,
                        score: desiredScore,
                        totalChapters: SelectedMedia.Chapters,
                        coverImageUrl: SelectedMedia.CoverImageUrl,
                        siteUrl: SelectedMedia.SiteUrl,
                        updatedAt: DateTimeOffset.UtcNow);
                }
                else
                {
                    info = await _service
                        .TrackSeriesAsync(
                            _seriesTitle,
                            SelectedMedia,
                            SelectedStatus,
                            desiredProgress,
                            desiredScore,
                            _cts.Token)
                        .ConfigureAwait(true);
                }

                TrackingInfo = info;
                StatusMessage = null;
                CloseRequested?.Invoke(this, true);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Operation cancelled.";
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
                MessageBox.Show(ex.Message, "MyAnimeList", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private int? GetProgressValue()
        {
            if (!UseProgress)
            {
                return null;
            }

            if (int.TryParse(ProgressText, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) && value > 0)
            {
                return value;
            }

            return null;
        }

        private void UpdateSelectionDefaults()
        {
            if (SelectedMedia == null)
            {
                return;
            }

            if (_existingTracking?.MediaId == SelectedMedia.Id)
            {
                SelectedStatus = _existingTracking.Status;
                if (_existingTracking.Progress is > 0)
                {
                    UseProgress = true;
                    ProgressText = _existingTracking.Progress.Value.ToString(CultureInfo.CurrentCulture);
                }

                if (_existingTracking.Score is > 0)
                {
                    UseScore = true;
                    Score = _existingTracking.Score.Value;
                }
            }
            else
            {
                if (_suggestedProgress.HasValue)
                {
                    UseProgress = true;
                    ProgressText = _suggestedProgress.Value.ToString(CultureInfo.CurrentCulture);
                }

                if (SelectedMedia.Score is > 0)
                {
                    UseScore = true;
                    Score = SelectedMedia.Score.Value;
                }
            }

            OnPropertyChanged(nameof(ScoreDisplay));
        }

        private static IReadOnlyList<StatusOption> BuildStatusOptions()
        {
            var statuses = Enum.GetValues<MyAnimeListStatus>();
            var options = new List<StatusOption>(statuses.Length);
            foreach (var status in statuses)
            {
                options.Add(new StatusOption(status, ToDisplayString(status)));
            }

            return options;
        }

        private static string ToDisplayString(MyAnimeListStatus status) => status switch
        {
            MyAnimeListStatus.PlanToRead => "Plan to read",
            MyAnimeListStatus.OnHold => "On hold",
            _ => status.ToString()
        };

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

        public sealed record StatusOption(MyAnimeListStatus Value, string Display);
    }
}