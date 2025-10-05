using MSCS.Commands;
using MSCS.Interfaces;
using MSCS.Models;
using System;
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
        private AniListMedia? _selectedMedia;
        private string _searchQuery;
        private bool _isBusy;
        private string? _statusMessage;
        private bool _disposed;

        public AniListTrackingViewModel(IAniListService aniListService, string mangaTitle, string initialQuery)
        {
            _aniListService = aniListService ?? throw new ArgumentNullException(nameof(aniListService));
            _mangaTitle = mangaTitle ?? throw new ArgumentNullException(nameof(mangaTitle));
            _searchQuery = initialQuery ?? string.Empty;

            Results = new ObservableCollection<AniListMedia>();

            SearchCommand = new AsyncRelayCommand(_ => SearchAsync(), _ => !IsBusy && !string.IsNullOrWhiteSpace(SearchQuery));
            ConfirmCommand = new AsyncRelayCommand(_ => ConfirmAsync(), _ => !IsBusy && SelectedMedia != null);
            CancelCommand = new RelayCommand(_ => CloseRequested?.Invoke(this, false));

            if (!string.IsNullOrWhiteSpace(initialQuery))
            {
                _ = SearchAsync();
            }
        }

        public ObservableCollection<AniListMedia> Results { get; }

        public AniListMedia? SelectedMedia
        {
            get => _selectedMedia;
            set
            {
                if (SetProperty(ref _selectedMedia, value))
                {
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
                TrackingInfo = await _aniListService.TrackSeriesAsync(_mangaTitle, SelectedMedia, _cts.Token).ConfigureAwait(true);
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