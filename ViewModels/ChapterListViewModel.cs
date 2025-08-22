using MSCS.Commands;
using MSCS.Models;
using MSCS.Interfaces;
using MSCS.Sources;
using System.Collections.ObjectModel;
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
            set => SetProperty(ref _selectedChapter, value);
        }

        public ICommand OpenChapterCommand { get; }
        public ICommand BackCommand { get; }

        public ChapterListViewModel(IMangaSource source, INavigationService navigationService, Manga manga)
        {
            _source = source;
            _navigationService = navigationService;
            Manga = manga;

            OpenChapterCommand = new RelayCommand(_ => OpenChapter(), _ => SelectedChapter != null);
            BackCommand = new RelayCommand(_ => _navigationService.NavigateToSingleton<MangaListViewModel>());

            _ = LoadChaptersAsync(); 
        }

        public ChapterListViewModel() { }

        private async Task LoadChaptersAsync()
        {
            if (string.IsNullOrEmpty(Manga?.Url)) return;

            var chapters = await _source.GetChaptersAsync(Manga.Url, _cts.Token);
            Chapters = new ObservableCollection<Chapter>(chapters);
            Debug.WriteLine($"Loaded {Chapters.Count} chapters for {Manga.Title}");
        }

        private async void OpenChapter()
        {
            if (SelectedChapter == null || string.IsNullOrEmpty(SelectedChapter.Url)) return;

            Debug.WriteLine($"Opening chapter: {SelectedChapter.Title} ({SelectedChapter.Url})");
            int index = Chapters.IndexOf(SelectedChapter) + 1;

            var images = await _source.FetchChapterImages(SelectedChapter.Url, _cts.Token);
            if (images != null && images.Any())
            {
                // Convert to ObservableCollection for the reader VM/UI
                var imagesObs = new ObservableCollection<ChapterImage>(images);

                var readerVM = new ReaderViewModel(
                    imagesObs,
                    SelectedChapter.Title,
                    _navigationService,
                    this,
                    index);

                _navigationService.NavigateToViewModel(readerVM);
                Debug.WriteLine("Navigating to ReaderViewModel with images loaded.");
            }
        }

        public async Task<IReadOnlyList<ChapterImage>> GetNextChapterImages(int index)
        {
            if (index < 0 || index >= Chapters.Count - 1)
                return Array.Empty<ChapterImage>();

            var nextChapter = Chapters[index + 1];
            Debug.WriteLine($"Fetching next chapter: {nextChapter.Title} ({nextChapter.Url})");
            return await _source.FetchChapterImages(nextChapter.Url, _cts.Token);
        }
    }
}
