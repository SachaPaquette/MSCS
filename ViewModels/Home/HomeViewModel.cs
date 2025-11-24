using System;

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

        public LocalLibraryViewModel LocalLibrary { get; }

        public BookmarkLibraryViewModel Bookmarks { get; }

        public ContinueReadingViewModel ContinueReading { get; }
    }
}