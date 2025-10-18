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
            ChangeStatusCommand = new AsyncRelayCommand(parameter => ChangeStatusAsync(parameter), _ => !IsLoading);
            EditTrackingCommand = new AsyncRelayCommand(parameter => OpenTrackingEditorAsync(parameter), _ => !IsLoading);


            _aniListService.AuthenticationChanged += OnAniListAuthenticationChanged;
            _aniListService.TrackingChanged += OnAniListTrackingChanged;

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

        public ICommand ChangeStatusCommand { get; }

        public ICommand EditTrackingCommand { get; }

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
            var resolvedStatus = status.Value;
            var updated = false;

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

        private void CancelActiveLoad()
        {
            var existing = Interlocked.Exchange(ref _activeLoadCts, null);
            if (existing != null)
            {
                existing.Cancel();
                existing.Dispose();
            }
        }

        private void OnAniListAuthenticationChanged(object? sender, EventArgs e)
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


        private void OnAniListTrackingChanged(object? sender, AniListTrackingChangedEventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(() => HandleTrackingChanged(e)));
            }
            else
            {
                HandleTrackingChanged(e);
            }
        }

        private void HandleTrackingChanged(AniListTrackingChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.MangaTitle))
            {
                _ = LoadAsync();
                return;
            }

            ApplyTrackingUpdate(e);
        }

        private void ApplyTrackingUpdate(AniListTrackingChangedEventArgs e)
        {
            var info = e.TrackingInfo;
            if (info == null || !info.Status.HasValue)
            {
                if (RemoveMediaFromSections(e.MediaId))
                {
                    UpdateStatisticsFromSections();
                    OnPropertyChanged(nameof(HasAnySeries));
                }

                return;
            }

            var targetSection = _sections.FirstOrDefault(section => section.Status == info.Status.Value);
            if (targetSection == null)
            {
                return;
            }

            AniListListSectionViewModel? currentSection = null;
            AniListMedia? existingMedia = null;
            foreach (var section in _sections)
            {
                var match = section.Items.FirstOrDefault(media => media.Id == info.MediaId);
                if (match != null)
                {
                    currentSection = section;
                    existingMedia = match;
                    break;
                }
            }

            var updatedMedia = CreateUpdatedMedia(existingMedia, info);

            if (currentSection != null && currentSection != targetSection)
            {
                currentSection.RemoveById(info.MediaId);
            }

            targetSection.Upsert(updatedMedia);

            UpdateStatisticsFromSections();
            OnPropertyChanged(nameof(HasAnySeries));
        }

        private bool RemoveMediaFromSections(int mediaId)
        {
            var removed = false;
            foreach (var section in _sections)
            {
                removed |= section.RemoveById(mediaId);
            }

            return removed;
        }

        private static AniListMedia CreateUpdatedMedia(AniListMedia? existing, AniListTrackingInfo info)
        {
            return new AniListMedia
            {
                Id = info.MediaId,
                RomajiTitle = existing?.RomajiTitle ?? info.Title,
                EnglishTitle = existing?.EnglishTitle ?? info.Title,
                NativeTitle = existing?.NativeTitle ?? info.Title,
                Status = existing?.Status,
                CoverImageUrl = info.CoverImageUrl ?? existing?.CoverImageUrl,
                BannerImageUrl = existing?.BannerImageUrl,
                StartDateText = existing?.StartDateText,
                Format = existing?.Format,
                Chapters = info.TotalChapters ?? existing?.Chapters,
                SiteUrl = info.SiteUrl ?? existing?.SiteUrl,
                AverageScore = existing?.AverageScore,
                MeanScore = existing?.MeanScore,
                UserStatus = info.Status,
                UserProgress = info.Progress,
                UserScore = info.Score,
                UserUpdatedAt = info.UpdatedAt
            };
        }

        private void UpdateStatisticsFromSections()
        {
            var snapshot = _sections.ToDictionary(
                section => section.Status,
                section => (IReadOnlyList<AniListMedia>)section.Items.ToList());
            Statistics.Update(snapshot);
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
            _aniListService.AuthenticationChanged -= OnAniListAuthenticationChanged;
            _aniListService.TrackingChanged -= OnAniListTrackingChanged;
            CancelActiveLoad();
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}