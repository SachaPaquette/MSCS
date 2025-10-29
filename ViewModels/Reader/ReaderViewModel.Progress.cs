using MSCS.Models;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Threading;

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
                if (!HasReachedRestoreTarget(clampedOffset))
                {
                    return;
                }

                _isRestoringProgress = false;
                _pendingRestoreProgress = null;
                _pendingRestoreOffset = null;
            }

            PersistReadingProgress();
        }

        public void UpdateScrollAnchor(string? imageUrl, double? anchorProgress)
        {
            if (_isRestoringProgress)
            {
                return;
            }

            _lastKnownAnchorImageUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl;
            _lastKnownAnchorImageProgress = anchorProgress.HasValue
                ? Math.Clamp(anchorProgress.Value, 0.0, 1.0)
                : null;
        }

        private bool HasReachedRestoreTarget(double currentOffset)
        {
            if (_pendingRestoreOffset.HasValue)
            {
                var tolerance = Math.Max(1.0, _lastKnownViewportHeight * 0.01);
                if (Math.Abs(currentOffset - _pendingRestoreOffset.Value) <= tolerance)
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

            var currentOffset = _lastKnownScrollOffset;
            var extentHeight = double.IsNaN(_lastKnownExtentHeight) ? 0 : Math.Max(0, _lastKnownExtentHeight);
            var viewportHeight = double.IsNaN(_lastKnownViewportHeight) ? 0 : Math.Max(0, _lastKnownViewportHeight);
            var scrollableHeight = Math.Max(extentHeight - viewportHeight, 0);
            var now = DateTime.UtcNow;
            var offsetDifference = double.IsNaN(_lastPersistedScrollOffset)
                ? double.MaxValue
                : Math.Abs(currentOffset - _lastPersistedScrollOffset);

            if (!force && offsetDifference < 16 && (now - _lastProgressSaveUtc) < TimeSpan.FromSeconds(2))
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
                null,
                DateTimeOffset.UtcNow,
                string.IsNullOrWhiteSpace(mangaUrl) ? null : mangaUrl,
                string.IsNullOrWhiteSpace(sourceKey) ? null : sourceKey,
                currentOffset,
                scrollableHeight > 0 ? scrollableHeight : null,
                string.IsNullOrWhiteSpace(_lastKnownAnchorImageUrl) ? null : _lastKnownAnchorImageUrl,
                _lastKnownAnchorImageProgress.HasValue
                    ? Math.Clamp(_lastKnownAnchorImageProgress.Value, 0.0, 1.0)
                    : null);
            var key = CreateProgressKey();
            if (!key.IsEmpty)
            {
                _userSettings.SetReadingProgress(key, progress);
            }
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
                double? pendingOffset = progress.ScrollOffset.HasValue && progress.ScrollOffset.Value >= 0
                    ? Math.Max(0, progress.ScrollOffset.Value)
                    : null;

                double? pendingProgress = null;
                double clampedProgress = Math.Clamp(progress.ScrollProgress, 0.0, 1.0);

                if (pendingOffset.HasValue && progress.ScrollableHeight.HasValue && progress.ScrollableHeight.Value > 0)
                {
                    var normalized = pendingOffset.Value / progress.ScrollableHeight.Value;
                    var clamped = Math.Clamp(normalized, 0.0, 1.0);
                    if (clamped > 0)
                    {
                        pendingProgress = clamped;
                    }
                }
                else if (!pendingOffset.HasValue && clampedProgress > 0)
                {
                    pendingProgress = clampedProgress;
                }

                _pendingRestoreAnchorImageUrl = string.IsNullOrWhiteSpace(progress.AnchorImageUrl)
                    ? null
                    : progress.AnchorImageUrl;
                _pendingRestoreAnchorImageProgress = progress.AnchorImageProgress.HasValue
                    ? Math.Clamp(progress.AnchorImageProgress.Value, 0.0, 1.0)
                    : null;
                _pendingRestoreProgress = pendingProgress;
                _pendingRestoreOffset = pendingOffset;
                ResetRestoreTargetTracking();

                double displayProgress;
                if (_pendingRestoreOffset.HasValue)
                {
                    displayProgress = pendingProgress ?? 0.0;
                }
                else
                {
                    displayProgress = pendingProgress ?? clampedProgress;
                }
                SetProperty(ref _scrollProgress, displayProgress, nameof(ScrollProgress));

                if (_pendingRestoreOffset.HasValue)
                {
                    _lastKnownScrollOffset = _pendingRestoreOffset.Value;
                    _lastPersistedScrollOffset = _pendingRestoreOffset.Value;
                }

                if (_pendingRestoreProgress.HasValue)
                {
                    await EnsureImagesLoadedForProgressAsync(_pendingRestoreProgress.Value).ConfigureAwait(false);
                }

                if (_pendingRestoreOffset.HasValue)
                {
                    RequestScrollRestore(null, _pendingRestoreOffset);
                }
                else if (_pendingRestoreProgress.HasValue)
                {
                    RequestScrollRestore(_pendingRestoreProgress, null);
                }
                else
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
                ResetRestoreTargetTracking();
                var request = new ScrollRestoreRequest(progress, offset, _pendingRestoreAnchorImageUrl, _pendingRestoreAnchorImageProgress);
                _queuedScrollRestoreRequest = request;
                _scrollRestoreRequested?.Invoke(this, request);
                ScheduleRestoreTargetEvaluation();
            }
        }

        private ReadingProgressKey CreateProgressKey()
        {
            var sourceKey = _chapterListViewModel?.SourceKey;
            var mangaUrl = _chapterListViewModel?.Manga?.Url;
            return new ReadingProgressKey(MangaTitle, sourceKey, mangaUrl);
        }

        public bool IsRestoringProgress => _isRestoringProgress;

        private DateTime _restoreCooldownUntil = DateTime.MinValue;
        public bool IsInRestoreCooldown => DateTime.UtcNow < _restoreCooldownUntil;

        internal void NotifyScrollRestoreCompleted()
        {
            if (_isRestoringProgress)
            {
                _isRestoringProgress = false;
                _pendingRestoreProgress = null;
                _pendingRestoreOffset = null;
                _lastProgressSaveUtc = DateTime.MinValue;
            }
            _pendingRestoreAnchorImageUrl = null;
            _pendingRestoreAnchorImageProgress = null;
            _pendingRestoreTargetIndex = null;
            _restoreTargetReadySignaled = false;
            _restoreCooldownUntil = DateTime.UtcNow.AddMilliseconds(300);
            _queuedScrollRestoreRequest = null;
        }


        internal void NotifyImagesLoadedForRestore()
        {
            TrySignalRestoreTargetReady();
        }

        private void ResetRestoreTargetTracking()
        {
            _restoreTargetReadySignaled = false;
            UpdateRestoreTargetIndex();
        }

        private void UpdateRestoreTargetIndex()
        {
            int? target = null;

            if (!string.IsNullOrWhiteSpace(_pendingRestoreAnchorImageUrl))
            {
                var anchorIndex = GetImageIndex(_pendingRestoreAnchorImageUrl!);
                if (anchorIndex >= 0)
                {
                    target = anchorIndex;
                }
            }

            if (!target.HasValue && _pendingRestoreProgress.HasValue && _allImages.Count > 0)
            {
                var estimated = (int)Math.Clamp(
                    Math.Ceiling(_pendingRestoreProgress.Value * _allImages.Count) - 1,
                    0,
                    _allImages.Count - 1);
                target = estimated;
            }

            _pendingRestoreTargetIndex = target;
        }

        private void TrySignalRestoreTargetReady()
        {
            if (!_isRestoringProgress || _restoreTargetReadySignaled)
            {
                return;
            }

            bool shouldSignal;
            if (_pendingRestoreTargetIndex.HasValue)
            {
                shouldSignal = _pendingRestoreTargetIndex.Value < _loadedCount;
            }
            else
            {
                shouldSignal = _loadedCount > 0 || RemainingImages <= 0;
            }

            if (!shouldSignal && RemainingImages <= 0)
            {
                shouldSignal = true;
            }

            if (shouldSignal)
            {
                _restoreTargetReadySignaled = true;
                _restoreTargetReady?.Invoke(this, EventArgs.Empty);
            }
        }

        private void ScheduleRestoreTargetEvaluation()
        {
            if (!_isRestoringProgress)
            {
                return;
            }

            var dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            dispatcher.InvokeAsync(TrySignalRestoreTargetReady, DispatcherPriority.Background);
        }

        internal int GetImageIndex(string imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                return -1;
            }

            return _allImages.FindIndex(img => string.Equals(img.ImageUrl, imageUrl, StringComparison.OrdinalIgnoreCase));
        }

        internal bool IsImageLoaded(int index)
        {
            return index >= 0 && index < _loadedCount;
        }

    }
}