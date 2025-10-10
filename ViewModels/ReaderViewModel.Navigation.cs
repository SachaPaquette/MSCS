using MSCS.Commands;
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

namespace MSCS.ViewModels
{
    public partial class ReaderViewModel
    {
        private readonly SemaphoreSlim _chapterNavigationSemaphore = new(1, 1);

        private async Task<bool> TryMoveToChapterAsync(int newIndex)
        {
            if (_chapterListViewModel == null)
            {
                Debug.WriteLine("Chapter list view model is unavailable for navigation.");
                return false;
            }

            if (newIndex < 0 || newIndex >= _chapterListViewModel.Chapters.Count)
            {
                Debug.WriteLine($"Requested chapter index {newIndex} is out of range.");
                return false;
            }

            var acquired = false;
            try
            {
                await _chapterNavigationSemaphore.WaitAsync();
                acquired = true;

                if (_isChapterNavigationInProgress)
                {
                    Debug.WriteLine("A chapter navigation is already in progress.");
                    return false;
                }

                _isChapterNavigationInProgress = true;

                if (newIndex == _currentChapterIndex + 1)
                {
                    await UpdateAniListProgressAsync().ConfigureAwait(false);
                }

                var images = await _chapterListViewModel.GetChapterImagesAsync(newIndex);
                if (images == null || images.Count == 0)
                {
                    Debug.WriteLine($"No images returned for chapter at index {newIndex}.");
                    return false;
                }

                _currentChapterIndex = newIndex;
                ResetImages(images);
                _ = _chapterListViewModel.PrefetchChapterAsync(newIndex + 1);
                Debug.WriteLine($"Navigated to chapter {newIndex} with {images.Count} images.");
                CommandManager.InvalidateRequerySuggested();
                UpdateSelectedChapter(newIndex);
                if (!_isRestoringProgress)
                {
                    PersistReadingProgress(force: true);
                    ChapterChanged?.Invoke(this, EventArgs.Empty);
                }
                return true;
            }
            finally
            {
                _isChapterNavigationInProgress = false;
                if (acquired)
                {
                    _chapterNavigationSemaphore.Release();
                }
            }
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

            _ = LoadMoreImagesAsync();
        }

        private bool CanGoToNextChapter()
        {
            if (_chapterListViewModel == null)
            {
                return false;
            }

            return _currentChapterIndex + 1 < _chapterListViewModel.Chapters.Count;
        }

        public async Task GoToNextChapterAsync()
        {
            if (!CanGoToNextChapter())
            {
                return;
            }
            await TryMoveToChapterAsync(_currentChapterIndex + 1);
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

            OnPropertyChanged(nameof(GoBackCommand));
            OnPropertyChanged(nameof(GoHomeCommand));
            OnPropertyChanged(nameof(NextChapterCommand));
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
            }
        }
    }
}