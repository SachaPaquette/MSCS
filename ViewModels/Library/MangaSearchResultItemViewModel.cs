using MSCS.Commands;
using MSCS.Models;
using MSCS.Services;
using System;
using System.Windows.Input;

namespace MSCS.ViewModels
{
    public sealed class MangaSearchResultItemViewModel : BaseViewModel, IDisposable
    {
        private readonly UserSettings _userSettings;
        private readonly string _sourceKey;
        private bool _isBookmarked;
        private string? _bookmarkStorageKey;
        private bool _disposed;

        public MangaSearchResultItemViewModel(Manga manga, string sourceKey, UserSettings userSettings)
        {
            Manga = manga ?? throw new ArgumentNullException(nameof(manga));
            _sourceKey = string.IsNullOrWhiteSpace(sourceKey) ? string.Empty : sourceKey.Trim();
            _userSettings = userSettings ?? throw new ArgumentNullException(nameof(userSettings));

            ToggleBookmarkCommand = new RelayCommand(_ => ToggleBookmark(), _ => CanToggleBookmark());
            UpdateBookmarkState();
        }

        public Manga Manga { get; }

        public string Title => Manga?.Title ?? string.Empty;

        public string Url => Manga?.Url ?? string.Empty;

        public string? CoverImageUrl => string.IsNullOrWhiteSpace(Manga?.CoverImageUrl) ? null : Manga.CoverImageUrl;

        public ICommand ToggleBookmarkCommand { get; }

        public bool IsBookmarked
        {
            get => _isBookmarked;
            private set
            {
                if (SetProperty(ref _isBookmarked, value))
                {
                    OnPropertyChanged(nameof(BookmarkButtonText));
                    OnPropertyChanged(nameof(BookmarkIconGlyph));
                    OnPropertyChanged(nameof(BookmarkToolTip));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string BookmarkButtonText => IsBookmarked ? "Remove bookmark" : "Add bookmark";

        public string BookmarkIconGlyph => IsBookmarked ? "\uE735" : "\uE734";

        public string BookmarkToolTip => IsBookmarked
            ? "Remove this series from your bookmarks"
            : "Add this series to your bookmarks";

        public void UpdateBookmarkState()
        {
            if (_disposed)
            {
                return;
            }

            var key = CreateBookmarkKey();
            if (key.IsEmpty)
            {
                _bookmarkStorageKey = null;
                IsBookmarked = false;
                return;
            }

            if (_userSettings.TryGetBookmark(key, out var bookmark) && bookmark != null)
            {
                _bookmarkStorageKey = bookmark.StorageKey;
                IsBookmarked = true;
            }
            else
            {
                _bookmarkStorageKey = null;
                IsBookmarked = false;
            }
        }

        private void ToggleBookmark()
        {
            if (_disposed)
            {
                return;
            }

            var key = CreateBookmarkKey();
            if (key.IsEmpty)
            {
                return;
            }

            if (IsBookmarked)
            {
                if (!string.IsNullOrEmpty(_bookmarkStorageKey))
                {
                    _userSettings.RemoveBookmark(_bookmarkStorageKey);
                }
                else
                {
                    _userSettings.RemoveBookmark(key);
                }

                IsBookmarked = false;
                _bookmarkStorageKey = null;
            }
            else
            {
                var title = Title;
                if (string.IsNullOrWhiteSpace(title))
                {
                    return;
                }

                var entry = _userSettings.AddOrUpdateBookmark(key, title, CoverImageUrl);
                _bookmarkStorageKey = entry?.StorageKey;
                IsBookmarked = entry != null;
            }
        }

        private bool CanToggleBookmark()
        {
            return !_disposed && !string.IsNullOrWhiteSpace(Title);
        }

        private BookmarkKey CreateBookmarkKey()
        {
            var title = Title;
            var sourceKey = string.IsNullOrWhiteSpace(_sourceKey) ? null : _sourceKey;
            var url = string.IsNullOrWhiteSpace(Manga?.Url) ? null : Manga.Url;
            return new BookmarkKey(title, sourceKey, url);
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}