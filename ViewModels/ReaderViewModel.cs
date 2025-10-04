using MSCS.Commands;
using MSCS.Helpers;
using MSCS.Interfaces;
using MSCS.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace MSCS.ViewModels
{
    public class ReaderViewModel : BaseViewModel
    {
        private readonly List<ChapterImage> _allImages;
        private readonly ChapterListViewModel? _chapterListViewModel;
        private readonly INavigationService? _navigationService;
        private int _loadedCount;
        private int _currentChapterIndex;
        public int RemainingImages => _allImages.Count - _loadedCount;
        private double _widthFactor = Constants.DefaultWidthFactor;
        public double WidthFactor
        {
            get => _widthFactor;
            set => SetProperty(ref _widthFactor, Math.Clamp(value, 0.3, 1.0));
        }

        private double _maxPageWidth = Constants.DefaultMaxPageWidth;
        public double MaxPageWidth
        {
            get => _maxPageWidth;
            set => SetProperty(ref _maxPageWidth, Math.Max(400, value));
        }
        private bool _isSidebarOpen;
        public bool IsSidebarOpen
        {
            get => _isSidebarOpen;
            set => SetProperty(ref _isSidebarOpen, value);
        }

        private string _chapterTitle = string.Empty;
        public string ChapterTitle
        {
            get => _chapterTitle;
            private set => SetProperty(ref _chapterTitle, value);
        }

        public ObservableCollection<ChapterImage> ImageUrls { get; }
        public ICommand GoBackCommand { get; private set; }
        public ICommand GoHomeCommand { get; private set; }
        public ICommand NextChapterCommand { get; private set; }

        public ReaderViewModel()
        {
            Debug.WriteLine("ReaderViewModel initialized with no images");
            _navigationService = null;
            _chapterListViewModel = null;
            _allImages = new List<ChapterImage>();
            ImageUrls = new ObservableCollection<ChapterImage>();
            ConfigureNavigationCommands();
        }

        public ReaderViewModel(
            IEnumerable<ChapterImage>? imageUrls,
            string title,
            INavigationService navigationService,
            ChapterListViewModel chapterListViewModel,
            int currentChapterIndex)
        {
            _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
            _chapterListViewModel = chapterListViewModel ?? throw new ArgumentNullException(nameof(chapterListViewModel));
            _currentChapterIndex = currentChapterIndex;

            _allImages = imageUrls?.ToList() ?? new List<ChapterImage>();
            ImageUrls = new ObservableCollection<ChapterImage>();
            ChapterTitle = title ?? string.Empty;
            _loadedCount = 0;

            ConfigureNavigationCommands();

            Debug.WriteLine($"ReaderViewModel initialized with {_allImages.Count} images");
            Debug.WriteLine($"Current chapter index {_currentChapterIndex}");

            LoadMoreImages();  // initial batch
            _ = _chapterListViewModel.PrefetchChapterAsync(_currentChapterIndex + 1);
        }


        public void LoadMoreImages()
        {
            int remaining = _allImages.Count - _loadedCount;
            if (remaining <= 0) return;

            int countToLoad = Math.Min(Constants.DefaultLoadedBatchSize, remaining);
            for (int i = 0; i < countToLoad; i++)
            {
                ImageUrls.Add(_allImages[_loadedCount + i]);
            }
            _loadedCount += countToLoad;
            OnPropertyChanged(nameof(RemainingImages));
            Debug.WriteLine($"Loaded {_loadedCount} / {_allImages.Count} images");
        }

        public async Task GoToNextChapterAsync()
        {
            Debug.WriteLine("Navigating to next chapter...");

            if (!CanGoToNextChapter())
            {
                Debug.WriteLine("No next chapter available.");
                return;
            }

            if (!await TryMoveToChapterAsync(_currentChapterIndex + 1))
            {
                Debug.WriteLine("No next chapter available.");
            }
            CommandManager.InvalidateRequerySuggested();
        }

        private async Task<bool> TryMoveToChapterAsync(int newIndex)
        {
            if (_chapterListViewModel == null)
            {
                Debug.WriteLine("Chapter list view model is unavailable for navigation.");
                return false;
            }

            if (newIndex < 0 || newIndex >= _chapterListViewModel.Chapters.Count)
            {
                Debug.WriteLine($"Requested chapter index {newIndex} is out of range.");
                return false;
            }

            var images = await _chapterListViewModel.GetChapterImagesAsync(newIndex);
            if (images == null || images.Count == 0)
            {
                Debug.WriteLine($"No images returned for chapter at index {newIndex}.");
                return false;
            }

            _currentChapterIndex = newIndex;
            if (newIndex < _chapterListViewModel.Chapters.Count)
            {
                ChapterTitle = _chapterListViewModel.Chapters[newIndex].Title;
            }

            ResetImages(images);
            _ = _chapterListViewModel.PrefetchChapterAsync(newIndex + 1);
            Debug.WriteLine($"Navigated to chapter {newIndex} with {images.Count} images.");
            CommandManager.InvalidateRequerySuggested();
            return true;
        }

        private void ResetImages(IEnumerable<ChapterImage> images)
        {
            _allImages.Clear();
            ImageUrls.Clear();
            _loadedCount = 0;

            foreach (var img in images)
            {
                _allImages.Add(img);
            }

            LoadMoreImages();
        }

        private bool CanGoToNextChapter()
        {
            if (_chapterListViewModel == null)
            {
                return false;
            }

            return _currentChapterIndex + 1 < _chapterListViewModel.Chapters.Count;
        }

        private void ConfigureNavigationCommands()
        {
            if (_navigationService == null)
            {
                GoBackCommand = new RelayCommand(_ => { }, _ => false);
                GoHomeCommand = new RelayCommand(_ => { }, _ => false);
            }
            else
            {
                GoBackCommand = new RelayCommand(_ => _navigationService.GoBack(), _ => _navigationService.CanGoBack);
                GoHomeCommand = new RelayCommand(_ => _navigationService.NavigateToSingleton<MangaListViewModel>());
                WeakEventManager<INavigationService, EventArgs>.AddHandler(_navigationService, nameof(INavigationService.CanGoBackChanged), OnNavigationCanGoBackChanged);
            }

            NextChapterCommand = new RelayCommand(async _ => await GoToNextChapterAsync(), _ => CanGoToNextChapter());

            OnPropertyChanged(nameof(GoBackCommand));
            OnPropertyChanged(nameof(GoHomeCommand));
            OnPropertyChanged(nameof(NextChapterCommand));
        }

        private void OnNavigationCanGoBackChanged(object sender, EventArgs e)
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }
}