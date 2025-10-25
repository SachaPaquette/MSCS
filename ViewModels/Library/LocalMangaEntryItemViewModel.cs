﻿using System;
using System.Windows.Input;
using MSCS.Commands;
using MSCS.Models;
using MSCS.Services;
using MSCS.Sources;

namespace MSCS.ViewModels
{
    public class LocalMangaEntryItemViewModel : BaseViewModel, IDisposable
    {
        private readonly UserSettings _userSettings;
        private bool _isBookmarked;
        private bool _disposed;
        private string? _bookmarkStorageKey;

        public LocalMangaEntryItemViewModel(LocalMangaEntry entry, UserSettings userSettings)
        {
            Entry = entry ?? throw new ArgumentNullException(nameof(entry));
            _userSettings = userSettings ?? throw new ArgumentNullException(nameof(userSettings));

            ToggleBookmarkCommand = new RelayCommand(_ => ToggleBookmark(), _ => CanToggleBookmark());
            UpdateBookmarkState();
        }

        public LocalMangaEntry Entry { get; }

        public string Title => Entry.Title;

        public string Path => Entry.Path;

        public int ChapterCount => Entry.ChapterCount;

        public DateTime LastModifiedUtc => Entry.LastModifiedUtc;

        public string GroupKey => Entry.GroupKey;

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

        public Manga ToManga()
        {
            return Entry.ToManga();
        }

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

        public void Dispose()
        {
            _disposed = true;
            CommandManager.InvalidateRequerySuggested();
        }

        private bool CanToggleBookmark()
        {
            return !_disposed && !string.IsNullOrWhiteSpace(Title);
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
                if (!string.IsNullOrWhiteSpace(_bookmarkStorageKey))
                {
                    _userSettings.RemoveBookmark(_bookmarkStorageKey);
                }
                else
                {
                    _userSettings.RemoveBookmark(key);
                }

                _bookmarkStorageKey = null;
                IsBookmarked = false;
            }
            else
            {
                var entry = _userSettings.AddOrUpdateBookmark(key, Title, coverImageUrl: null);
                _bookmarkStorageKey = entry?.StorageKey;
                IsBookmarked = entry != null;
            }
        }

        private BookmarkKey CreateBookmarkKey()
        {
            var title = Title;
            var path = string.IsNullOrWhiteSpace(Path) ? null : Path;
            return new BookmarkKey(title, SourceKeyConstants.LocalLibrary, path);
        }
    }
}