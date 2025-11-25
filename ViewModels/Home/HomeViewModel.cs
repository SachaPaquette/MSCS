using System;

namespace MSCS.ViewModels
{
    public class LocalChapterRequestedEventArgs : EventArgs
    {
        public LocalChapterRequestedEventArgs(string chapterPath)
        {
            ChapterPath = chapterPath ?? throw new ArgumentNullException(nameof(chapterPath));
        }

        public string ChapterPath { get; }
    }
}

namespace MSCS.ViewModels
{
    public class HomeViewModel : BaseViewModel
    {
        public HomeViewModel(
            LocalLibraryViewModel localLibraryViewModel,
            BookmarkLibraryViewModel bookmarkLibraryViewModel,
            ContinueReadingViewModel continueReadingViewModel)
        {
            LocalLibrary = localLibraryViewModel ?? throw new ArgumentNullException(nameof(localLibraryViewModel));
            Bookmarks = bookmarkLibraryViewModel ?? throw new ArgumentNullException(nameof(bookmarkLibraryViewModel));
            ContinueReading = continueReadingViewModel ?? throw new ArgumentNullException(nameof(continueReadingViewModel));
        }

        public event EventHandler<LocalChapterRequestedEventArgs>? LocalChapterRequested;

        public LocalLibraryViewModel LocalLibrary { get; }

        public BookmarkLibraryViewModel Bookmarks { get; }

        public ContinueReadingViewModel ContinueReading { get; }

        public void OpenLocalChapter(LocalLibraryChapterEntryViewModel chapter)
        {
            if (chapter == null || string.IsNullOrWhiteSpace(chapter.FullPath))
            {
                return;
            }

            LocalChapterRequested?.Invoke(this, new LocalChapterRequestedEventArgs(chapter.FullPath));
        }
    }
}