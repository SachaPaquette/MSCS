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
                    return;
                }

                int countToLoad = desiredCount.HasValue
                    ? Math.Min(remaining, Math.Max(1, desiredCount.Value))
                    : Math.Min(Constants.DefaultLoadedBatchSize, remaining);
                var batch = new List<ChapterImage>(countToLoad);
                for (int i = 0; i < countToLoad; i++)
                {
                    token.ThrowIfCancellationRequested();
                    batch.Add(_allImages[_loadedCount + i]);
                }

                var dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
                await dispatcher.InvokeAsync(() =>
                {
                    foreach (var image in batch)
                    {
                        ImageUrls.Add(image);
                    }

                    _loadedCount += batch.Count;
                    OnPropertyChanged(nameof(RemainingImages));
                    OnPropertyChanged(nameof(LoadedImages));
                    OnPropertyChanged(nameof(TotalImages));
                    OnPropertyChanged(nameof(LoadingProgress));
                }, DispatcherPriority.Background);

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
        }
    }
}