using MSCS.Commands;
using MSCS.Models;
using MSCS.Interfaces;
using MSCS.Sources;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Diagnostics;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;

namespace MSCS.ViewModels
{
    public class ChapterListViewModel : BaseViewModel
    {
        private readonly IMangaSource _source;
        private readonly INavigationService _navigationService;
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentDictionary<int, Task<IReadOnlyList<ChapterImage>>> _chapterImageCache = new();

        private Manga _manga;
        private ObservableCollection<Chapter> _chapters = new();
        private Chapter _selectedChapter;

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

        public Chapter SelectedChapter
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

        public ChapterListViewModel(IMangaSource source, INavigationService navigationService, Manga manga)
        {
            _source = source;
            _navigationService = navigationService;
            Manga = manga;

            OpenChapterCommand = new RelayCommand(_ => OpenChapter(), _ => SelectedChapter != null);
            BackCommand = new RelayCommand(_ => _navigationService.GoBack(), _ => _navigationService.CanGoBack);
            WeakEventManager<INavigationService, EventArgs>.AddHandler(_navigationService, nameof(INavigationService.CanGoBackChanged), OnNavigationStateChanged);

            _ = LoadChaptersAsync();
        }

        public ChapterListViewModel() { }

        private async Task LoadChaptersAsync()
        {
            if (string.IsNullOrEmpty(Manga?.Url)) return;

            var chapters = await _source.GetChaptersAsync(Manga.Url, _cts.Token);
            _chapterImageCache.Clear();
            Chapters = new ObservableCollection<Chapter>(chapters);
            Debug.WriteLine($"Loaded {Chapters.Count} chapters for {Manga.Title}");
        }

        private async void OpenChapter()
        {
            if (SelectedChapter == null || string.IsNullOrEmpty(SelectedChapter.Url)) return;

            Debug.WriteLine($"Opening chapter: {SelectedChapter.Title} ({SelectedChapter.Url})");
            int index = Chapters.IndexOf(SelectedChapter);

            var images = await GetChapterImagesAsync(index);
            if (images != null && images.Any())
            {
                var readerVM = new ReaderViewModel(
                    images,
                    SelectedChapter.Title,
                    _navigationService,
                    this,
                    index);

                _navigationService.NavigateToViewModel(readerVM);
                Debug.WriteLine("Navigating to ReaderViewModel with images loaded.");
                _ = PrefetchChapterAsync(index + 1);
            }
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

            return _chapterImageCache.GetOrAdd(index, FetchChapterImagesInternalAsync);
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
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to fetch images for chapter {chapter.Title}: {ex.Message}");
                _chapterImageCache.TryRemove(index, out _);
                throw;
            }
        }
    }
}