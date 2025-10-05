using MSCS.Commands;
using MSCS.Helpers;
using MSCS.Interfaces;
using MSCS.Models;
using MSCS.Views;
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
        private readonly IAniListService? _aniListService;
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

        private string _mangaTitle = string.Empty;
        public string MangaTitle
        {
            get => _mangaTitle;
            private set => SetProperty(ref _mangaTitle, value);
        }

        private AniListTrackingInfo? _trackingInfo;
        public AniListTrackingInfo? TrackingInfo
        {
            get => _trackingInfo;
            private set
            {
                if (SetProperty(ref _trackingInfo, value))
                {
                    OnPropertyChanged(nameof(IsAniListTracked));
                    OnPropertyChanged(nameof(AniListButtonText));
                }
            }
        }
        public bool IsAniListAvailable => _aniListService != null;
        public bool IsAniListTracked => TrackingInfo != null;
        public string AniListButtonText => IsAniListTracked ? "Tracked" : "Track";
        public ICommand AniListTrackCommand { get; private set; } = new RelayCommand(_ => { });

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
                    _ = HandleSelectedChapterChangedAsync(value);
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
            _aniListService = null;
            _allImages = new List<ChapterImage>();
            ImageUrls = new ObservableCollection<ChapterImage>();
            Chapters = new ObservableCollection<Chapter>();
            MangaTitle = string.Empty;
            InitializeNavigationCommands();
            InitializeAniListIntegration();
        }

        public ReaderViewModel(
            IEnumerable<ChapterImage>? imageUrls,
            string title,
            INavigationService navigationService,
            ChapterListViewModel chapterListViewModel,
            int currentChapterIndex,
            IAniListService? aniListService)
        {
            _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
            _chapterListViewModel = chapterListViewModel ?? throw new ArgumentNullException(nameof(chapterListViewModel));
            _currentChapterIndex = currentChapterIndex;
            _aniListService = aniListService;

            _allImages = imageUrls?.ToList() ?? new List<ChapterImage>();
            ImageUrls = new ObservableCollection<ChapterImage>();
            ChapterTitle = title ?? string.Empty;
            _loadedCount = 0;
            MangaTitle = _chapterListViewModel?.Manga?.Title ?? string.Empty;
            if (string.IsNullOrWhiteSpace(MangaTitle))
            {
                MangaTitle = title ?? string.Empty;
            }

            if (_chapterListViewModel != null)
            {
                Chapters = _chapterListViewModel.Chapters;
                PropertyChangedEventManager.AddHandler(_chapterListViewModel, ChapterListViewModelOnPropertyChanged, string.Empty);
                SelectInitialChapter();
            }
            else
            {
                Chapters = new ObservableCollection<Chapter>();
            }

            InitializeNavigationCommands();
            InitializeAniListIntegration();

            Debug.WriteLine($"ReaderViewModel initialized with {_allImages.Count} images");
            Debug.WriteLine($"Current chapter index {_currentChapterIndex}");

            LoadMoreImages();  // initial batch
            _ = _chapterListViewModel?.PrefetchChapterAsync(_currentChapterIndex + 1);
            _ = UpdateAniListProgressAsync();
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
            UpdateSelectedChapter(newIndex);
            _ = UpdateAniListProgressAsync();
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
        private void InitializeNavigationCommands()
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

        private async Task HandleSelectedChapterChangedAsync(Chapter? chapter)
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

        private void SelectInitialChapter()
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

        private void UpdateSelectedChapter(int index)
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
                _ = UpdateAniListProgressAsync();
            }
        }

        // Legacy wrappers kept for compatibility with older partial classes or bindings that still
        // reference the previous helper names. They now forward to the renamed implementations.
        private void ConfigureNavigationCommands() => InitializeNavigationCommands();
        private void InitializeSelectedChapter() => SelectInitialChapter();
        private Task OnSelectedChapterChangedAsync(Chapter? chapter) => HandleSelectedChapterChangedAsync(chapter);

        private void ChapterListViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChapterListViewModel.Chapters) && _chapterListViewModel != null)
            {
                Chapters = _chapterListViewModel.Chapters;
                SelectInitialChapter();
            }
        }

        private void InitializeAniListIntegration()
        {
            AniListTrackCommand = new AsyncRelayCommand(TrackWithAniListAsync, () => _aniListService != null);
            OnPropertyChanged(nameof(AniListTrackCommand));
            OnPropertyChanged(nameof(IsAniListAvailable));
            if (_aniListService != null && !string.IsNullOrWhiteSpace(MangaTitle))
            {
                WeakEventManager<IAniListService, EventArgs>.AddHandler(_aniListService, nameof(IAniListService.TrackingChanged), OnAniListTrackingChanged);
                if (_aniListService.TryGetTracking(MangaTitle, out var info))
                {
                    TrackingInfo = info;
                }
            }
        }

        private void OnAniListTrackingChanged(object? sender, EventArgs e)
        {
            if (_aniListService == null || string.IsNullOrWhiteSpace(MangaTitle))
            {
                return;
            }

            if (_aniListService.TryGetTracking(MangaTitle, out var info))
            {
                TrackingInfo = info;
            }
        }

        private async Task TrackWithAniListAsync()
        {
            if (_aniListService == null)
            {
                System.Windows.MessageBox.Show("AniList service is not available.", "AniList", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(MangaTitle))
            {
                System.Windows.MessageBox.Show("Unable to determine the manga title for tracking.", "AniList", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!_aniListService.IsAuthenticated)
            {
                System.Windows.MessageBox.Show("Connect your AniList account from the Settings tab before tracking a series.", "AniList", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var trackingViewModel = new AniListTrackingViewModel(_aniListService, MangaTitle, MangaTitle);
            var dialog = new AniListTrackingWindow(trackingViewModel);
            if (System.Windows.Application.Current?.MainWindow != null)
            {
                dialog.Owner = System.Windows.Application.Current.MainWindow;
            }

            var result = dialog.ShowDialog();
            if (result == true && trackingViewModel.TrackingInfo != null)
            {
                TrackingInfo = trackingViewModel.TrackingInfo;
                await UpdateAniListProgressAsync().ConfigureAwait(true);
            }
        }

        private async Task UpdateAniListProgressAsync()
        {
            if (_aniListService == null || TrackingInfo == null || string.IsNullOrWhiteSpace(MangaTitle))
            {
                return;
            }

            var progress = GetProgressForChapter(SelectedChapter);
            if (progress <= 0)
            {
                return;
            }

            try
            {
                await _aniListService.UpdateProgressAsync(MangaTitle, progress).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to update AniList progress: {ex.Message}");
            }
        }

        private int GetProgressForChapter(Chapter? chapter)
        {
            if (chapter == null)
            {
                return 0;
            }

            if (chapter.Number > 0)
            {
                var rounded = (int)Math.Round(chapter.Number, MidpointRounding.AwayFromZero);
                return Math.Max(1, rounded);
            }

            if (_chapterListViewModel != null)
            {
                var idx = _chapterListViewModel.Chapters.IndexOf(chapter);
                if (idx >= 0)
                {
                    return idx + 1;
                }
            }

            return _currentChapterIndex + 1;
        }
    }
}