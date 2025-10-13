using MSCS.Models;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace MSCS.ViewModels
{
    public partial class ReaderViewModel
    {
        public void UpdateScrollPosition(double verticalOffset, double extentHeight, double viewportHeight)
        {
            var clampedExtent = double.IsNaN(extentHeight) ? 0 : Math.Max(extentHeight, 0);
            var clampedViewport = double.IsNaN(viewportHeight) ? 0 : Math.Max(viewportHeight, 0);

            if (clampedExtent <= 0 || clampedViewport <= 0)
            {
                return;
            }

            var previousOffset = _lastKnownScrollOffset;
            var previousExtent = _lastKnownExtentHeight;
            var previousViewport = _lastKnownViewportHeight;

            var scrollableHeight = Math.Max(clampedExtent - clampedViewport, 0);
            var maxOffset = scrollableHeight > 0 ? scrollableHeight : 0;
            var clampedOffset = double.IsNaN(verticalOffset) ? 0 : Math.Clamp(verticalOffset, 0, maxOffset);
            var progress = scrollableHeight > 0 ? Math.Clamp(clampedOffset / scrollableHeight, 0.0, 1.0) : 0.0;

            var isLayoutReset = scrollableHeight <= 0
                && clampedOffset <= 0
                && previousExtent > 0
                && previousViewport > 0
                && previousOffset > 0;

            if (isLayoutReset)
            {
                return;
            }

            _lastKnownScrollOffset = clampedOffset;
            _lastKnownExtentHeight = clampedExtent;
            _lastKnownViewportHeight = clampedViewport;

            SetProperty(ref _scrollProgress, progress, nameof(ScrollProgress));

            if (_isRestoringProgress)
            {
                if (!HasReachedRestoreTarget(clampedOffset, progress))
                {
                    return;
                }

                _isRestoringProgress = false;
                _pendingRestoreProgress = null;
                _pendingRestoreOffset = null;
            }

            PersistReadingProgress();
        }

        private bool HasReachedRestoreTarget(double currentOffset, double currentProgress)
        {
            if (_pendingRestoreOffset.HasValue)
            {
                var tolerance = Math.Max(1.0, _lastKnownViewportHeight * 0.01);
                if (Math.Abs(currentOffset - _pendingRestoreOffset.Value) <= tolerance)
                {
                    return true;
                }
            }

            if (_pendingRestoreProgress.HasValue)
            {
                if (Math.Abs(currentProgress - _pendingRestoreProgress.Value) <= 0.01)
                {
                    return true;
                }
            }

            return false;
        }

        private void PersistReadingProgress(bool force = false)
        {
            if (_isRestoringProgress || _userSettings == null || string.IsNullOrWhiteSpace(MangaTitle))
            {
                return;
            }

            var normalizedProgress = Math.Clamp(_scrollProgress, 0.0, 1.0);
            var currentOffset = _lastKnownScrollOffset;
            var now = DateTime.UtcNow;
            var difference = double.IsNaN(_lastPersistedScrollProgress)
                ? double.MaxValue
                : Math.Abs(normalizedProgress - _lastPersistedScrollProgress);
            var offsetDifference = double.IsNaN(_lastPersistedScrollOffset)
                ? double.MaxValue
                : Math.Abs(currentOffset - _lastPersistedScrollOffset);

            if (!force && difference < 0.02 && offsetDifference < 16 && (now - _lastProgressSaveUtc) < TimeSpan.FromSeconds(2))
            {
                return;
            }

            Chapter? currentChapter = null;
            if (_chapterListViewModel != null &&
                _currentChapterIndex >= 0 &&
                _currentChapterIndex < _chapterListViewModel.Chapters.Count)
            {
                currentChapter = _chapterListViewModel.Chapters[_currentChapterIndex];
            }
            else if (SelectedChapter != null)
            {
                currentChapter = SelectedChapter;
            }

            var title = currentChapter?.Title ?? ChapterTitle ?? string.Empty;
            var mangaUrl = _chapterListViewModel?.Manga?.Url;
            var sourceKey = _chapterListViewModel?.SourceKey;
            var coverImageUrl = _chapterListViewModel?.Manga?.CoverImageUrl;

            var progress = new MangaReadingProgress(
                _currentChapterIndex,
                title,
                normalizedProgress,
                DateTimeOffset.UtcNow,
                string.IsNullOrWhiteSpace(mangaUrl) ? null : mangaUrl,
                string.IsNullOrWhiteSpace(sourceKey) ? null : sourceKey,
                currentOffset);
            var key = CreateProgressKey();
            if (!key.IsEmpty)
            {
                _userSettings.SetReadingProgress(key, progress);
            }
            _lastPersistedScrollProgress = normalizedProgress;
            _lastPersistedScrollOffset = currentOffset;
            _lastProgressSaveUtc = now;
        }

        private void RestoreReadingProgress()
        {
            if (_hasRestoredInitialProgress || string.IsNullOrWhiteSpace(MangaTitle))
            {
                return;
            }

            if (_initialProgress != null)
            {
                _hasRestoredInitialProgress = true;
                var initialProg = _initialProgress;
                _initialProgress = null;
                _ = RestoreReadingProgressAsync(initialProg);
                return;
            }

            if (_userSettings != null && _userSettings.TryGetReadingProgress(CreateProgressKey(), out var progress) && progress != null)
            {
                _hasRestoredInitialProgress = true;
                _ = RestoreReadingProgressAsync(progress);
            }
            else if (_chapterListViewModel != null)
            {
                _hasRestoredInitialProgress = true;
                PersistReadingProgress(force: true);
            }
        }

        private async Task RestoreReadingProgressAsync(MangaReadingProgress progress)
        {
            if (progress == null)
            {
                return;
            }

            try
            {
                _isRestoringProgress = true;
                if (_chapterListViewModel != null && _chapterListViewModel.Chapters.Count > 0)
                {
                    var targetIndex = Math.Clamp(progress.ChapterIndex, 0, _chapterListViewModel.Chapters.Count - 1);
                    if (targetIndex != _currentChapterIndex)
                    {
                        await TryMoveToChapterAsync(targetIndex).ConfigureAwait(false);
                    }
                }
                var clamped = Math.Clamp(progress.ScrollProgress, 0.0, 1.0);
                _pendingRestoreProgress = clamped > 0 ? clamped : null;
                _pendingRestoreOffset = progress.ScrollOffset.HasValue && progress.ScrollOffset.Value > 0
                    ? Math.Max(0, progress.ScrollOffset.Value)
                    : null;
                SetProperty(ref _scrollProgress, clamped, nameof(ScrollProgress));
                _lastPersistedScrollProgress = clamped;
                if (_pendingRestoreOffset.HasValue)
                {
                    _lastKnownScrollOffset = _pendingRestoreOffset.Value;
                }

                if (_pendingRestoreProgress.HasValue)
                {
                    await EnsureImagesLoadedForProgressAsync(_pendingRestoreProgress.Value).ConfigureAwait(false);
                    RequestScrollRestore(_pendingRestoreProgress, _pendingRestoreOffset);
                }
                else
                {
                    if (_pendingRestoreOffset.HasValue)
                    {
                        RequestScrollRestore(null, _pendingRestoreOffset);
                    }
                    else
                    {
                        _isRestoringProgress = false;
                    }
                }

                if (!_pendingRestoreProgress.HasValue && !_pendingRestoreOffset.HasValue)
                {
                    _isRestoringProgress = false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to restore reading progress: {ex.Message}");
                _pendingRestoreProgress = null;
                _pendingRestoreOffset = null;
            }
            finally
            {
                if (!_pendingRestoreProgress.HasValue && !_pendingRestoreOffset.HasValue)
                {
                    _isRestoringProgress = false;
                }
            }
        }

        private void RequestScrollRestore(double? progress, double? offset)
        {
            if (progress.HasValue || offset.HasValue)
            {
                var request = new ScrollRestoreRequest(progress, offset);
                _queuedScrollRestoreRequest = request;
                _scrollRestoreRequested?.Invoke(this, request);
            }
        }
        private ReadingProgressKey CreateProgressKey()
        {
            var sourceKey = _chapterListViewModel?.SourceKey;
            var mangaUrl = _chapterListViewModel?.Manga?.Url;
            return new ReadingProgressKey(MangaTitle, sourceKey, mangaUrl);
        }

        internal void NotifyScrollRestoreCompleted()
        {
            if (!_isRestoringProgress)
            {
                return;
            }

            _isRestoringProgress = false;
            _pendingRestoreProgress = null;
            _pendingRestoreOffset = null;
            _lastProgressSaveUtc = DateTime.MinValue;
        }
    }
}