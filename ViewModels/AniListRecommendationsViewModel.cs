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

        public AniListRecommendationsViewModel(AniListService aniListService)
        {
            _aniListService = aniListService ?? throw new ArgumentNullException(nameof(aniListService));

            TopManga = new ObservableCollection<AniListMedia>();
            TopManhwa = new ObservableCollection<AniListMedia>();

            RefreshCommand = new AsyncRelayCommand(_ => LoadRecommendationsAsync(), _ => !IsLoading);
            ShowMoreMangaCommand = new AsyncRelayCommand(_ => ShowMoreAsync(AniListRecommendationCategory.Manga), _ => CanShowMoreManga && !IsLoading);
            ShowMoreManhwaCommand = new AsyncRelayCommand(_ => ShowMoreAsync(AniListRecommendationCategory.Manhwa), _ => CanShowMoreManhwa && !IsLoading);
            OpenSeriesCommand = new RelayCommand(OpenSeries);
            ChangeStatusCommand = new AsyncRelayCommand(parameter => ChangeStatusAsync(parameter), _ => !IsLoading);
            EditTrackingCommand = new AsyncRelayCommand(parameter => OpenTrackingEditorAsync(parameter), _ => !IsLoading);

            _ = LoadRecommendationsAsync();
        }

        public ObservableCollection<AniListMedia> TopManga { get; }

        public ObservableCollection<AniListMedia> TopManhwa { get; }

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

        public ICommand OpenSeriesCommand { get; }

        public ICommand ChangeStatusCommand { get; }

        public ICommand EditTrackingCommand { get; }

        public bool HasRecommendations => TopManga.Count > 0 || TopManhwa.Count > 0;

        public bool CanShowMoreManga => TopManga.Count >= _mangaItemsToLoad && _mangaItemsToLoad < MaxItemsPerCategory;

        public bool CanShowMoreManhwa => TopManhwa.Count >= _manhwaItemsToLoad && _manhwaItemsToLoad < MaxItemsPerCategory;

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
                var mangaTask = _aniListService.GetTopSeriesAsync(AniListRecommendationCategory.Manga, _mangaItemsToLoad, _cts.Token);
                var manhwaTask = _aniListService.GetTopSeriesAsync(AniListRecommendationCategory.Manhwa, _manhwaItemsToLoad, _cts.Token);
                var results = await Task.WhenAll(mangaTask, manhwaTask).ConfigureAwait(true);

                UpdateCollection(TopManga, results[0]);
                UpdateCollection(TopManhwa, results[1]);
                OnPropertyChanged(nameof(HasRecommendations));
                OnPropertyChanged(nameof(CanShowMoreManga));
                OnPropertyChanged(nameof(CanShowMoreManhwa));

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
                OnPropertyChanged(nameof(CanShowMoreManga));
                OnPropertyChanged(nameof(CanShowMoreManhwa));
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

            var currentRequested = category == AniListRecommendationCategory.Manga ? _mangaItemsToLoad : _manhwaItemsToLoad;
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
                _ => "Loading more recommendations..."
            };

            try
            {
                var items = await _aniListService.GetTopSeriesAsync(category, desiredCount, _cts.Token).ConfigureAwait(true);

                var target = category == AniListRecommendationCategory.Manga ? TopManga : TopManhwa;
                UpdateCollection(target, items);

                if (category == AniListRecommendationCategory.Manga)
                {
                    _mangaItemsToLoad = desiredCount;
                    OnPropertyChanged(nameof(CanShowMoreManga));
                }
                else
                {
                    _manhwaItemsToLoad = desiredCount;
                    OnPropertyChanged(nameof(CanShowMoreManhwa));
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
            if (parameter is not object[] values || values.Length < 2 ||
                values[0] is not AniListMedia media ||
                values[1] is not AniListMediaListStatus status)
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

            try
            {
                IsLoading = true;
                StatusMessage = $"Updating AniList status to {status.ToDisplayString()}...";

                await _aniListService.TrackSeriesAsync(
                    trackingKey,
                    media,
                    status,
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
                StatusMessage = $"AniList status updated to {status.ToDisplayString()}.";
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