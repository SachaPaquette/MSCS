using MSCS.Models;
using MSCS.ViewModels;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace MSCS.Services.Reader
{
    public sealed class ReaderChapterCoordinator
    {
        private readonly ChapterListViewModel _chapterListViewModel;
        private readonly SemaphoreSlim _navigationSemaphore = new(1, 1);
        private readonly SemaphoreSlim _prefetchSemaphore = new(4, 4);
        private readonly HttpClient _httpClient = new();
        private bool _isNavigationInProgress;
        private const int MaxCachedImages = 96;
        private readonly ConcurrentDictionary<string, CachedImage> _imageCache = new();
        private readonly LinkedList<string> _cacheOrder = new();
        private readonly object _cacheLock = new();

        public ReaderChapterCoordinator(ChapterListViewModel chapterListViewModel)
        {
            _chapterListViewModel = chapterListViewModel ?? throw new ArgumentNullException(nameof(chapterListViewModel));
        }

        public event EventHandler? ImageCached;

        public bool CanNavigateTo(int index)
        {
            return index >= 0 && index < _chapterListViewModel.Chapters.Count;
        }

        public bool CanGoToNext(int currentIndex) => CanNavigateTo(currentIndex + 1);

        public bool CanGoToPrevious(int currentIndex) => CanNavigateTo(currentIndex - 1);

        public async Task<ReaderChapterNavigationResult?> MoveToChapterAsync(int index, CancellationToken cancellationToken = default)
        {
            if (!CanNavigateTo(index))
            {
                Debug.WriteLine($"Requested chapter index {index} is out of range.");
                return null;
            }

            await _navigationSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            var acquired = true;
            try
            {
                if (_isNavigationInProgress)
                {
                    Debug.WriteLine("A chapter navigation is already in progress.");
                    return null;
                }

                _isNavigationInProgress = true;

                var images = await _chapterListViewModel.GetChapterImagesAsync(index).ConfigureAwait(false);
                if (images == null || images.Count == 0)
                {
                    Debug.WriteLine($"No images returned for chapter at index {index}.");
                    return null;
                }

                var chapter = index >= 0 && index < _chapterListViewModel.Chapters.Count
                    ? _chapterListViewModel.Chapters[index]
                    : null;

                PrefetchImages(images, 0, Math.Min(12, images.Count));
                _ = _chapterListViewModel.PrefetchChapterAsync(index + 1);

                return new ReaderChapterNavigationResult(index, chapter, images);
            }
            finally
            {
                _isNavigationInProgress = false;
                if (acquired)
                {
                    _navigationSemaphore.Release();
                }
            }
        }

        public void PrefetchImages(IReadOnlyList<ChapterImage> images, int startIndex, int count, CancellationToken cancellationToken = default)
        {
            if (images == null || images.Count == 0 || count <= 0)
            {
                return;
            }

            var end = Math.Min(images.Count, startIndex + count);
            for (var i = Math.Max(0, startIndex); i < end; i++)
            {
                QueuePrefetch(images[i], cancellationToken);
            }
        }

        public BitmapSource? TryGetCachedImage(ChapterImage image)
        {
            var key = GetCacheKey(image);
            if (key == null)
            {
                return null;
            }

            if (_imageCache.TryGetValue(key, out var cached))
            {
                TouchCache(key);
                return cached.Bitmap;
            }

            return null;
        }

        public void ReleaseImage(ChapterImage? image)
        {
            if (image == null)
            {
                return;
            }

            var key = GetCacheKey(image);
            if (key == null)
            {
                return;
            }

            if (_imageCache.TryRemove(key, out _))
            {
                lock (_cacheLock)
                {
                    var node = _cacheOrder.Find(key);
                    if (node != null)
                    {
                        _cacheOrder.Remove(node);
                    }
                }
            }
        }

        private void QueuePrefetch(ChapterImage image, CancellationToken cancellationToken)
        {
            var key = GetCacheKey(image);
            if (key == null)
            {
                return;
            }

            if (_imageCache.ContainsKey(key))
            {
                TouchCache(key);
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await PrefetchImageAsync(key, image, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine($"Image prefetch cancelled for {key}.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to prefetch image {key}: {ex.Message}");
                }
            }, CancellationToken.None);
        }

        private async Task PrefetchImageAsync(string key, ChapterImage image, CancellationToken cancellationToken)
        {
            if (_imageCache.ContainsKey(key))
            {
                TouchCache(key);
                return;
            }

            await _prefetchSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_imageCache.ContainsKey(key))
                {
                    TouchCache(key);
                    return;
                }

                var cached = await LoadBitmapAsync(image, cancellationToken).ConfigureAwait(false);
                if (cached == null)
                {
                    return;
                }

                _imageCache[key] = cached;
                lock (_cacheLock)
                {
                    _cacheOrder.AddFirst(key);
                    while (_cacheOrder.Count > MaxCachedImages)
                    {
                        var last = _cacheOrder.Last;
                        if (last == null)
                        {
                            break;
                        }

                        _cacheOrder.RemoveLast();
                        _imageCache.TryRemove(last.Value, out _);
                    }
                }

                ImageCached?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                _prefetchSemaphore.Release();
            }
        }

        private async Task<CachedImage?> LoadBitmapAsync(ChapterImage image, CancellationToken cancellationToken)
        {
            if (image.StreamFactory != null)
            {
                using var stream = image.StreamFactory();
                if (stream == null)
                {
                    return null;
                }

                return await CreateCachedImageAsync(stream, cancellationToken).ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(image.ImageUrl))
            {
                return null;
            }

            if (Uri.TryCreate(image.ImageUrl, UriKind.Absolute, out var uri) &&
                (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                 uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                if (image.Headers != null)
                {
                    foreach (var header in image.Headers)
                    {
                        if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
                        {
                            request.Content ??= new StringContent(string.Empty);
                            request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                        }
                    }
                }

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                return await CreateCachedImageAsync(stream, cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(image.ImageUrl))
            {
                await using var fileStream = File.OpenRead(image.ImageUrl);
                return await CreateCachedImageAsync(fileStream, cancellationToken).ConfigureAwait(false);
            }

            return null;
        }

        private static async Task<CachedImage?> CreateCachedImageAsync(Stream source, CancellationToken cancellationToken)
        {
            await using var ms = new MemoryStream();
            await source.CopyToAsync(ms, 81920, cancellationToken).ConfigureAwait(false);
            var bytes = ms.ToArray();
            if (bytes.Length == 0)
            {
                return null;
            }

            using var msImage = new MemoryStream(bytes, writable: false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = msImage;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return new CachedImage(bitmap);
        }

        private void TouchCache(string key)
        {
            lock (_cacheLock)
            {
                var node = _cacheOrder.Find(key);
                if (node != null)
                {
                    _cacheOrder.Remove(node);
                    _cacheOrder.AddFirst(node);
                }
            }
        }

        private static string? GetCacheKey(ChapterImage image)
        {
            if (!string.IsNullOrWhiteSpace(image.ImageUrl))
            {
                return image.ImageUrl;
            }

            return image.StreamFactory != null ? image.StreamFactory.GetHashCode().ToString() : null;
        }

        private sealed class CachedImage
        {
            public CachedImage(BitmapImage bitmap)
            {
                Bitmap = bitmap;
            }

            public BitmapImage Bitmap { get; }
        }
    }

    public sealed record ReaderChapterNavigationResult(int ChapterIndex, Chapter? Chapter, IReadOnlyList<ChapterImage> Images);
}