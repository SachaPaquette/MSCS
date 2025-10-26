using MSCS.Helpers;
using MSCS.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace MSCS.ViewModels
{
    public partial class ReaderViewModel
    {
        public async Task LoadMoreImagesAsync(int? desiredCount = null, CancellationToken cancellationToken = default)
        {
            if (_allImages.Count - _loadedCount <= 0)
            {
                return;
            }

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_imageLoadCts.Token, cancellationToken);
            var token = linkedCts.Token;
            bool acquired = false;
            try
            {
                await _imageLoadSemaphore.WaitAsync(token).ConfigureAwait(false);
                acquired = true;

                int remaining = _allImages.Count - _loadedCount;
                if (remaining <= 0)
                {
                    if (_isRestoringProgress)
                    {
                        var dispatch = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
                        await dispatch.InvokeAsync(NotifyImagesLoadedForRestore, DispatcherPriority.Background, token);
                    }
                    return;
                }

                int countToLoad = desiredCount.HasValue
                    ? Math.Min(remaining, Math.Max(1, desiredCount.Value))
                    : Math.Min(Constants.DefaultLoadedBatchSize, remaining);

                token.ThrowIfCancellationRequested();

                int startIndex = _loadedCount;
                var upcoming = Math.Min(
                    Constants.DefaultLoadedBatchSize * 2,
                    Math.Max(0, _allImages.Count - (startIndex + countToLoad)));
                _chapterCoordinator?.PrefetchImages(
                    _allImages,
                    Math.Max(0, startIndex),
                    countToLoad + upcoming,
                    _imageLoadCts.Token); var dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
                await dispatcher.InvokeAsync(() =>
                {
                    for (int offset = 0; offset < countToLoad; offset++)
                    {
                        ImageUrls.Add(_allImages[startIndex + offset]);
                    }

                    _loadedCount += countToLoad;
                    NotifyLoadingMetricsChanged();
                    if (_isRestoringProgress)
                    {
                        NotifyImagesLoadedForRestore();
                    }
                }, DispatcherPriority.Background, token);

                Debug.WriteLine($"Loaded {_loadedCount} / {_allImages.Count} images");
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Image loading cancelled.");
            }
            finally
            {
                if (acquired)
                {
                    _imageLoadSemaphore.Release();
                }
            }
        }

        private async Task EnsureImagesLoadedForProgressAsync(double progress)
        {
            if (progress <= 0 || _allImages.Count == 0)
            {
                return;
            }

            var targetIndex = (int)Math.Clamp(Math.Ceiling(progress * _allImages.Count) - 1, 0, _allImages.Count - 1);
            while (_loadedCount <= targetIndex && _loadedCount < _allImages.Count)
            {
                var needed = targetIndex - _loadedCount + 1;
                if (needed <= 0)
                {
                    break;
                }

                var requestCount = Math.Max(Constants.DefaultLoadedBatchSize, needed);
                await LoadMoreImagesAsync(requestCount).ConfigureAwait(false);
            }
            if (_isRestoringProgress)
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
                await dispatcher.InvokeAsync(NotifyImagesLoadedForRestore, DispatcherPriority.Background);
            }
        }

        private async Task InitializeChapterImagesAsync(int chapterIndex)
        {
            if (_chapterListViewModel == null)
            {
                return;
            }

            try
            {
                var images = await _chapterListViewModel.GetChapterImagesAsync(chapterIndex).ConfigureAwait(false);
                await ApplyInitialImagesAsync(images).ConfigureAwait(false);

                var restoreProgress = _pendingRestoreProgress;
                var restoreOffset = _pendingRestoreOffset;

                if (restoreProgress.HasValue)
                {
                    await EnsureImagesLoadedForProgressAsync(restoreProgress.Value).ConfigureAwait(false);
                }

                if (restoreProgress.HasValue || restoreOffset.HasValue)
                {
                    var normalizedForRestore = restoreOffset.HasValue ? null : restoreProgress;
                    RequestScrollRestore(normalizedForRestore, restoreOffset);
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"Initial image load cancelled for chapter {chapterIndex}.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to initialize chapter images for chapter {chapterIndex}: {ex.Message}");
            }
        }


        private async Task ApplyInitialImagesAsync(IReadOnlyList<ChapterImage>? images)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

            var newImages = images?.ToList() ?? new List<ChapterImage>();

            await _imageLoadSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                ReleaseChapterImageResources(_allImages);
                _allImages.Clear();
                _allImages.AddRange(newImages);
                _loadedCount = 0;
                if (_isRestoringProgress)
                {
                    ResetRestoreTargetTracking();
                    ScheduleRestoreTargetEvaluation();
                }
                await dispatcher.InvokeAsync(() =>
                {
                    ImageUrls.Clear();
                    NotifyLoadingMetricsChanged();
                }, DispatcherPriority.Background);
            }
            finally
            {
                _imageLoadSemaphore.Release();
            }

            if (_allImages.Count > 0)
            {
                await LoadMoreImagesAsync().ConfigureAwait(false);
                _chapterCoordinator?.PrefetchImages(_allImages, _loadedCount, Math.Min(Constants.DefaultLoadedBatchSize, _allImages.Count - _loadedCount));
            }
        }

        private static void ReleaseChapterImageResources(IEnumerable<ChapterImage> images)
        {
            foreach (var image in images)
            {
                try
                {
                    image.ReleaseResources?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to release resources for image '{image.ImageUrl}': {ex.Message}");
                }
            }
        }

        private void NotifyLoadingMetricsChanged()
        {
            OnPropertyChanged(nameof(RemainingImages));
            OnPropertyChanged(nameof(LoadedImages));
            OnPropertyChanged(nameof(TotalImages));
            OnPropertyChanged(nameof(LoadingProgress));
        }
    }
}