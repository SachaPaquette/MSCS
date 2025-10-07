using MSCS.Commands;
using MSCS.Interfaces;
using MSCS.Models;
using MSCS.Services;
using MSCS.Sources;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace MSCS.ViewModels
{
    public class ChapterListViewModel : BaseViewModel, IDisposable
    {
        private readonly IMangaSource _source;
        private readonly INavigationService _navigationService;
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentDictionary<int, Lazy<Task<IReadOnlyList<ChapterImage>>>> _chapterImageCache = new();
        private readonly LinkedList<int> _cacheOrder = new();
        private readonly Dictionary<int, LinkedListNode<int>> _cacheNodes = new();
        private readonly object _cacheLock = new();
        private bool _disposed;
        private readonly IAniListService? _aniListService;
        private readonly UserSettings? _userSettings;
        private const int MaxCachedChapters = 4;
        private readonly bool _autoOpenOnLoad;
        private bool _hasAutoOpened;
        private Manga _manga;
        private ObservableCollection<Chapter> _chapters = new();
        private Chapter? _selectedChapter;
        private MangaReadingProgress? _initialProgress;
        public Manga Manga
        {
            get => _manga;
            set => SetProperty(ref _manga, value);
        }

        public ObservableCollection<Chapter> Chapters
        {
            get => _chapters;
            set => SetProperty(ref _chapters, value);
        }

        public Chapter? SelectedChapter
        {
            get => _selectedChapter;
            set
            {
                if (SetProperty(ref _selectedChapter, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public ICommand OpenChapterCommand { get; }
        public ICommand BackCommand { get; }

        public string SourceKey { get; }

        public ChapterListViewModel(
            IMangaSource source,
            INavigationService navigationService,
            Manga manga,
            IAniListService? aniListService,
            UserSettings userSettings,
            string? sourceKey = null,
            bool autoOpenOnLoad = false,
            MangaReadingProgress? initialProgress = null)
        {
            _source = source;
            _navigationService = navigationService;
            Manga = manga;
            _aniListService = aniListService;
            _userSettings = userSettings;
            SourceKey = sourceKey ?? string.Empty;
            _autoOpenOnLoad = autoOpenOnLoad;
            _initialProgress = initialProgress;

            OpenChapterCommand = new RelayCommand(_ => OpenChapter(), _ => SelectedChapter != null);
            BackCommand = new RelayCommand(_ => _navigationService.GoBack(), _ => _navigationService.CanGoBack);
            WeakEventManager<INavigationService, EventArgs>.AddHandler(_navigationService, nameof(INavigationService.CanGoBackChanged), OnNavigationStateChanged);

            _ = LoadChaptersAsync();
        }

        public ChapterListViewModel() {
            SourceKey = string.Empty;
        }

        private async Task LoadChaptersAsync()
        {
            if (string.IsNullOrEmpty(Manga?.Url))
            {
                return;
            }

            try
            {
                var chapters = await _source.GetChaptersAsync(Manga.Url, _cts.Token);
                ClearChapterCache();
                Chapters = new ObservableCollection<Chapter>(chapters);
                Debug.WriteLine($"Loaded {Chapters.Count} chapters for {Manga.Title}");
                RestoreLastReadChapter();
                MaybeAutoOpenChapter();
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"Chapter load cancelled for {Manga?.Title}");
            }
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
            OpenChapter();
        }

        private async void OpenChapter()
        {
            var chapterToOpen = EnsureSelectedChapter();
            if (chapterToOpen == null || string.IsNullOrEmpty(chapterToOpen.Url))
            {
                return;
            }

            Debug.WriteLine($"Opening chapter: {chapterToOpen.Title} ({chapterToOpen.Url})");
            int index = Chapters.IndexOf(chapterToOpen);

            var images = await GetChapterImagesAsync(index);
            if (images != null && images.Any())
            {
                var readerVM = new ReaderViewModel(
                    images,
                    chapterToOpen.Title,
                    _navigationService,
                    this,
                    index,
                    _aniListService,
                    _userSettings,
                    _initialProgress);

                _initialProgress = null;

                _navigationService.NavigateToViewModel(readerVM);
                Debug.WriteLine("Navigating to ReaderViewModel with images loaded.");
                _ = PrefetchChapterAsync(index + 1);
            }
        }

        private Chapter? EnsureSelectedChapter()
        {
            if (SelectedChapter != null)
            {
                return SelectedChapter;
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
            if (_userSettings == null || string.IsNullOrWhiteSpace(Manga?.Title))
            {
                return null;
            }

            if (_userSettings.TryGetReadingProgress(Manga.Title, out var progress))
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
            if (_initialProgress == null)
            {
                return null;
            }

            var targetIndex = _initialProgress.ChapterIndex;
            if (targetIndex < 0 || targetIndex >= Chapters.Count)
            {
                return null;
            }

            return Chapters[targetIndex];
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

            Debug.WriteLine($"Fetching chapter images: {chapter.Title} ({chapter.Url})");
            try
            {
                var images = await _source.FetchChapterImages(chapter.Url, _cts.Token).ConfigureAwait(false);
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
            WeakEventManager<INavigationService, EventArgs>.RemoveHandler(_navigationService, nameof(INavigationService.CanGoBackChanged), OnNavigationStateChanged);
            _cts.Cancel();
            _cts.Dispose();
            ClearChapterCache();
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