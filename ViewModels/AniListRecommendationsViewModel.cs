using MSCS.Commands;
using MSCS.Enums;
using MSCS.Helpers;
using MSCS.Models;
using MSCS.Services;
using MSCS.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace MSCS.ViewModels
{
    public class AniListRecommendationsViewModel : BaseViewModel, IDisposable
    {
        private const int DefaultItemsPerCategory = 12;
        private const int ItemsIncrement = 12;
        private const int MaxItemsPerCategory = 50;

        private readonly AniListService _aniListService;
        private readonly CancellationTokenSource _cts = new();
        private bool _isLoading;
        private string? _statusMessage;
        private bool _disposed;
        private int _mangaItemsToLoad = DefaultItemsPerCategory;
        private int _manhwaItemsToLoad = DefaultItemsPerCategory;
        private int _trendingItemsToLoad = DefaultItemsPerCategory;
        private int _newReleasesItemsToLoad = DefaultItemsPerCategory;
        private int _staffPicksItemsToLoad = DefaultItemsPerCategory;
        public AniListRecommendationsViewModel(AniListService aniListService)
        {
            _aniListService = aniListService ?? throw new ArgumentNullException(nameof(aniListService));

            TopManga = new ObservableCollection<AniListMedia>();
            TopManhwa = new ObservableCollection<AniListMedia>();
            Trending = new ObservableCollection<AniListMedia>();
            NewReleases = new ObservableCollection<AniListMedia>();
            StaffPicks = new ObservableCollection<AniListMedia>();

            RefreshCommand = new AsyncRelayCommand(_ => LoadRecommendationsAsync(), _ => !IsLoading);
            ShowMoreMangaCommand = new AsyncRelayCommand(_ => ShowMoreAsync(AniListRecommendationCategory.Manga), _ => CanShowMoreManga && !IsLoading);
            ShowMoreManhwaCommand = new AsyncRelayCommand(_ => ShowMoreAsync(AniListRecommendationCategory.Manhwa), _ => CanShowMoreManhwa && !IsLoading);
            ShowMoreTrendingCommand = new AsyncRelayCommand(_ => ShowMoreAsync(AniListRecommendationCategory.Trending), _ => CanShowMoreTrending && !IsLoading);
            ShowMoreNewReleasesCommand = new AsyncRelayCommand(_ => ShowMoreAsync(AniListRecommendationCategory.NewReleases), _ => CanShowMoreNewReleases && !IsLoading);
            ShowMoreStaffPicksCommand = new AsyncRelayCommand(_ => ShowMoreAsync(AniListRecommendationCategory.StaffPicks), _ => CanShowMoreStaffPicks && !IsLoading);
            OpenSeriesCommand = new RelayCommand(OpenSeries);
            ChangeStatusCommand = new AsyncRelayCommand(parameter => ChangeStatusAsync(parameter), _ => !IsLoading);
            EditTrackingCommand = new AsyncRelayCommand(parameter => OpenTrackingEditorAsync(parameter), _ => !IsLoading);

            _ = LoadRecommendationsAsync();
        }

        public ObservableCollection<AniListMedia> TopManga { get; }

        public ObservableCollection<AniListMedia> TopManhwa { get; }

        public ObservableCollection<AniListMedia> Trending { get; }

        public ObservableCollection<AniListMedia> NewReleases { get; }

        public ObservableCollection<AniListMedia> StaffPicks { get; }

        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (SetProperty(ref _isLoading, value))
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

        public ICommand RefreshCommand { get; }

        public ICommand ShowMoreMangaCommand { get; }

        public ICommand ShowMoreManhwaCommand { get; }
        public ICommand ShowMoreTrendingCommand { get; }

        public ICommand ShowMoreNewReleasesCommand { get; }

        public ICommand ShowMoreStaffPicksCommand { get; }

        public ICommand OpenSeriesCommand { get; }

        public ICommand ChangeStatusCommand { get; }

        public ICommand EditTrackingCommand { get; }

        public bool HasRecommendations =>
            TopManga.Count > 0 ||
            TopManhwa.Count > 0 ||
            Trending.Count > 0 ||
            NewReleases.Count > 0 ||
            StaffPicks.Count > 0;

        public bool CanShowMoreManga => TopManga.Count >= _mangaItemsToLoad && _mangaItemsToLoad < MaxItemsPerCategory;

        public bool CanShowMoreManhwa => TopManhwa.Count >= _manhwaItemsToLoad && _manhwaItemsToLoad < MaxItemsPerCategory;
        public bool CanShowMoreTrending => Trending.Count >= _trendingItemsToLoad && _trendingItemsToLoad < MaxItemsPerCategory;

        public bool CanShowMoreNewReleases => NewReleases.Count >= _newReleasesItemsToLoad && _newReleasesItemsToLoad < MaxItemsPerCategory;

        public bool CanShowMoreStaffPicks => StaffPicks.Count >= _staffPicksItemsToLoad && _staffPicksItemsToLoad < MaxItemsPerCategory;

        private async Task LoadRecommendationsAsync()
        {
            if (_disposed)
            {
                return;
            }

            IsLoading = true;
            StatusMessage = "Fetching AniList recommendations...";

            try
            {
                var trendingTask = _aniListService.GetTopSeriesAsync(AniListRecommendationCategory.Trending, _trendingItemsToLoad, _cts.Token);
                var mangaTask = _aniListService.GetTopSeriesAsync(AniListRecommendationCategory.Manga, _mangaItemsToLoad, _cts.Token);
                var manhwaTask = _aniListService.GetTopSeriesAsync(AniListRecommendationCategory.Manhwa, _manhwaItemsToLoad, _cts.Token);
                var newReleasesTask = _aniListService.GetTopSeriesAsync(AniListRecommendationCategory.NewReleases, _newReleasesItemsToLoad, _cts.Token);
                var staffPicksTask = _aniListService.GetTopSeriesAsync(AniListRecommendationCategory.StaffPicks, _staffPicksItemsToLoad, _cts.Token);

                var results = await Task.WhenAll(trendingTask, mangaTask, manhwaTask, newReleasesTask, staffPicksTask).ConfigureAwait(true);

                UpdateCollection(Trending, results[0]);
                UpdateCollection(TopManga, results[1]);
                UpdateCollection(TopManhwa, results[2]);
                UpdateCollection(NewReleases, results[3]);
                UpdateCollection(StaffPicks, results[4]);
                OnPropertyChanged(nameof(HasRecommendations));
                OnPropertyChanged(nameof(CanShowMoreTrending));
                OnPropertyChanged(nameof(CanShowMoreManga));
                OnPropertyChanged(nameof(CanShowMoreManhwa));
                OnPropertyChanged(nameof(CanShowMoreNewReleases));
                OnPropertyChanged(nameof(CanShowMoreStaffPicks));

                StatusMessage = HasRecommendations ? null : "No recommendations available right now.";
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation when the view model is disposed.
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load AniList recommendations: {ex}");
                StatusMessage = "Failed to load recommendations. Please try again.";
            }
            finally
            {
                IsLoading = false;
                OnPropertyChanged(nameof(HasRecommendations));
                OnPropertyChanged(nameof(CanShowMoreTrending));
                OnPropertyChanged(nameof(CanShowMoreManga));
                OnPropertyChanged(nameof(CanShowMoreManhwa));
                OnPropertyChanged(nameof(CanShowMoreNewReleases));
                OnPropertyChanged(nameof(CanShowMoreStaffPicks));
            }
        }

        private void UpdateCollection(ObservableCollection<AniListMedia> target, IReadOnlyList<AniListMedia> items)
        {
            target.Clear();
            foreach (var item in items)
            {
                target.Add(item);
            }
        }

        private async Task ShowMoreAsync(AniListRecommendationCategory category)
        {
            if (_disposed)
            {
                return;
            }

            var currentRequested = category switch
            {
                AniListRecommendationCategory.Manga => _mangaItemsToLoad,
                AniListRecommendationCategory.Manhwa => _manhwaItemsToLoad,
                AniListRecommendationCategory.Trending => _trendingItemsToLoad,
                AniListRecommendationCategory.NewReleases => _newReleasesItemsToLoad,
                AniListRecommendationCategory.StaffPicks => _staffPicksItemsToLoad,
                _ => DefaultItemsPerCategory
            };

            if (currentRequested >= MaxItemsPerCategory)
            {
                return;
            }

            var desiredCount = Math.Min(currentRequested + ItemsIncrement, MaxItemsPerCategory);

            IsLoading = true;
            StatusMessage = category switch
            {
                AniListRecommendationCategory.Manga => "Loading more manga recommendations...",
                AniListRecommendationCategory.Manhwa => "Loading more manhwa recommendations...",
                AniListRecommendationCategory.Trending => "Loading more trending series...",
                AniListRecommendationCategory.NewReleases => "Loading more new releases...",
                AniListRecommendationCategory.StaffPicks => "Loading more staff picks...",
                _ => "Loading more recommendations..."
            };

            try
            {
                var items = await _aniListService.GetTopSeriesAsync(category, desiredCount, _cts.Token).ConfigureAwait(true);

                var target = category switch
                {
                    AniListRecommendationCategory.Manga => TopManga,
                    AniListRecommendationCategory.Manhwa => TopManhwa,
                    AniListRecommendationCategory.Trending => Trending,
                    AniListRecommendationCategory.NewReleases => NewReleases,
                    AniListRecommendationCategory.StaffPicks => StaffPicks,
                    _ => TopManga
                };

                UpdateCollection(target, items);


                switch (category)
                {
                    case AniListRecommendationCategory.Manga:
                        _mangaItemsToLoad = desiredCount;
                        OnPropertyChanged(nameof(CanShowMoreManga));
                        break;
                    case AniListRecommendationCategory.Manhwa:
                        _manhwaItemsToLoad = desiredCount;
                        OnPropertyChanged(nameof(CanShowMoreManhwa));
                        break;
                    case AniListRecommendationCategory.Trending:
                        _trendingItemsToLoad = desiredCount;
                        OnPropertyChanged(nameof(CanShowMoreTrending));
                        break;
                    case AniListRecommendationCategory.NewReleases:
                        _newReleasesItemsToLoad = desiredCount;
                        OnPropertyChanged(nameof(CanShowMoreNewReleases));
                        break;
                    case AniListRecommendationCategory.StaffPicks:
                        _staffPicksItemsToLoad = desiredCount;
                        OnPropertyChanged(nameof(CanShowMoreStaffPicks));
                        break;
                }

                StatusMessage = HasRecommendations ? null : "No recommendations available right now.";
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation when the view model is disposed.
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load additional AniList recommendations: {ex}");
                StatusMessage = "Failed to load recommendations. Please try again.";
            }
            finally
            {
                IsLoading = false;
                OnPropertyChanged(nameof(HasRecommendations));
            }
        }

        private async Task ChangeStatusAsync(object? parameter)
        {
            AniListMedia? media = null;
            AniListMediaListStatus? status = null;

            switch (parameter)
            {
                case AniListStatusChangeParameter typedParameter:
                    media = typedParameter.Media;
                    status = typedParameter.Status;
                    break;
                case object[] values when values.Length >= 2 &&
                                         values[0] is AniListMedia mediaValue &&
                                         values[1] is AniListMediaListStatus statusValue:
                    media = mediaValue;
                    status = statusValue;
                    break;
            }

            if (media == null || status == null)
            {
                return;
            }

            if (!_aniListService.IsAuthenticated)
            {
                System.Windows.MessageBox.Show(
                    "Connect your AniList account from the Settings tab before editing your list.",
                    "AniList",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var trackingKey = GetTrackingKey(media);

            var updated = false;
            var resolvedStatus = status.Value;

            try
            {
                IsLoading = true;
                StatusMessage = $"Updating AniList status to {resolvedStatus.ToDisplayString()}...";

                await _aniListService.TrackSeriesAsync(
                    trackingKey,
                    media,
                    resolvedStatus,
                    null,
                    null,
                    _cts.Token).ConfigureAwait(true);

                updated = true;
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "AniList update cancelled.";
            }
            catch (InvalidOperationException ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "AniList", MessageBoxButton.OK, MessageBoxImage.Warning);
                StatusMessage = ex.Message;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to update AniList status: {ex}");
                System.Windows.MessageBox.Show(
                    "Unable to update the AniList status. Please try again.",
                    "AniList",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                StatusMessage = "Failed to update the AniList status.";
            }
            finally
            {
                IsLoading = false;
            }

            if (updated)
            {
                await LoadRecommendationsAsync().ConfigureAwait(true);
                StatusMessage = $"AniList status updated to {resolvedStatus.ToDisplayString()}.";
            }
        }

        private async Task OpenTrackingEditorAsync(object? parameter)
        {
            if (parameter is not AniListMedia media)
            {
                return;
            }

            if (!_aniListService.IsAuthenticated)
            {
                System.Windows.MessageBox.Show(
                    "Connect your AniList account from the Settings tab before editing your list.",
                    "AniList",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
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

                var dialog = new AniListTrackingWindow(trackingViewModel);
                if (System.Windows.Application.Current?.MainWindow != null)
                {
                    dialog.Owner = System.Windows.Application.Current.MainWindow;
                }

                var result = dialog.ShowDialog();
                if (result == true && trackingViewModel.TrackingInfo != null)
                {
                    StatusMessage = "Refreshing recommendations...";
                    await LoadRecommendationsAsync().ConfigureAwait(true);
                    StatusMessage = "AniList tracking updated.";
                }
            }
            catch (InvalidOperationException ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "AniList", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to open AniList tracking editor: {ex}");
                System.Windows.MessageBox.Show(
                    "Unable to open the AniList tracking editor right now.",
                    "AniList",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                trackingViewModel?.Dispose();
            }
        }

        private void OpenSeries(object? parameter)
        {
            if (parameter is not AniListMedia media || string.IsNullOrWhiteSpace(media.SiteUrl))
            {
                return;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = media.SiteUrl,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to open AniList entry: {ex}");
                StatusMessage = "Unable to open the AniList page.";
            }
        }

        private static string GetTrackingKey(AniListMedia media)
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
    }
}