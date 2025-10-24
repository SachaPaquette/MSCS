using MSCS.Commands;
using MSCS.Models;
using MSCS.Services;
using MSCS.Sources;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Application = System.Windows.Application;

namespace MSCS.ViewModels
{
    public class BookmarkLibraryViewModel : BaseViewModel, IDisposable
    {
        private readonly UserSettings _userSettings;
        private readonly List<BookmarkItemViewModel> _allBookmarks = new();
        private string _searchQuery = string.Empty;
        private BookmarkItemViewModel? _selectedBookmark;
        private bool _disposed;

        public BookmarkLibraryViewModel(UserSettings userSettings)
        {
            _userSettings = userSettings ?? throw new ArgumentNullException(nameof(userSettings));
            _userSettings.BookmarksChanged += OnBookmarksChanged;

            Bookmarks = new ObservableCollection<BookmarkItemViewModel>();
            RemoveBookmarkCommand = new RelayCommand(
                parameter => RemoveBookmark(parameter as BookmarkItemViewModel),
                parameter => parameter is BookmarkItemViewModel item && !string.IsNullOrEmpty(item.Entry.StorageKey));

            RefreshBookmarks();
        }

        public ObservableCollection<BookmarkItemViewModel> Bookmarks { get; }

        public ICommand RemoveBookmarkCommand { get; }

        public event EventHandler<BookmarkSelectedEventArgs>? BookmarkSelected;

        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (SetProperty(ref _searchQuery, value))
                {
                    ApplyFilter();
                    OnPropertyChanged(nameof(EmptyStateMessage));
                }
            }
        }

        public BookmarkItemViewModel? SelectedBookmark
        {
            get => _selectedBookmark;
            set
            {
                if (SetProperty(ref _selectedBookmark, value))
                {
                    if (value != null && !_disposed)
                    {
                        BookmarkSelected?.Invoke(this, new BookmarkSelectedEventArgs(value.Entry));
                        SelectedBookmark = null;
                    }
                }
            }
        }

        public bool ShowEmptyState => Bookmarks.Count == 0;

        public string EmptyStateMessage => string.IsNullOrWhiteSpace(SearchQuery)
            ? "Bookmarks you add will appear here."
            : "No bookmarks match your search.";

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _userSettings.BookmarksChanged -= OnBookmarksChanged;
        }

        private void RefreshBookmarks()
        {
            SelectedBookmark = null;
            _allBookmarks.Clear();

            var entries = _userSettings.GetAllBookmarks();
            foreach (var entry in entries.OrderByDescending(e => e.AddedUtc))
            {
                _allBookmarks.Add(new BookmarkItemViewModel(entry));
            }

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            IEnumerable<BookmarkItemViewModel> items = _allBookmarks;
            var query = SearchQuery?.Trim();

            if (!string.IsNullOrEmpty(query))
            {
                items = items.Where(item => item.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            Bookmarks.Clear();
            foreach (var item in items)
            {
                Bookmarks.Add(item);
            }

            OnPropertyChanged(nameof(ShowEmptyState));
        }

        private void RemoveBookmark(BookmarkItemViewModel? item)
        {
            if (_disposed || item == null)
            {
                return;
            }

            _userSettings.RemoveBookmark(item.Entry.StorageKey);
        }

        private void OnBookmarksChanged(object? sender, EventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(RefreshBookmarks);
            }
            else
            {
                RefreshBookmarks();
            }
        }
    }

    public class BookmarkItemViewModel : BaseViewModel
    {
        private static readonly string LocalSourceName = "Local Library";

        public BookmarkItemViewModel(BookmarkEntry entry)
        {
            Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        }

        public BookmarkEntry Entry { get; }

        public string Title => Entry.Title;

        public string? CoverImageUrl => Entry.CoverImageUrl;

        public bool HasCover => !string.IsNullOrWhiteSpace(CoverImageUrl);

        public bool IsLocal => string.Equals(Entry.SourceKey, SourceKeyConstants.LocalLibrary, StringComparison.OrdinalIgnoreCase);

        public string SourceDisplayName
        {
            get
            {
                if (IsLocal)
                {
                    return LocalSourceName;
                }

                if (string.IsNullOrWhiteSpace(Entry.SourceKey))
                {
                    return "External Source";
                }

                var descriptor = SourceRegistry.GetDescriptor(Entry.SourceKey);
                return descriptor?.DisplayName ?? Entry.SourceKey;
            }
        }

        public string AddedDisplay => Entry.AddedUtc.ToLocalTime().ToString("g");

        public bool CanOpen => !string.IsNullOrWhiteSpace(Entry.MangaUrl) || IsLocal;
    }

    public class BookmarkSelectedEventArgs : EventArgs
    {
        public BookmarkSelectedEventArgs(BookmarkEntry entry)
        {
            Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        }

        public BookmarkEntry Entry { get; }
    }
}