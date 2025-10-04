using MSCS.Commands;
using MSCS.Helpers;
using MSCS.Interfaces;
using MSCS.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
        public int LoadedImages => _loadedCount;
        public int TotalImages => _allImages.Count;
        public double LoadingProgress => _allImages.Count == 0 ? 0d : (double)_loadedCount / _allImages.Count;
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
        private double _scrollProgress;
        public double ScrollProgress
        {
            get => _scrollProgress;
            set => SetProperty(ref _scrollProgress, Math.Clamp(value, 0.0, 1.0));
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

        private ObservableCollection<Chapter> _chapters = new();
        public ObservableCollection<Chapter> Chapters
        {
            get => _chapters;
            private set => SetProperty(ref _chapters, value);
        }

        private Chapter? _selectedChapter;
        private bool _isUpdatingSelectedChapter;
        public Chapter? SelectedChapter
        {
            get => _selectedChapter;
            set
            {
                if (SetProperty(ref _selectedChapter, value) && !_isUpdatingSelectedChapter)
                {
                    _ = OnSelectedChapterChangedAsync(value);
                }
            }
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
            Chapters = new ObservableCollection<Chapter>();
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

            if (_chapterListViewModel != null)
            {
                Chapters = _chapterListViewModel.Chapters;
                PropertyChangedEventManager.AddHandler(_chapterListViewModel, ChapterListViewModelOnPropertyChanged, string.Empty);
                InitializeSelectedChapter();
            }
            else
            {
                Chapters = new ObservableCollection<Chapter>();
            }

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
            OnPropertyChanged(nameof(LoadedImages));
            OnPropertyChanged(nameof(TotalImages));
            OnPropertyChanged(nameof(LoadingProgress));
            Debug.WriteLine($"Loaded {_loadedCount} / {_allImages.Count} images");
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
            ResetImages(images);
            _ = _chapterListViewModel.PrefetchChapterAsync(newIndex + 1);
            Debug.WriteLine($"Navigated to chapter {newIndex} with {images.Count} images.");
            CommandManager.InvalidateRequerySuggested();
            UpdateChapterSelection(newIndex);
            return true;
        }

        private void ResetImages(IEnumerable<ChapterImage> images)
        {
            _allImages.Clear();
            ImageUrls.Clear();
            _loadedCount = 0;
            ScrollProgress = 0;
            foreach (var img in images)
            {
                _allImages.Add(img);
            }
            OnPropertyChanged(nameof(TotalImages));
            OnPropertyChanged(nameof(LoadingProgress));
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
        public async Task GoToNextChapterAsync()
        {
            if (!CanGoToNextChapter())
            {
                return;
            }
            await TryMoveToChapterAsync(_currentChapterIndex + 1);
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

            NextChapterCommand = new AsyncRelayCommand(_ => GoToNextChapterAsync(), _ => CanGoToNextChapter());

            OnPropertyChanged(nameof(GoBackCommand));
            OnPropertyChanged(nameof(GoHomeCommand));
            OnPropertyChanged(nameof(NextChapterCommand));
        }

        private void OnNavigationCanGoBackChanged(object sender, EventArgs e)
    {
        CommandManager.InvalidateRequerySuggested();
    }

        private async Task OnSelectedChapterChangedAsync(Chapter? chapter)
        {
            if (chapter == null || _chapterListViewModel == null)
            {
                return;
            }

            try
            {
                int index = _chapterListViewModel.Chapters.IndexOf(chapter);
                if (index < 0 || index == _currentChapterIndex)
                {
                    return;
                }

                await TryMoveToChapterAsync(index);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to change chapter from sidebar: {ex.Message}");
            }
        }

        private void InitializeSelectedChapter()
        {
            if (_chapterListViewModel == null)
            {
                return;
            }

            if (_currentChapterIndex >= 0 && _currentChapterIndex < _chapterListViewModel.Chapters.Count)
            {
                _isUpdatingSelectedChapter = true;
                SelectedChapter = _chapterListViewModel.Chapters[_currentChapterIndex];
                ChapterTitle = SelectedChapter?.Title ?? ChapterTitle;
                _isUpdatingSelectedChapter = false;
            }
        }

        private void UpdateChapterSelection(int index)
        {
            if (_chapterListViewModel == null)
            {
                return;
            }

            if (index >= 0 && index < _chapterListViewModel.Chapters.Count)
            {
                _isUpdatingSelectedChapter = true;
                SelectedChapter = _chapterListViewModel.Chapters[index];
                ChapterTitle = SelectedChapter?.Title ?? ChapterTitle;
                _isUpdatingSelectedChapter = false;
            }
        }

        private void ChapterListViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChapterListViewModel.Chapters) && _chapterListViewModel != null)
            {
                Chapters = _chapterListViewModel.Chapters;
                InitializeSelectedChapter();
            }
        }
    }
}
