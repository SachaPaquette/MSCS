using MSCS.Commands;
using MSCS.Enums;
using MSCS.Interfaces;
using MSCS.Models;
using MSCS.Views;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace MSCS.ViewModels
{
    public partial class ReaderViewModel
    {
        private void InitializeAniListIntegration()
        {
            AniListTrackCommand = new AsyncRelayCommand(TrackWithAniListAsync, () => _aniListService != null);
            AniListOpenInBrowserCommand = new RelayCommand(_ => OpenAniListInBrowser(), _ => CanOpenAniList);
            AniListRemoveTrackingCommand = new AsyncRelayCommand(RemoveAniListTrackingAsync, () => _aniListService != null && TrackingInfo != null);
            OnPropertyChanged(nameof(AniListTrackCommand));
            OnPropertyChanged(nameof(AniListOpenInBrowserCommand));
            OnPropertyChanged(nameof(AniListRemoveTrackingCommand));
            OnPropertyChanged(nameof(IsAniListAvailable));

            if (_aniListService != null && !string.IsNullOrWhiteSpace(MangaTitle))
            {
                WeakEventManager<IAniListService, AniListTrackingChangedEventArgs>.AddHandler(_aniListService, nameof(IAniListService.TrackingChanged), OnAniListTrackingChanged); if (_aniListService.TryGetTracking(MangaTitle, out var info))
                {
                    TrackingInfo = info;
                }
                else
                {
                    NotifyAniListProperties();
                }

                _ = RefreshAniListTrackingAsync();
            }
            else
            {
                NotifyAniListProperties();
            }
        }

        private void OnAniListTrackingChanged(object? sender, AniListTrackingChangedEventArgs e)
        {

            var matchesTitle = !string.IsNullOrEmpty(e.MangaTitle) &&
                                string.Equals(e.MangaTitle, MangaTitle, StringComparison.OrdinalIgnoreCase);
            var matchesMediaId = e.MediaId != 0 && TrackingInfo?.MediaId == e.MediaId;

            if (!matchesTitle && !matchesMediaId && !string.IsNullOrEmpty(e.MangaTitle))
            {
                return;
            }

            if (string.IsNullOrEmpty(e.MangaTitle) && !matchesMediaId)
            {
                if (_aniListService.TryGetTracking(MangaTitle, out var refreshed))
                {
                    TrackingInfo = refreshed;
                }
                else
                {
                    TrackingInfo = null;
                    NotifyAniListProperties();
                }

                return;
            }

            if (e.TrackingInfo != null)
            {
                TrackingInfo = e.TrackingInfo;
            }
            else if (_aniListService.TryGetTracking(MangaTitle, out var info))
            {
                TrackingInfo = info;
            }
            else
            {
                TrackingInfo = null;
                NotifyAniListProperties();
            }
        }

        private async Task TrackWithAniListAsync()
        {
            if (_aniListService == null)
            {
                System.Windows.MessageBox.Show("AniList service is not available.", "AniList", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(MangaTitle))
            {
                System.Windows.MessageBox.Show("Unable to determine the manga title for tracking.", "AniList", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!_aniListService.IsAuthenticated)
            {
                System.Windows.MessageBox.Show("Connect your AniList account from the Settings tab before tracking a series.", "AniList", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var suggestedProgress = GetProgressForChapter(SelectedChapter);
            var initialQuery = TrackingInfo?.Title ?? MangaTitle;
            var trackingViewModel = new AniListTrackingViewModel(
                _aniListService,
                MangaTitle,
                initialQuery,
                TrackingInfo,
                suggestedProgress > 0 ? suggestedProgress : null);
            var dialog = new AniListTrackingWindow(trackingViewModel);
            if (System.Windows.Application.Current?.MainWindow != null)
            {
                dialog.Owner = System.Windows.Application.Current.MainWindow;
            }

            var result = dialog.ShowDialog();
            if (result == true && trackingViewModel.TrackingInfo != null)
            {
                TrackingInfo = trackingViewModel.TrackingInfo;
                await UpdateAniListProgressAsync().ConfigureAwait(true);
            }
        }

        private void OpenAniListInBrowser()
        {
            var url = TrackingInfo?.SiteUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(url)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Unable to open AniList: {ex.Message}", "AniList", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task RemoveAniListTrackingAsync()
        {
            if (_aniListService == null || TrackingInfo == null || string.IsNullOrWhiteSpace(MangaTitle))
            {
                return;
            }

            var confirmation = System.Windows.MessageBox.Show(
                "Remove AniList tracking for this series?",
                "AniList",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                var removed = await _aniListService.UntrackSeriesAsync(MangaTitle).ConfigureAwait(true);
                if (removed)
                {
                    TrackingInfo = null;
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Unable to remove AniList tracking: {ex.Message}", "AniList", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task RefreshAniListTrackingAsync()
        {
            if (_aniListService == null || string.IsNullOrWhiteSpace(MangaTitle))
            {
                return;
            }

            try
            {
                var refreshed = await _aniListService.RefreshTrackingAsync(MangaTitle).ConfigureAwait(true);
                if (refreshed != null)
                {
                    TrackingInfo = refreshed;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to refresh AniList tracking: {ex.Message}");
            }
        }

        private void NotifyAniListProperties()
        {
            OnPropertyChanged(nameof(IsAniListTracked));
            OnPropertyChanged(nameof(AniListButtonText));
            OnPropertyChanged(nameof(AniListStatusDisplay));
            OnPropertyChanged(nameof(AniListProgressDisplay));
            OnPropertyChanged(nameof(AniListScoreDisplay));
            OnPropertyChanged(nameof(AniListUpdatedDisplay));
            OnPropertyChanged(nameof(CanOpenAniList));
            CommandManager.InvalidateRequerySuggested();
        }

        private async Task UpdateAniListProgressAsync()
        {
            if (_aniListService == null || TrackingInfo == null || string.IsNullOrWhiteSpace(MangaTitle))
            {
                return;
            }

            var progress = GetProgressForChapter(SelectedChapter);
            if (progress <= 0)
            {
                return;
            }

            try
            {
                await _aniListService.UpdateProgressAsync(MangaTitle, progress).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to update AniList progress: {ex.Message}");
            }
        }

        private int GetProgressForChapter(Chapter? chapter)
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
                var idx = _chapterListViewModel.Chapters.IndexOf(chapter);
                if (idx >= 0)
                {
                    return idx + 1;
                }
            }

            return _currentChapterIndex + 1;
        }
    }
}