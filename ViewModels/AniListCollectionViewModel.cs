using MSCS.Commands;
using MSCS.Enums;
using MSCS.Models;
using MSCS.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace MSCS.ViewModels
{
    public class AniListCollectionViewModel : BaseViewModel, IDisposable
    {
        private readonly AniListService _aniListService;
        private readonly ObservableCollection<AniListListSectionViewModel> _sections;
        private readonly CancellationTokenSource _cts = new();
        private CancellationTokenSource? _activeLoadCts;
        private bool _isLoading;
        private string? _statusMessage;
        private bool _disposed;

        public AniListCollectionViewModel(AniListService aniListService)
        {
            _aniListService = aniListService ?? throw new ArgumentNullException(nameof(aniListService));

            _sections = new ObservableCollection<AniListListSectionViewModel>(
                Enum.GetValues(typeof(AniListMediaListStatus))
                    .Cast<AniListMediaListStatus>()
                    .Select(status => new AniListListSectionViewModel(status)));

            Sections = new ReadOnlyObservableCollection<AniListListSectionViewModel>(_sections);
            Statistics = new AniListStatisticsViewModel();

            RefreshCommand = new AsyncRelayCommand(_ => LoadAsync(), _ => !IsLoading);
            OpenSeriesCommand = new RelayCommand(OpenSeries);

            _aniListService.AuthenticationChanged += OnAniListStateChanged;
            _aniListService.TrackingChanged += OnAniListStateChanged;

            _ = LoadAsync();
        }

        public ReadOnlyObservableCollection<AniListListSectionViewModel> Sections { get; }
        public AniListStatisticsViewModel Statistics { get; }

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

        public bool IsAuthenticated => _aniListService.IsAuthenticated;

        public string? UserName => _aniListService.UserName;

        public ICommand RefreshCommand { get; }

        public ICommand OpenSeriesCommand { get; }

        public bool HasAnySeries => _sections.Any(section => section.HasItems);

        private async Task LoadAsync()
        {
            if (_disposed)
            {
                return;
            }

            if (!_aniListService.IsAuthenticated)
            {
                CancelActiveLoad();
                ClearSections();
                StatusMessage = "Connect your AniList account from the Settings tab to view your library.";
                Statistics.Reset();
                OnPropertyChanged(nameof(IsAuthenticated));
                OnPropertyChanged(nameof(UserName));
                OnPropertyChanged(nameof(HasAnySeries));
                return;
            }

            CancelActiveLoad();

            var loadCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            _activeLoadCts = loadCts;

            IsLoading = true;
            StatusMessage = "Loading your AniList series...";

            try
            {
                var lists = await _aniListService.GetUserListsAsync(loadCts.Token).ConfigureAwait(true);
                UpdateSections(lists);
                StatusMessage = HasAnySeries ? null : "Your AniList library is currently empty.";
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellations triggered by refresh/dispose.
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load AniList library: {ex}");
                StatusMessage = "Unable to load your AniList library right now.";
            }
            finally
            {
                if (ReferenceEquals(_activeLoadCts, loadCts))
                {
                    _activeLoadCts.Dispose();
                    _activeLoadCts = null;
                }

                IsLoading = false;
                OnPropertyChanged(nameof(IsAuthenticated));
                OnPropertyChanged(nameof(UserName));
                OnPropertyChanged(nameof(HasAnySeries));
            }
        }

        private void UpdateSections(IReadOnlyDictionary<AniListMediaListStatus, IReadOnlyList<AniListMedia>> lists)
        {
            foreach (var section in _sections)
            {
                if (lists != null && lists.TryGetValue(section.Status, out var items) && items != null)
                {
                    section.ReplaceItems(items);
                }
                else
                {
                    section.ReplaceItems(Array.Empty<AniListMedia>());
                }
            }
            Statistics.Update(lists);
        }

        private void ClearSections()
        {
            foreach (var section in _sections)
            {
                section.ReplaceItems(Array.Empty<AniListMedia>());
            }
            Statistics.Reset();
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

        private void CancelActiveLoad()
        {
            var existing = Interlocked.Exchange(ref _activeLoadCts, null);
            if (existing != null)
            {
                existing.Cancel();
                existing.Dispose();
            }
        }

        private void OnAniListStateChanged(object? sender, EventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(() =>
                {
                    OnPropertyChanged(nameof(IsAuthenticated));
                    OnPropertyChanged(nameof(UserName));
                    _ = LoadAsync();
                }));
            }
            else
            {
                OnPropertyChanged(nameof(IsAuthenticated));
                OnPropertyChanged(nameof(UserName));
                _ = LoadAsync();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _aniListService.AuthenticationChanged -= OnAniListStateChanged;
            _aniListService.TrackingChanged -= OnAniListStateChanged;
            CancelActiveLoad();
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}