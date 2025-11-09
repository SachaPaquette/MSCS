using MSCS.Commands;
using MSCS.Enums;
using MSCS.Helpers;
using MSCS.Interfaces;
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

        private readonly IAniListService _aniListService;
        private readonly CancellationTokenSource _cts = new();
        private readonly Dictionary<AniListRecommendationCategory, Task<bool>> _categoryLoadTasks = new();
        private readonly HashSet<AniListRecommendationCategory> _loadedCategories = new();
        private readonly object _categoryLoadLock = new();

        private bool _isLoading;
        private string? _statusMessage;
        private bool _disposed;
        private int _mangaItemsToLoad = DefaultItemsPerCategory;
        private int _manhwaItemsToLoad = DefaultItemsPerCategory;
        private int _trendingItemsToLoad = DefaultItemsPerCategory;
        private int _newReleasesItemsToLoad = DefaultItemsPerCategory;
        private int _staffPicksItemsToLoad = DefaultItemsPerCategory;
        public AniListRecommendationsViewModel(IAniListService aniListService)
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
            StatusMessage = "Fetching trending AniList recommendations...";

            ResetDeferredCategories();

            try
            {
                var trendingLoaded = await EnsureCategoryLoadedAsyncInternal(AniListRecommendationCategory.Trending, true).ConfigureAwait(true);
                var staffLoaded = await EnsureCategoryLoadedAsyncInternal(AniListRecommendationCategory.StaffPicks, true).ConfigureAwait(true);

                if (!trendingLoaded && !staffLoaded)
                {
                    StatusMessage = "Unable to load recommendations right now.";
                }
                else
                {
                    StatusMessage = HasRecommendations
                        ? "Expand a category to load more recommendations."
                        : "No recommendations available right now.";
                }
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


        private void ResetDeferredCategories()
        {
            lock (_categoryLoadLock)
            {
                _categoryLoadTasks.Clear();
                _loadedCategories.Clear();
            }

            UpdateCollection(Trending, Array.Empty<AniListMedia>());
            UpdateCollection(TopManga, Array.Empty<AniListMedia>());
            UpdateCollection(TopManhwa, Array.Empty<AniListMedia>());
            UpdateCollection(NewReleases, Array.Empty<AniListMedia>());
            UpdateCollection(StaffPicks, Array.Empty<AniListMedia>());

            _trendingItemsToLoad = DefaultItemsPerCategory;
            _mangaItemsToLoad = DefaultItemsPerCategory;
            _manhwaItemsToLoad = DefaultItemsPerCategory;
            _newReleasesItemsToLoad = DefaultItemsPerCategory;
            _staffPicksItemsToLoad = DefaultItemsPerCategory;

            OnPropertyChanged(nameof(CanShowMoreTrending));
            OnPropertyChanged(nameof(CanShowMoreManga));
            OnPropertyChanged(nameof(CanShowMoreManhwa));
            OnPropertyChanged(nameof(CanShowMoreNewReleases));
            OnPropertyChanged(nameof(CanShowMoreStaffPicks));
            OnPropertyChanged(nameof(HasRecommendations));
        }

        private void UpdateCollection(ObservableCollection<AniListMedia> target, IReadOnlyList<AniListMedia> items)
        {
            target.Clear();
            foreach (var item in items)
            {
                target.Add(item);
            }
        }


        public Task<bool> EnsureCategoryLoadedAsync(AniListRecommendationCategory category)
        {
            return EnsureCategoryLoadedAsyncInternal(category, true);
        }

        private Task<bool> EnsureCategoryLoadedSilentlyAsync(AniListRecommendationCategory category)
        {
            return EnsureCategoryLoadedAsyncInternal(category, false);
        }

        private Task<bool> EnsureCategoryLoadedAsyncInternal(AniListRecommendationCategory category, bool showStatus)
        {
            Task<bool> loadTask;
            lock (_categoryLoadLock)
            {
                if (_loadedCategories.Contains(category))
                {
                    return Task.FromResult(true);
                }

                if (!_categoryLoadTasks.TryGetValue(category, out loadTask))
                {
                    var desiredCount = GetRequestedItemCount(category);
                    loadTask = LoadCategoryAsync(category, desiredCount, showStatus);
                    _categoryLoadTasks[category] = loadTask;
                }
                else if (showStatus)
                {
                    StatusMessage = GetLoadingMessage(category);
                }
            }

            return AwaitAndFinalizeLoadAsync(category, loadTask);
        }

        private async Task<bool> AwaitAndFinalizeLoadAsync(AniListRecommendationCategory category, Task<bool> loadTask)
        {
            var loaded = await loadTask.ConfigureAwait(true);

            lock (_categoryLoadLock)
            {
                if (loaded)
                {
                    _loadedCategories.Add(category);
                }

                _categoryLoadTasks.Remove(category);
            }

            return loaded;
        }


        private async Task ShowMoreAsync(AniListRecommendationCategory category)
        {
            if (_disposed)
            {
                return;
            }

            var baseLoaded = await EnsureCategoryLoadedSilentlyAsync(category).ConfigureAwait(true);
            if (!baseLoaded)
            {
                return;
            }

            var currentRequested = GetRequestedItemCount(category);
            if (currentRequested >= MaxItemsPerCategory)
            {
                return;
            }

            var desiredCount = Math.Min(currentRequested + ItemsIncrement, MaxItemsPerCategory);

            IsLoading = true;
            StatusMessage = GetLoadingMessage(category);

            try
            {
                await LoadCategoryAsync(category, desiredCount, true).ConfigureAwait(true);
            }
            finally
            {
                IsLoading = false;
                NotifyShowMoreState(category);
            }
        }

        private int GetRequestedItemCount(AniListRecommendationCategory category)
        {
            return category switch
            {
                AniListRecommendationCategory.Manga => _mangaItemsToLoad,
                AniListRecommendationCategory.Manhwa => _manhwaItemsToLoad,
                AniListRecommendationCategory.Trending => _trendingItemsToLoad,
                AniListRecommendationCategory.NewReleases => _newReleasesItemsToLoad,
                AniListRecommendationCategory.StaffPicks => _staffPicksItemsToLoad,
                _ => DefaultItemsPerCategory
            };
        }

        private async Task<bool> LoadCategoryAsync(AniListRecommendationCategory category, int desiredCount, bool showStatus)
        {
            if (_disposed)
            {
                return false;
            }

            if (showStatus)
            {
                StatusMessage = GetLoadingMessage(category);
            }

            try
            {
                var items = await _aniListService.GetTopSeriesAsync(category, desiredCount, _cts.Token).ConfigureAwait(true);
                UpdateCollection(GetCollection(category), items);
                UpdateRequestedCount(category, desiredCount);
                OnPropertyChanged(nameof(HasRecommendations));

                if (showStatus)
                {
                    StatusMessage = HasRecommendations
                        ? "Expand a category to load more recommendations."
                        : "No recommendations available right now.";
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load AniList {category}: {ex}");
                if (showStatus)
                {
                    StatusMessage = GetFailureMessage(category);
                }

                return false;
            }
        }

        private ObservableCollection<AniListMedia> GetCollection(AniListRecommendationCategory category)
        {
            return category switch
            {
                AniListRecommendationCategory.Manga => TopManga,
                AniListRecommendationCategory.Manhwa => TopManhwa,
                AniListRecommendationCategory.Trending => Trending,
                AniListRecommendationCategory.NewReleases => NewReleases,
                AniListRecommendationCategory.StaffPicks => StaffPicks,
                _ => TopManga
            };
        }

        private void UpdateRequestedCount(AniListRecommendationCategory category, int desiredCount)
        {
            switch (category)
            {
                case AniListRecommendationCategory.Manga:
                    _mangaItemsToLoad = desiredCount;
                    break;
                case AniListRecommendationCategory.Manhwa:
                    _manhwaItemsToLoad = desiredCount;
                    break;
                case AniListRecommendationCategory.Trending:
                    _trendingItemsToLoad = desiredCount;
                    break;
                case AniListRecommendationCategory.NewReleases:
                    _newReleasesItemsToLoad = desiredCount;
                    break;
                case AniListRecommendationCategory.StaffPicks:
                    _staffPicksItemsToLoad = desiredCount;
                    break;
            }

            NotifyShowMoreState(category);
        }

        private void NotifyShowMoreState(AniListRecommendationCategory category)
        {
            switch (category)
            {
                case AniListRecommendationCategory.Manga:
                    OnPropertyChanged(nameof(CanShowMoreManga));
                    break;
                case AniListRecommendationCategory.Manhwa:
                    OnPropertyChanged(nameof(CanShowMoreManhwa));
                    break;
                case AniListRecommendationCategory.Trending:
                    OnPropertyChanged(nameof(CanShowMoreTrending));
                    break;
                case AniListRecommendationCategory.NewReleases:
                    OnPropertyChanged(nameof(CanShowMoreNewReleases));
                    break;
                case AniListRecommendationCategory.StaffPicks:
                    OnPropertyChanged(nameof(CanShowMoreStaffPicks));
                    break;
            }
        }

        private static string GetLoadingMessage(AniListRecommendationCategory category)
        {
            return category switch
            {
                AniListRecommendationCategory.Manga => "Loading manga recommendations...",
                AniListRecommendationCategory.Manhwa => "Loading manhwa recommendations...",
                AniListRecommendationCategory.Trending => "Loading trending series...",
                AniListRecommendationCategory.NewReleases => "Loading new releases...",
                AniListRecommendationCategory.StaffPicks => "Loading staff picks...",
                _ => "Loading recommendations..."
            };
        }

        private static string GetFailureMessage(AniListRecommendationCategory category)
        {
            return category switch
            {
                AniListRecommendationCategory.Manga => "Unable to load manga recommendations right now.",
                AniListRecommendationCategory.Manhwa => "Unable to load manhwa recommendations right now.",
                AniListRecommendationCategory.Trending => "Unable to load trending series right now.",
                AniListRecommendationCategory.NewReleases => "Unable to load new releases right now.",
                AniListRecommendationCategory.StaffPicks => "Unable to load staff picks right now.",
                _ => "Unable to load recommendations right now."
            };
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

                var dialogViewModel = new TrackingWindowViewModel(
                    "Manage Tracking",
                    new ITrackingDialogViewModel[] { trackingViewModel });
                var dialog = new TrackingWindow(dialogViewModel);

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