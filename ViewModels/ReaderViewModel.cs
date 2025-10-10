using MSCS.Commands;
using MSCS.Enums;
using MSCS.Helpers;
using MSCS.Interfaces;
using MSCS.Models;
using MSCS.Services;
using MSCS.ViewModels;
using System.Windows.Media;
using MSCS.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace MSCS.ViewModels
{
    public class ReaderViewModel : BaseViewModel
    {
        private readonly List<ChapterImage> _allImages;
        private readonly ChapterListViewModel? _chapterListViewModel;
        private readonly INavigationService? _navigationService;
        private readonly IAniListService? _aniListService;
        private readonly UserSettings? _userSettings;
        private readonly SemaphoreSlim _imageLoadSemaphore = new(1, 1);
        private CancellationTokenSource _imageLoadCts = new();
        private bool _isChapterNavigationInProgress;
        public event EventHandler? ChapterChanged;
        public event EventHandler<ScrollRestoreRequest>? ScrollRestoreRequested;
        private int _loadedCount;
        private int _currentChapterIndex;
        public int RemainingImages => _allImages.Count - _loadedCount;
        public int LoadedImages => _loadedCount;
        public int TotalImages => _allImages.Count;
        public double LoadingProgress => _allImages.Count == 0 ? 0d : (double)_loadedCount / _allImages.Count;
        private double _widthFactor = Constants.DefaultWidthFactor;
        private double _lastPersistedScrollProgress = double.NaN;
        private double _lastPersistedScrollOffset = double.NaN;
        private DateTime _lastProgressSaveUtc = DateTime.MinValue;
        private bool _isRestoringProgress;
        private bool _hasRestoredInitialProgress;
        private MangaReadingProgress? _initialProgress;
        private double? _pendingRestoreProgress;
        private double? _pendingRestoreOffset;
        private double _lastKnownScrollOffset;
        private double _lastKnownExtentHeight;
        private double _lastKnownViewportHeight;
        private static readonly SolidColorBrush MidnightBackground = CreateFrozenBrush("#0F151F");
        private static readonly SolidColorBrush MidnightSurface = CreateFrozenBrush("#111727");
        private static readonly SolidColorBrush BlackBackground = CreateFrozenBrush("#000000");
        private static readonly SolidColorBrush BlackSurface = CreateFrozenBrush("#050505");
        private static readonly SolidColorBrush SepiaBackground = CreateFrozenBrush("#21160C");
        private static readonly SolidColorBrush SepiaSurface = CreateFrozenBrush("#2B1D12");
        private static readonly SolidColorBrush HighContrastBackground = CreateFrozenBrush("#0B0B0B");
        private static readonly SolidColorBrush HighContrastSurface = CreateFrozenBrush("#1E1E1E");

        private static SolidColorBrush CreateFrozenBrush(string hex)
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        public double WidthFactor
        {
            get => _widthFactor;
            set
            {
                if (SetProperty(ref _widthFactor, Math.Clamp(value, 0.3, 1.0)))
                {
                    OnPropertyChanged(nameof(ZoomPercent));
                }
            }
        }

        private double _maxPageWidth = Constants.DefaultMaxPageWidth;
        public double MaxPageWidth
        {
            get => _maxPageWidth;
            set => SetProperty(ref _maxPageWidth, Math.Max(400, value));
        }

        private ReaderTheme _theme = ReaderTheme.Midnight;
        public ReaderTheme Theme
        {
            get => _theme;
            set
            {
                if (SetProperty(ref _theme, value))
                {
                    OnPropertyChanged(nameof(ReaderBackgroundBrush));
                    OnPropertyChanged(nameof(ReaderSurfaceBrush));
                }
            }
        }

        public System.Windows.Media.Brush ReaderBackgroundBrush => Theme switch
        {
            ReaderTheme.PureBlack => BlackBackground,
            ReaderTheme.Sepia => SepiaBackground,
            ReaderTheme.HighContrast => HighContrastBackground,
            _ => MidnightBackground
        };

        public System.Windows.Media.Brush ReaderSurfaceBrush => Theme switch
        {
            ReaderTheme.PureBlack => BlackSurface,
            ReaderTheme.Sepia => SepiaSurface,
            ReaderTheme.HighContrast => HighContrastSurface,
            _ => MidnightSurface
        };

        public double ZoomPercent => Math.Round(WidthFactor * 100);
        private double _scrollProgress;
        public double ScrollProgress => _scrollProgress;
        private bool _isSidebarOpen;
        public bool IsSidebarOpen
        {
            get => _isSidebarOpen;
            set => SetProperty(ref _isSidebarOpen, value);
        }

        public void UpdateScrollPosition(double verticalOffset, double extentHeight, double viewportHeight)
        {
            var clampedExtent = double.IsNaN(extentHeight) ? 0 : Math.Max(extentHeight, 0);
            var clampedViewport = double.IsNaN(viewportHeight) ? 0 : Math.Max(viewportHeight, 0);
            var scrollableHeight = Math.Max(clampedExtent - clampedViewport, 0);
            var maxOffset = scrollableHeight > 0 ? scrollableHeight : 0;
            var clampedOffset = double.IsNaN(verticalOffset) ? 0 : Math.Clamp(verticalOffset, 0, maxOffset);
            var progress = scrollableHeight > 0 ? Math.Clamp(clampedOffset / scrollableHeight, 0.0, 1.0) : 0.0;

            _lastKnownScrollOffset = clampedOffset;
            _lastKnownExtentHeight = clampedExtent;
            _lastKnownViewportHeight = clampedViewport;

            SetProperty(ref _scrollProgress, progress, nameof(ScrollProgress));

            if (_isRestoringProgress)
            {
                if (!HasReachedRestoreTarget(clampedOffset, progress))
                {
                    return;
                }

                _isRestoringProgress = false;
                _pendingRestoreProgress = null;
                _pendingRestoreOffset = null;
            }

            PersistReadingProgress();
        }

        private bool HasReachedRestoreTarget(double currentOffset, double currentProgress)
        {
            if (_pendingRestoreOffset.HasValue)
            {
                var tolerance = Math.Max(1.0, _lastKnownViewportHeight * 0.01);
                if (Math.Abs(currentOffset - _pendingRestoreOffset.Value) <= tolerance)
                {
                    return true;
                }
            }

            if (_pendingRestoreProgress.HasValue)
            {
                if (Math.Abs(currentProgress - _pendingRestoreProgress.Value) <= 0.01)
                {
                    return true;
                }
            }

            return false;
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
        public ObservableCollection<ChapterImage> ImageUrls { get; }
        public ICommand GoBackCommand { get; private set; } = new RelayCommand(_ => { }, _ => false);
        public ICommand GoHomeCommand { get; private set; } = new RelayCommand(_ => { }, _ => false);
        public ICommand NextChapterCommand { get; private set; } = new AsyncRelayCommand(_ => Task.CompletedTask, _ => false);
        public ICommand AniListTrackCommand { get; private set; } = new RelayCommand(_ => { });
        public ICommand IncreaseZoomCommand { get; private set; } = new RelayCommand(_ => { }, _ => false);
        public ICommand DecreaseZoomCommand { get; private set; } = new RelayCommand(_ => { }, _ => false);
        public ICommand ResetZoomCommand { get; private set; } = new RelayCommand(_ => { }, _ => false);
        public ICommand SetThemeCommand { get; private set; } = new RelayCommand(_ => { });

        public bool IsAniListAvailable => _aniListService != null;
        public bool IsAniListTracked => TrackingInfo != null;
        public string AniListButtonText => IsAniListTracked ? "Tracked" : "Track";

        public ReaderViewModel()
        {
            Debug.WriteLine("ReaderViewModel initialized with no images");
            _navigationService = null;
            _chapterListViewModel = null;
            _userSettings = null;
            _allImages = new List<ChapterImage>();
            ImageUrls = new ObservableCollection<ChapterImage>();
            Chapters = new ObservableCollection<Chapter>();
            InitializeNavigationCommands();
            InitializePreferenceCommands();
        }

        public ReaderViewModel(
            IEnumerable<ChapterImage>? imageUrls,
            string title,
            INavigationService navigationService,
            ChapterListViewModel chapterListViewModel,
            int currentChapterIndex,
            IAniListService? aniListService,
            UserSettings? userSettings = null,
            MangaReadingProgress? initialProgress = null)
        {
            _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
            _chapterListViewModel = chapterListViewModel ?? throw new ArgumentNullException(nameof(chapterListViewModel));
            _currentChapterIndex = currentChapterIndex;
            _aniListService = aniListService;
            _userSettings = userSettings;
            _initialProgress = initialProgress;

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
            InitializePreferenceCommands();

            Debug.WriteLine($"ReaderViewModel initialized with {_allImages.Count} images");
            Debug.WriteLine($"Current chapter index {_currentChapterIndex}");

            _ = LoadMoreImagesAsync(); 
            _ = _chapterListViewModel?.PrefetchChapterAsync(_currentChapterIndex + 1);
            _ = UpdateAniListProgressAsync();
            RestoreReadingProgress();
        }


        public async Task LoadMoreImagesAsync(CancellationToken cancellationToken = default)
        {
            if (_allImages.Count - _loadedCount <= 0)
            {
                return;
            }

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_imageLoadCts.Token, cancellationToken);
            var token = linkedCts.Token;
            bool acquired = false;
            try
            {
                await _imageLoadSemaphore.WaitAsync(token).ConfigureAwait(false);
                acquired = true;

                int remaining = _allImages.Count - _loadedCount;
                if (remaining <= 0)
                {
                    return;
                }

                int countToLoad = Math.Min(Constants.DefaultLoadedBatchSize, remaining);
                var batch = new List<ChapterImage>(countToLoad);
                for (int i = 0; i < countToLoad; i++)
                {
                    token.ThrowIfCancellationRequested();
                    batch.Add(_allImages[_loadedCount + i]);
                }

                var dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
                await dispatcher.InvokeAsync(() =>
                {
                    foreach (var image in batch)
                    {
                        ImageUrls.Add(image);
                    }

                    _loadedCount += batch.Count;
                    OnPropertyChanged(nameof(RemainingImages));
                    OnPropertyChanged(nameof(LoadedImages));
                    OnPropertyChanged(nameof(TotalImages));
                    OnPropertyChanged(nameof(LoadingProgress));
                });

                Debug.WriteLine($"Loaded {_loadedCount} / {_allImages.Count} images");
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Image loading cancelled.");
            }
            finally
            {
                if (acquired)
                {
                    _imageLoadSemaphore.Release();
                }
            }
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

            var acquired = false;
            try
            {
                await _chapterNavigationSemaphore.WaitAsync();
                acquired = true;

                if (_isChapterNavigationInProgress)
                {
                    Debug.WriteLine("A chapter navigation is already in progress.");
                    return false;
                }

                _isChapterNavigationInProgress = true;

                // Update AniList progress only when moving to the next chapter (i.e., after completing the current)
                if (newIndex == _currentChapterIndex + 1)
                {
                    await UpdateAniListProgressAsync().ConfigureAwait(false);
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
                if (!_isRestoringProgress)
                {
                    PersistReadingProgress(force: true);
                    ChapterChanged?.Invoke(this, EventArgs.Empty);
                }
                return true;
            }
            finally
            {
                _isChapterNavigationInProgress = false;
                if (acquired)
                {
                    _chapterNavigationSemaphore.Release();
                }
            }
        }

        private void ResetImages(IEnumerable<ChapterImage> images)
        {
            _imageLoadCts.Cancel();
            _imageLoadCts.Dispose();
            _imageLoadCts = new CancellationTokenSource();
            _pendingRestoreProgress = null;
            _pendingRestoreOffset = null;

            var dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

            dispatcher.Invoke(() =>
            {
                _allImages.Clear();
                ImageUrls.Clear();
                _loadedCount = 0;
                SetProperty(ref _scrollProgress, 0.0, nameof(ScrollProgress));
                _lastKnownScrollOffset = 0;
                _lastKnownExtentHeight = 0;
                _lastKnownViewportHeight = 0;
                foreach (var img in images)
                {
                    _allImages.Add(img);
                }

                OnPropertyChanged(nameof(TotalImages));
                OnPropertyChanged(nameof(LoadedImages));
                OnPropertyChanged(nameof(RemainingImages));
                OnPropertyChanged(nameof(LoadingProgress));
            });

            _ = LoadMoreImagesAsync();
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
                WeakEventManager<INavigationService, EventArgs>.AddHandler(_navigationService, nameof(INavigationService.CanGoBackChanged), OnNavigationCanGoBackChanged!);
            }

            NextChapterCommand = new AsyncRelayCommand(_ => GoToNextChapterAsync(), _ => CanGoToNextChapter());

            OnPropertyChanged(nameof(GoBackCommand));
            OnPropertyChanged(nameof(GoHomeCommand));
            OnPropertyChanged(nameof(NextChapterCommand));
        }

        private void InitializePreferenceCommands()
        {
            IncreaseZoomCommand = new RelayCommand(_ => WidthFactor = Math.Min(1.0, WidthFactor + 0.05));
            DecreaseZoomCommand = new RelayCommand(_ => WidthFactor = Math.Max(0.3, WidthFactor - 0.05));
            ResetZoomCommand = new RelayCommand(_ =>
            {
                WidthFactor = Constants.DefaultWidthFactor;
                MaxPageWidth = Constants.DefaultMaxPageWidth;
            });

            SetThemeCommand = new RelayCommand(param =>
            {
                if (param is ReaderTheme theme)
                {
                    Theme = theme;
                }
                else if (param is string str && Enum.TryParse(str, out ReaderTheme parsed))
                {
                    Theme = parsed;
                }
            });

            OnPropertyChanged(nameof(IncreaseZoomCommand));
            OnPropertyChanged(nameof(DecreaseZoomCommand));
            OnPropertyChanged(nameof(ResetZoomCommand));
            OnPropertyChanged(nameof(SetThemeCommand));
        }

        private void OnNavigationCanGoBackChanged(object? sender, EventArgs e)
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
            }
        }

        private void ChapterListViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChapterListViewModel.Chapters) && _chapterListViewModel != null)
            {
                Chapters = _chapterListViewModel.Chapters;
                SelectInitialChapter();
                RestoreReadingProgress();
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
    
        // Add a SemaphoreSlim for chapter navigation
        private readonly SemaphoreSlim _chapterNavigationSemaphore = new(1, 1);

        private void PersistReadingProgress(bool force = false)
        {
            if (_isRestoringProgress || _userSettings == null || string.IsNullOrWhiteSpace(MangaTitle))
            {
                return;
            }

            var normalizedProgress = Math.Clamp(_scrollProgress, 0.0, 1.0);
            var currentOffset = _lastKnownScrollOffset;
            var now = DateTime.UtcNow;
            var difference = double.IsNaN(_lastPersistedScrollProgress)
                ? double.MaxValue
                : Math.Abs(normalizedProgress - _lastPersistedScrollProgress);
            var offsetDifference = double.IsNaN(_lastPersistedScrollOffset)
                ? double.MaxValue
                : Math.Abs(currentOffset - _lastPersistedScrollOffset);

            if (!force && difference < 0.02 && offsetDifference < 16 && (now - _lastProgressSaveUtc) < TimeSpan.FromSeconds(2))
            {
                return;
            }

            Chapter? currentChapter = null;
            if (_chapterListViewModel != null &&
                _currentChapterIndex >= 0 &&
                _currentChapterIndex < _chapterListViewModel.Chapters.Count)
            {
                currentChapter = _chapterListViewModel.Chapters[_currentChapterIndex];
            }
            else if (SelectedChapter != null)
            {
                currentChapter = SelectedChapter;
            }

            var title = currentChapter?.Title ?? ChapterTitle ?? string.Empty;
            var mangaUrl = _chapterListViewModel?.Manga?.Url;
            var sourceKey = _chapterListViewModel?.SourceKey;
            var coverImageUrl = _chapterListViewModel?.Manga?.CoverImageUrl;

            var progress = new MangaReadingProgress(
                _currentChapterIndex,
                title,
                normalizedProgress,
                DateTimeOffset.UtcNow,
                string.IsNullOrWhiteSpace(mangaUrl) ? null : mangaUrl,
                string.IsNullOrWhiteSpace(sourceKey) ? null : sourceKey,
                currentOffset);
            _userSettings.SetReadingProgress(MangaTitle, progress);
            _lastPersistedScrollProgress = normalizedProgress;
            _lastPersistedScrollOffset = currentOffset;
            _lastProgressSaveUtc = now;
        }

        private void RestoreReadingProgress()
        {
            if (_hasRestoredInitialProgress || string.IsNullOrWhiteSpace(MangaTitle))
            {
                return;
            }

            if (_initialProgress != null)
            {
                _hasRestoredInitialProgress = true;
                var initialProg = _initialProgress;
                _initialProgress = null;
                _ = RestoreReadingProgressAsync(initialProg);
                return;
            }

            if (_userSettings != null && _userSettings.TryGetReadingProgress(MangaTitle, out var progress) && progress != null)
            {
                _hasRestoredInitialProgress = true;
                _ = RestoreReadingProgressAsync(progress);
            }
            else if (_chapterListViewModel != null)
            {
                _hasRestoredInitialProgress = true;
                PersistReadingProgress(force: true);
            }
        }


        private async Task RestoreReadingProgressAsync(MangaReadingProgress progress)
        {
            if (progress == null)
            {
                return;
            }

            try
            {
                _isRestoringProgress = true;
                if (_chapterListViewModel != null && _chapterListViewModel.Chapters.Count > 0)
                {
                    var targetIndex = Math.Clamp(progress.ChapterIndex, 0, _chapterListViewModel.Chapters.Count - 1);
                    if (targetIndex != _currentChapterIndex)
                    {
                        await TryMoveToChapterAsync(targetIndex).ConfigureAwait(false);
                    }
                }
                var clamped = Math.Clamp(progress.ScrollProgress, 0.0, 1.0);
                _pendingRestoreProgress = clamped > 0 ? clamped : null;
                _pendingRestoreOffset = progress.ScrollOffset.HasValue && progress.ScrollOffset.Value > 0
                    ? Math.Max(0, progress.ScrollOffset.Value)
                    : null;
                SetProperty(ref _scrollProgress, clamped, nameof(ScrollProgress));
                _lastPersistedScrollProgress = clamped;
                if (_pendingRestoreOffset.HasValue)
                {
                    _lastKnownScrollOffset = _pendingRestoreOffset.Value;
                }

                if (_pendingRestoreProgress.HasValue)
                {
                    await EnsureImagesLoadedForProgressAsync(_pendingRestoreProgress.Value).ConfigureAwait(false);
                    RequestScrollRestore(_pendingRestoreProgress, _pendingRestoreOffset);
                }
                else
                {
                    if (_pendingRestoreOffset.HasValue)
                    {
                        RequestScrollRestore(null, _pendingRestoreOffset);
                    }
                    else
                    {
                        _isRestoringProgress = false;
                    }
                }

                if (!_pendingRestoreProgress.HasValue && !_pendingRestoreOffset.HasValue)
                {
                    _isRestoringProgress = false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to restore reading progress: {ex.Message}");
                _pendingRestoreProgress = null;
                _pendingRestoreOffset = null;
            }
            finally
            {
                if (!_pendingRestoreProgress.HasValue && !_pendingRestoreOffset.HasValue)
                {
                    _isRestoringProgress = false;
                }
            }
        }

        private void RequestScrollRestore(double? progress, double? offset)
        {
            if (progress.HasValue || offset.HasValue)
            {
                ScrollRestoreRequested?.Invoke(this, new ScrollRestoreRequest(progress, offset));
            }
        }

        private async Task EnsureImagesLoadedForProgressAsync(double progress)
        {
            if (progress <= 0 || _allImages.Count == 0)
            {
                return;
            }

            var targetIndex = (int)Math.Clamp(Math.Ceiling(progress * _allImages.Count) - 1, 0, _allImages.Count - 1);
            while (_loadedCount <= targetIndex && _loadedCount < _allImages.Count)
            {
                await LoadMoreImagesAsync().ConfigureAwait(false);
            }
        }

        internal void NotifyScrollRestoreCompleted()
        {
            if (!_isRestoringProgress)
            {
                return;
            }

            _isRestoringProgress = false;
            _pendingRestoreProgress = null;
            _pendingRestoreOffset = null;
            _lastProgressSaveUtc = DateTime.MinValue;
        }
    }

    public sealed class ScrollRestoreRequest : EventArgs
    {
        public ScrollRestoreRequest(double? normalizedProgress, double? scrollOffset)
        {
            NormalizedProgress = normalizedProgress;
            ScrollOffset = scrollOffset;
        }

        public double? NormalizedProgress { get; }

        public double? ScrollOffset { get; }
    }
}
