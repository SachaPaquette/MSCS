using MSCS.Commands;
using MSCS.Helpers;
using MSCS.Interfaces;
using MSCS.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace MSCS.ViewModels
{
    public partial class ReaderViewModel
    {

        private async Task<bool> TryMoveToChapterAsync(int newIndex)
        {
            if (_chapterCoordinator == null)
            {
                Debug.WriteLine("Chapter coordinator is unavailable for navigation.");
                return false;
            }


            if (newIndex == _currentChapterIndex)
            {
                return true;
            }

            if (_currentChapterIndex >= 0)
            {
                await UpdateTrackingProgressAsync().ConfigureAwait(false);
            }

            var result = await _chapterCoordinator.MoveToChapterAsync(newIndex).ConfigureAwait(false);
            if (result == null)
            {
                var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
                dispatcher.Invoke(() =>
                {
                    MessageBox.Show(
                        Application.Current?.MainWindow,
                        "Unable to load the selected chapter. Please check your connection and try again.",
                        "Chapter navigation",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                });

                return false;
            }

            _currentChapterIndex = result.ChapterIndex;
            _chapterCoordinator.PrefetchImages(result.Images, 0, Math.Min(Constants.DefaultLoadedBatchSize * 2, result.Images.Count));
            ResetImages(result.Images);
            Debug.WriteLine($"Navigated to chapter {result.ChapterIndex} with {result.Images.Count} images.");
            CommandManager.InvalidateRequerySuggested();
            UpdateSelectedChapter(result.ChapterIndex);
            if (!_isRestoringProgress)
            {
                PersistReadingProgress(force: true);
                ChapterChanged?.Invoke(this, EventArgs.Empty);
            }

            return true;
        }

        private void ResetImages(IEnumerable<ChapterImage> images)
        {
            _imageLoadCts.Cancel();
            _imageLoadCts.Dispose();
            _imageLoadCts = new CancellationTokenSource();
            _pendingRestoreProgress = null;
            _pendingRestoreOffset = null;
            _queuedScrollRestoreRequest = null;

            var dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

            dispatcher.Invoke(() =>
            {
                _allImages.Clear();
                ImageUrls.Clear();
                _loadedCount = 0;
                SetProperty(ref _scrollProgress, 0.0, nameof(ScrollProgress));
                _lastKnownScrollOffset = 0;
                _lastKnownExtentHeight = 0;
                _lastKnownViewportHeight = 0;
                foreach (var img in images)
                {
                    _allImages.Add(img);
                }

                OnPropertyChanged(nameof(TotalImages));
                OnPropertyChanged(nameof(LoadedImages));
                OnPropertyChanged(nameof(RemainingImages));
                OnPropertyChanged(nameof(LoadingProgress));
            });

            _chapterCoordinator?.PrefetchImages(_allImages, 0, Math.Min(Constants.DefaultLoadedBatchSize, _allImages.Count));
            _ = LoadMoreImagesAsync();
        }

        private bool CanGoToNextChapter()
        {
            return _chapterCoordinator?.CanGoToNext(_currentChapterIndex) ?? false;
        }

        public async Task GoToNextChapterAsync()
        {
            if (!CanGoToNextChapter())
            {
                return;
            }
            await TryMoveToChapterAsync(_currentChapterIndex + 1);
        }

        private bool CanGoToPreviousChapter()
        {
            return _chapterCoordinator?.CanGoToPrevious(_currentChapterIndex) ?? false;
        }

        public async Task GoToPreviousChapterAsync()
        {
            if (!CanGoToPreviousChapter())
            {
                return;
            }

            await TryMoveToChapterAsync(_currentChapterIndex - 1);
        }

        private void InitializeNavigationCommands()
        {
            if (_navigationService == null)
            {
                GoBackCommand = new RelayCommand(_ => { }, _ => false);
                GoHomeCommand = new RelayCommand(_ => { }, _ => false);
            }
            else
            {
                GoBackCommand = new RelayCommand(_ =>
                {
                    PersistReadingProgress(force: true);
                    _navigationService.GoBack();
                }, _ => _navigationService.CanGoBack);
                GoHomeCommand = new RelayCommand(_ =>
                {
                    PersistReadingProgress(force: true);
                    _navigationService.NavigateToSingleton<MangaListViewModel>();
                });
                WeakEventManager<INavigationService, EventArgs>.AddHandler(_navigationService, nameof(INavigationService.CanGoBackChanged), OnNavigationCanGoBackChanged!);
            }

            NextChapterCommand = new AsyncRelayCommand(_ => GoToNextChapterAsync(), _ => CanGoToNextChapter());
            PreviousChapterCommand = new AsyncRelayCommand(_ => GoToPreviousChapterAsync(), _ => CanGoToPreviousChapter());

            OnPropertyChanged(nameof(GoBackCommand));
            OnPropertyChanged(nameof(GoHomeCommand));
            OnPropertyChanged(nameof(NextChapterCommand));
            OnPropertyChanged(nameof(PreviousChapterCommand));
        }

        private void OnNavigationCanGoBackChanged(object? sender, EventArgs e)
        {
            CommandManager.InvalidateRequerySuggested();
        }

        private async Task HandleSelectedChapterChangedAsync(Chapter? chapter)
        {
            if (chapter == null || _chapterListViewModel == null)
            {
                return;
            }

            try
            {
                int index = _chapterListViewModel.Chapters.IndexOf(chapter);
                if (index < 0 || index == _currentChapterIndex)
                {
                    return;
                }

                await TryMoveToChapterAsync(index);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to change chapter from sidebar: {ex.Message}");
            }
        }

        private void SelectInitialChapter()
        {
            if (_chapterListViewModel == null)
            {
                return;
            }

            if (_currentChapterIndex >= 0 && _currentChapterIndex < _chapterListViewModel.Chapters.Count)
            {
                _isUpdatingSelectedChapter = true;
                SelectedChapter = _chapterListViewModel.Chapters[_currentChapterIndex];
                ChapterTitle = SelectedChapter?.Title ?? ChapterTitle;
                _isUpdatingSelectedChapter = false;
            }
        }

        private void UpdateSelectedChapter(int index)
        {
            if (_chapterListViewModel == null)
            {
                return;
            }

            if (index >= 0 && index < _chapterListViewModel.Chapters.Count)
            {
                _isUpdatingSelectedChapter = true;
                SelectedChapter = _chapterListViewModel.Chapters[index];
                ChapterTitle = SelectedChapter?.Title ?? ChapterTitle;
                _isUpdatingSelectedChapter = false;
            }
        }

        private void ChapterListViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChapterListViewModel.Chapters) && _chapterListViewModel != null)
            {
                Chapters = _chapterListViewModel.Chapters;
                SelectInitialChapter();
                RestoreReadingProgress();
                _preferences.SetProfileKey(DetermineProfileKey());
            }
        }
    }
}