using MSCS.Commands;
using MSCS.Interfaces;
using MSCS.Models;
using MSCS.Services;
using MSCS.Sources;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Application = System.Windows.Application;

namespace MSCS.ViewModels
{
    public class ChapterListViewModel : BaseViewModel, IDisposable
    {
        private const int DefaultChapterLimit = 100;
        private readonly IMangaSource _source;
        private readonly INavigationService _navigationService;
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentDictionary<int, Lazy<Task<IReadOnlyList<ChapterImage>>>> _chapterImageCache = new();
        private readonly LinkedList<int> _cacheOrder = new();
        private readonly Dictionary<int, LinkedListNode<int>> _cacheNodes = new();
        private readonly object _cacheLock = new();
        private bool _disposed;
        private readonly MediaTrackingServiceRegistry? _trackingRegistry;
        private readonly UserSettings? _userSettings;
        private const int MaxCachedChapters = 2;
        private readonly bool _autoOpenOnLoad;
        private bool _hasAutoOpened;
        private Manga _manga;
        private ObservableCollection<Chapter> _chapters = new();
        private Chapter? _selectedChapter;
        private MangaReadingProgress? _initialProgress;
        private string _chapterSearchText = string.Empty;
        private bool _showAllChapters;
        private ICollectionView? _filteredChapters;
        private bool _isLoadingChapters;
        private bool _isEmptyAfterFilter;
        private string? _bookmarkStorageKey;
        private bool _isBookmarked;

