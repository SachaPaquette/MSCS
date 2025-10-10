using MSCS.Commands;
using MSCS.Enums;
using MSCS.Models;
using MSCS.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace MSCS.ViewModels
{
    public class AniListRecommendationsViewModel : BaseViewModel, IDisposable
    {
        private readonly AniListService _aniListService;
        private readonly CancellationTokenSource _cts = new();
        private bool _isLoading;
        private string? _statusMessage;
        private bool _disposed;

        public AniListRecommendationsViewModel(AniListService aniListService)
        {
            _aniListService = aniListService ?? throw new ArgumentNullException(nameof(aniListService));

            TopManga = new ObservableCollection<AniListMedia>();
            TopManhwa = new ObservableCollection<AniListMedia>();

            RefreshCommand = new AsyncRelayCommand(_ => LoadRecommendationsAsync(), _ => !IsLoading);
            OpenSeriesCommand = new RelayCommand(OpenSeries);

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

        public ICommand OpenSeriesCommand { get; }

        public bool HasRecommendations => TopManga.Count > 0 || TopManhwa.Count > 0;

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
                var mangaTask = _aniListService.GetTopSeriesAsync(AniListRecommendationCategory.Manga, 12, _cts.Token);
                var manhwaTask = _aniListService.GetTopSeriesAsync(AniListRecommendationCategory.Manhwa, 12, _cts.Token);
                var results = await Task.WhenAll(mangaTask, manhwaTask).ConfigureAwait(true);

                UpdateCollection(TopManga, results[0]);
                UpdateCollection(TopManhwa, results[1]);
                OnPropertyChanged(nameof(HasRecommendations));

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
            }
        }

        private static void UpdateCollection(ObservableCollection<AniListMedia> target, IReadOnlyList<AniListMedia> items)
        {
            target.Clear();
            foreach (var item in items)
            {
                target.Add(item);
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