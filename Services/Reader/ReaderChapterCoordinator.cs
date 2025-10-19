using MSCS.Models;
using MSCS.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MSCS.Services.Reader
{
    public sealed class ReaderChapterCoordinator
    {
        private readonly ChapterListViewModel _chapterListViewModel;
        private readonly SemaphoreSlim _navigationSemaphore = new(1, 1);
        private bool _isNavigationInProgress;

        public ReaderChapterCoordinator(ChapterListViewModel chapterListViewModel)
        {
            _chapterListViewModel = chapterListViewModel ?? throw new ArgumentNullException(nameof(chapterListViewModel));
        }

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
    }

    public sealed record ReaderChapterNavigationResult(int ChapterIndex, Chapter? Chapter, IReadOnlyList<ChapterImage> Images);
}