        public Manga Manga
        {
            get => _manga;
            set
            {
                if (SetProperty(ref _manga, value))
                {
                    UpdateBookmarkState();
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public ObservableCollection<Chapter> Chapters
        {
            get => _chapters;
            set
            {
                if (_chapters == value)
                {
                    return;
                }

                if (_chapters != null)
                {
                    _chapters.CollectionChanged -= OnChaptersCollectionChanged;
                }

                if (SetProperty(ref _chapters, value))
                {
                    if (_chapters != null)
                    {
                        _chapters.CollectionChanged += OnChaptersCollectionChanged;
                    }

                    RefreshChapterView();
                }
            }
        }

        public Chapter? SelectedChapter
        {
            get => _selectedChapter;
            set
            {
                if (SetProperty(ref _selectedChapter, value))
                {
                    EnsureChapterVisible(value);
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public ICommand OpenChapterCommand { get; }
        public ICommand BackCommand { get; }
        public ICommand ToggleChapterListLengthCommand { get; }
        public ICommand ToggleBookmarkCommand { get; }

        public string SourceKey { get; }


        public ICollectionView? FilteredChapters
        {
            get => _filteredChapters;
            private set
            {
                if (_filteredChapters == value)
                {
                    return;
                }

                if (_filteredChapters is INotifyCollectionChanged oldNotify)
                {
                    oldNotify.CollectionChanged -= OnFilteredChaptersCollectionChanged;
                }

                if (SetProperty(ref _filteredChapters, value))
                {
                    if (_filteredChapters is INotifyCollectionChanged newNotify)
                    {
                        newNotify.CollectionChanged += OnFilteredChaptersCollectionChanged;
                    }

                    UpdateFilteredState();
                }
            }
        }

        public bool IsLoadingChapters
        {
            get => _isLoadingChapters;
            private set
            {
                if (SetProperty(ref _isLoadingChapters, value))
                {
                    UpdateFilteredState();
                }
            }
        }

        public bool IsEmptyAfterFilter
        {
            get => _isEmptyAfterFilter;
            private set => SetProperty(ref _isEmptyAfterFilter, value);
        }

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

        public string ChapterSearchText
        {
            get => _chapterSearchText;
            set
            {
                var newValue = value ?? string.Empty;
                if (SetProperty(ref _chapterSearchText, newValue))
                {
                    UpdateLimitedState();
                    RefreshFilteredChapters();
                }
            }
        }

        public bool ShowAllChapters
        {
            get => _showAllChapters;
            set
            {
                if (SetProperty(ref _showAllChapters, value))
                {
                    UpdateLimitedState();
                    RefreshFilteredChapters();
                }
            }
        }

        public bool HasChapterOverflow => Chapters?.Count > DefaultChapterLimit;

        public bool IsShowingLimitedList => HasChapterOverflow && !ShowAllChapters && string.IsNullOrWhiteSpace(ChapterSearchText);

        public string ChapterOverflowMessage
        {
            get
            {
                var total = Chapters?.Count ?? 0;
                var limited = Math.Min(DefaultChapterLimit, total);
                return $"Showing the first {limited} of {total} chapters. Use search or Show all to browse the rest.";
            }
        }

        public string ChapterToggleButtonText => ShowAllChapters ? "Show fewer chapters" : "Show all chapters";

        public ChapterListViewModel(
            IMangaSource source,
            INavigationService navigationService,
            Manga manga,
            MediaTrackingServiceRegistry? trackingRegistry,
            UserSettings userSettings,
            string? sourceKey = null,
            bool autoOpenOnLoad = false,
            MangaReadingProgress? initialProgress = null)
        {
            _source = source;
            _navigationService = navigationService;
            Manga = manga;
            _trackingRegistry = trackingRegistry;
            _userSettings = userSettings;
            if (_userSettings != null)
            {
                _userSettings.BookmarksChanged += OnBookmarksChanged;
            }
            SourceKey = sourceKey ?? string.Empty;
            _autoOpenOnLoad = autoOpenOnLoad;
            _initialProgress = initialProgress;

            OpenChapterCommand = new RelayCommand(
                async parameter =>
                {
                    if (parameter is Chapter chapter)
                        await OpenChapterAsync(chapter);
                    else
                        await OpenChapterAsync();
                },
                parameter => CanOpenChapter(parameter));

            BackCommand = new RelayCommand(_ => _navigationService.GoBack(), _ => _navigationService.CanGoBack);
            WeakEventManager<INavigationService, EventArgs>.AddHandler(_navigationService, nameof(INavigationService.CanGoBackChanged), OnNavigationStateChanged);
            ToggleChapterListLengthCommand = new RelayCommand(_ => ToggleChapterListMode(), _ => HasChapterOverflow);
            ToggleBookmarkCommand = new RelayCommand(_ => ToggleBookmark(), _ => CanToggleBookmark());
            InitializeChapterListState();
            UpdateBookmarkState();

            _ = LoadChaptersAsync();
        }

        public ChapterListViewModel()
        {
            SourceKey = string.Empty;
            OpenChapterCommand = new RelayCommand(_ => { }, _ => false);
            BackCommand = new RelayCommand(_ => { }, _ => false);
            ToggleChapterListLengthCommand = new RelayCommand(_ => ToggleChapterListMode(), _ => HasChapterOverflow);
            ToggleBookmarkCommand = new RelayCommand(_ => { }, _ => false);
            InitializeChapterListState();
        }

        private void InitializeChapterListState()
        {
            _chapters.CollectionChanged += OnChaptersCollectionChanged;
            RefreshChapterView();
        }

        private void RefreshChapterView()
        {
            if (_filteredChapters != null)
            {
                _filteredChapters.Filter = null;
            }

            ICollectionView? view = null;

            if (_chapters != null)
            {
                view = CollectionViewSource.GetDefaultView(_chapters);
                view.Filter = FilterChapter;
            }

            FilteredChapters = view;
            UpdateLimitedState();
            RefreshFilteredChapters();
        }

        private bool FilterChapter(object item)
        {
            if (item is not Chapter chapter)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(ChapterSearchText))
            {
                if (!HasChapterOverflow || ShowAllChapters)
                {
                    return true;
                }

                var index = Chapters.IndexOf(chapter);
                return index >= 0 && index < DefaultChapterLimit;
            }

            var searchText = ChapterSearchText.Trim();
            var comparison = StringComparison.OrdinalIgnoreCase;
            if (!string.IsNullOrEmpty(chapter.Title) && chapter.Title.IndexOf(searchText, comparison) >= 0)
            {
                return true;
            }

            var numberText = chapter.Number.ToString("0.##", CultureInfo.InvariantCulture);
            return numberText.IndexOf(searchText, comparison) >= 0;
        }

        private void UpdateLimitedState()
        {
            OnPropertyChanged(nameof(HasChapterOverflow));
            OnPropertyChanged(nameof(IsShowingLimitedList));
            OnPropertyChanged(nameof(ChapterOverflowMessage));
            OnPropertyChanged(nameof(ChapterToggleButtonText));
            CommandManager.InvalidateRequerySuggested();
        }

        private void OnChaptersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateLimitedState();
            RefreshFilteredChapters();
        }

        private void ToggleChapterListMode()
        {
            ShowAllChapters = !ShowAllChapters;
        }

        private void EnsureChapterVisible(Chapter? chapter)
        {
            if (chapter == null || !HasChapterOverflow || !string.IsNullOrWhiteSpace(ChapterSearchText))
            {
                return;
            }

            var index = Chapters.IndexOf(chapter);
            if (index >= DefaultChapterLimit && !ShowAllChapters)
            {
                ShowAllChapters = true;
            }
        }

        private async Task LoadChaptersAsync()
        {
            if (string.IsNullOrEmpty(Manga?.Url))
            {
                return;
            }

            IsLoadingChapters = true;

            try
            {
                var chapters = await _source.GetChaptersAsync(Manga.Url, _cts.Token);
                ClearChapterCache();
                Chapters = new ObservableCollection<Chapter>(chapters);
                ChapterSearchText = string.Empty;
                ShowAllChapters = false;
                Debug.WriteLine($"Loaded {Chapters.Count} chapters for {Manga.Title}");
                RestoreLastReadChapter();
                MaybeAutoOpenChapter();
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"Chapter load cancelled for {Manga?.Title}");
            }
            finally
            {
                IsLoadingChapters = false;
            }
        }

        private void RefreshFilteredChapters()
        {
            FilteredChapters?.Refresh();
            UpdateFilteredState();
        }

        private void OnFilteredChaptersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateFilteredState();
        }

        private void UpdateFilteredState()
        {
            var isEmpty = FilteredChapters?.IsEmpty ?? true;
            if (IsLoadingChapters)
            {
                isEmpty = false;
            }

            IsEmptyAfterFilter = isEmpty;
        }

        private bool CanOpenChapter(object? parameter)
        {
            if (parameter is Chapter chapter)
            {
                return chapter != null && !string.IsNullOrWhiteSpace(chapter.Url);
            }

            return SelectedChapter != null && !string.IsNullOrWhiteSpace(SelectedChapter.Url);
        }

        private void MaybeAutoOpenChapter()
        {
            if (!_autoOpenOnLoad || _hasAutoOpened)
            {
                return;
            }

            var targetChapter = EnsureSelectedChapter();
            if (targetChapter == null)
            {
                return;
            }

            _hasAutoOpened = true;
            _ = OpenChapterAsync(targetChapter);
        }


        private async Task OpenChapterAsync(Chapter? explicitChapter = null)
        {
            var chapterToOpen = explicitChapter ?? EnsureSelectedChapter();
            if (chapterToOpen == null || string.IsNullOrEmpty(chapterToOpen.Url))
            {
                return;
            }

            if (!ReferenceEquals(SelectedChapter, chapterToOpen))
            {
                SelectedChapter = chapterToOpen;
            }

            var index = Chapters.IndexOf(chapterToOpen);
            if (index < 0)
            {
                return;
            }

            Debug.WriteLine($"Opening chapter: {chapterToOpen.Title} ({chapterToOpen.Url})");

            IReadOnlyList<ChapterImage> images;
            if (!TryGetCachedChapterImages(index, out var cachedImages) || cachedImages == null)
            {
                images = await GetChapterImagesAsync(index);
            }
            else
            {
                images = cachedImages;
            }

            if (images == null || images.Count == 0)
            {
                Debug.WriteLine($"No images returned for chapter: {chapterToOpen.Title} ({chapterToOpen.Url})");
                return;
            }

            var shouldUseInitialProgress = ShouldUseInitialProgressForChapter(chapterToOpen, index);
            MangaReadingProgress? progressForReader;

            if (shouldUseInitialProgress)
            {
                progressForReader = _initialProgress;
            }
            else
            {
                progressForReader = new MangaReadingProgress(
                    index,
                    chapterToOpen.Title,
                    0,
                    DateTimeOffset.UtcNow,
                    string.IsNullOrWhiteSpace(Manga?.Url) ? null : Manga.Url,
                    string.IsNullOrWhiteSpace(SourceKey) ? null : SourceKey);

                if (_userSettings != null)
                {
                    var key = CreateProgressKey();
                    if (!key.IsEmpty)
                    {
                        _userSettings.SetReadingProgress(key, progressForReader);
                    }
                }
            }

            var readerViewModel = new ReaderViewModel(
                images,
                chapterToOpen.Title ?? string.Empty,
                _navigationService,
                this,
                index,
                _trackingRegistry,
                _userSettings,
                progressForReader);

            _navigationService.NavigateToViewModel(readerViewModel);
            _ = PrefetchChapterAsync(index + 1);
        }

        private bool ShouldUseInitialProgressForChapter(Chapter chapterToOpen, int index)
        {
            if (_initialProgress == null)
            {
                return false;
            }

            if (index == _initialProgress.ChapterIndex)
            {
                return true;
            }

            if (index >= 0 && index < Chapters.Count && !string.IsNullOrWhiteSpace(_initialProgress.ChapterTitle))
            {
                if (IsChapterMatchForProgress(chapterToOpen, _initialProgress))
                {
                    UpdateInitialProgressIndex(_initialProgress, index, chapterToOpen);
                    return true;
                }
            }

            return false;
        }

        public bool TryGetCachedChapterImages(int index, out IReadOnlyList<ChapterImage>? images)
        {
            images = null;
            if (index < 0 || index >= Chapters.Count)
            {
                return false;
            }

            if (_chapterImageCache.TryGetValue(index, out var cached) && cached.IsValueCreated)
            {
                var task = cached.Value;
                if (task.IsCompletedSuccessfully)
                {
                    images = task.Result;
                    return true;
                }
            }

            return false;
        }

        private Chapter? EnsureSelectedChapter()
        {
            if (SelectedChapter != null)
            {
                return SelectedChapter;
            }

            var initialChapter = GetInitialProgressChapter();
            if (initialChapter != null)
            {
                SelectedChapter = initialChapter;
                return initialChapter;
            }

            var lastRead = GetLastReadChapter();
            if (lastRead != null)
            {
                SelectedChapter = lastRead;
                return lastRead;
            }

            return null;
        }

        private Chapter? GetLastReadChapter()
        {
            if (_userSettings == null)
            {
                return null;
            }

            var key = CreateProgressKey();
            if (_userSettings.TryGetReadingProgress(key, out var progress))
            {
                var index = progress.ChapterIndex;
                if (index >= 0 && index < Chapters.Count)
                {
                    return Chapters[index];
                }
            }

            return null;
        }

        private void RestoreLastReadChapter()
        {
            var lastRead = GetInitialProgressChapter() ?? GetLastReadChapter();
            if (lastRead != null)
            {
                SelectedChapter = lastRead;
            }
        }
        private Chapter? GetInitialProgressChapter()
        {
            if (_initialProgress == null || Chapters == null || Chapters.Count == 0)
            {
                return null;
            }

            var progress = _initialProgress;
            Chapter? indexCandidate = null;

            if (progress.ChapterIndex >= 0 && progress.ChapterIndex < Chapters.Count)
            {
                indexCandidate = Chapters[progress.ChapterIndex];
                if (IsChapterMatchForProgress(indexCandidate, progress))
                {
                    UpdateInitialProgressIndex(progress, progress.ChapterIndex, indexCandidate);
                    return indexCandidate;
                }
            }

            var titleCandidate = TryResolveChapterByTitle(progress.ChapterTitle);
            if (titleCandidate != null)
            {
                var resolvedIndex = Chapters.IndexOf(titleCandidate);
                if (resolvedIndex >= 0)
                {
                    UpdateInitialProgressIndex(progress, resolvedIndex, titleCandidate);
                }

                return titleCandidate;
            }

            return indexCandidate;
        }


        private static bool IsChapterMatchForProgress(Chapter? chapter, MangaReadingProgress progress)
        {
            if (chapter == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(progress.ChapterTitle))
            {
                return true;
            }

            var progressTitle = NormalizeChapterTitle(progress.ChapterTitle);
            if (string.IsNullOrEmpty(progressTitle))
            {
                return true;
            }

            var chapterTitle = NormalizeChapterTitle(chapter.Title);
            if (string.IsNullOrEmpty(chapterTitle))
            {
                return true;
            }

            return string.Equals(chapterTitle, progressTitle, StringComparison.OrdinalIgnoreCase);
        }


        private Chapter? TryResolveChapterByTitle(string? title)
        {
            var normalizedTitle = NormalizeChapterTitle(title);
            if (string.IsNullOrEmpty(normalizedTitle))
            {
                return null;
            }

            return Chapters.FirstOrDefault(chapter =>
                !string.IsNullOrEmpty(chapter?.Title) &&
                string.Equals(NormalizeChapterTitle(chapter.Title), normalizedTitle, StringComparison.OrdinalIgnoreCase));
        }

        private void UpdateInitialProgressIndex(MangaReadingProgress progress, int newIndex, Chapter chapter)
        {
            var updatedTitle = string.IsNullOrWhiteSpace(chapter?.Title) ? progress.ChapterTitle : chapter.Title;
            var shouldUpdate = newIndex != progress.ChapterIndex
                || !string.Equals(NormalizeChapterTitle(progress.ChapterTitle), NormalizeChapterTitle(updatedTitle), StringComparison.OrdinalIgnoreCase);

            if (!shouldUpdate)
            {
                return;
            }

            var updated = progress with
            {
                ChapterIndex = newIndex,
                ChapterTitle = updatedTitle
            };

            _initialProgress = updated;

            if (_userSettings != null)
            {
                var key = CreateProgressKey();
                if (!key.IsEmpty)
                {
                    _userSettings.SetReadingProgress(key, updated);
                }
            }
        }


        private void ToggleBookmark()
        {
            if (_userSettings == null)
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
            }
            else
            {
                var title = Manga?.Title ?? key.Title;
                var cover = Manga?.CoverImageUrl;
                var entry = _userSettings.AddOrUpdateBookmark(key, title, cover);
                _bookmarkStorageKey = entry?.StorageKey;
            }
        }

        private bool CanToggleBookmark()
        {
            return !_disposed &&
                   _userSettings != null &&
                   Manga != null &&
                   !string.IsNullOrWhiteSpace(Manga.Title);
        }

        private BookmarkKey CreateBookmarkKey()
        {
            var title = Manga?.Title ?? string.Empty;
            var sourceKey = string.IsNullOrWhiteSpace(SourceKey) ? null : SourceKey;
            var mangaUrl = string.IsNullOrWhiteSpace(Manga?.Url) ? null : Manga.Url;
            return new BookmarkKey(title, sourceKey, mangaUrl);
        }

        private ReadingProgressKey CreateProgressKey()
        {
            return new ReadingProgressKey(Manga?.Title, SourceKey, Manga?.Url);
        }


        private void UpdateBookmarkState()
        {
            if (_userSettings == null)
            {
                _bookmarkStorageKey = null;
                IsBookmarked = false;
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

        private void OnBookmarksChanged(object? sender, EventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(UpdateBookmarkState);
            }
            else
            {
                UpdateBookmarkState();
            }
        }

        private static string NormalizeChapterTitle(string? title)
        {
            return string.IsNullOrWhiteSpace(title)
                ? string.Empty
                : title.Trim();
        }

        private void OnNavigationStateChanged(object sender, EventArgs e)
        {
            CommandManager.InvalidateRequerySuggested();
        }

        public Task<IReadOnlyList<ChapterImage>> GetChapterImagesAsync(int index)
        {
            if (index < 0 || index >= Chapters.Count)
            {
                return Task.FromResult<IReadOnlyList<ChapterImage>>(Array.Empty<ChapterImage>());
            }

            var lazy = _chapterImageCache.GetOrAdd(index, CreateCacheEntry);
            TouchCache(index);
            return lazy.Value;
        }

        public async Task PrefetchChapterAsync(int index)
        {
            if (index < 0 || index >= Chapters.Count)
            {
                return;
            }

            try
            {
                await GetChapterImagesAsync(index).ConfigureAwait(false);
                Debug.WriteLine($"Prefetched chapter at index {index}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to prefetch chapter at index {index}: {ex.Message}");
            }
        }

        private async Task<IReadOnlyList<ChapterImage>> FetchChapterImagesInternalAsync(int index)
        {
            if (index < 0 || index >= Chapters.Count)
            {
                return Array.Empty<ChapterImage>();
            }

            var chapter = Chapters[index];
            if (chapter == null || string.IsNullOrEmpty(chapter.Url))
            {
                return Array.Empty<ChapterImage>();
            }

            try
            {
                var images = await _source.FetchChapterImages(chapter.Url, _cts.Token).ConfigureAwait(false);
                Debug.WriteLine("Images: " + (images != null ? images.Count.ToString() : "null"));
                Debug.WriteLine("Image 1: " + (images != null && images.Count > 0 ? images[0].ImageUrl : "N/A"));
                return images?.ToList() ?? new List<ChapterImage>();
            }
            catch (OperationCanceledException)
            {
                RemoveFromCache(index);
                Debug.WriteLine($"Fetching images cancelled for chapter {chapter.Title}");
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to fetch images for chapter {chapter.Title}: {ex.Message}");
                RemoveFromCache(index);
                throw;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_userSettings != null)
            {
                _userSettings.BookmarksChanged -= OnBookmarksChanged;
            }
            WeakEventManager<INavigationService, EventArgs>.RemoveHandler(_navigationService, nameof(INavigationService.CanGoBackChanged), OnNavigationStateChanged);
            _cts.Cancel();
            _cts.Dispose();
            ClearChapterCache();
            if (_chapters != null)
            {
                _chapters.CollectionChanged -= OnChaptersCollectionChanged;
            }

            if (_filteredChapters != null)
            {
                _filteredChapters.Filter = null;

                if (_filteredChapters is INotifyCollectionChanged notify)
                {
                    notify.CollectionChanged -= OnFilteredChaptersCollectionChanged;
                }
            }
        }

        private Lazy<Task<IReadOnlyList<ChapterImage>>> CreateCacheEntry(int index)
        {
            return new Lazy<Task<IReadOnlyList<ChapterImage>>>(() => FetchChapterImagesInternalAsync(index), LazyThreadSafetyMode.ExecutionAndPublication);
        }

        private void TouchCache(int index)
        {
            lock (_cacheLock)
            {
                if (_cacheNodes.TryGetValue(index, out var node))
                {
                    _cacheOrder.Remove(node);
                    _cacheOrder.AddFirst(node);
                }
                else
                {
                    var newNode = _cacheOrder.AddFirst(index);
                    _cacheNodes[index] = newNode;

                    while (_cacheOrder.Count > MaxCachedChapters)
                    {
                        var tail = _cacheOrder.Last;
                        if (tail == null)
                        {
                            break;
                        }

                        _cacheOrder.RemoveLast();
                        _cacheNodes.Remove(tail.Value);
                        _chapterImageCache.TryRemove(tail.Value, out _);
                    }
                }
            }
        }

        private void RemoveFromCache(int index)
        {
            _chapterImageCache.TryRemove(index, out _);
            lock (_cacheLock)
            {
                if (_cacheNodes.TryGetValue(index, out var node))
                {
                    _cacheOrder.Remove(node);
                    _cacheNodes.Remove(index);
                }
            }
        }

        private void ClearChapterCache()
        {
            _chapterImageCache.Clear();
            lock (_cacheLock)
            {
                _cacheOrder.Clear();
                _cacheNodes.Clear();
            }
        }
    }
}