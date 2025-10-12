using MSCS.Commands;
using MSCS.Enums;
using MSCS.Helpers;
using MSCS.Interfaces;
using MSCS.Models;
using MSCS.Services;
using MSCS.ViewModels;
using MSCS.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MSCS.ViewModels
{
    public partial class ReaderViewModel : BaseViewModel
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
        private EventHandler<ScrollRestoreRequest>? _scrollRestoreRequested;
        private ScrollRestoreRequest? _queuedScrollRestoreRequest;
        public event EventHandler<ScrollRestoreRequest>? ScrollRestoreRequested
        {
            add
            {
                _scrollRestoreRequested += value;
                if (value != null && _queuedScrollRestoreRequest != null)
                {
                    value(this, _queuedScrollRestoreRequest);
                }
            }
            remove
            {
                _scrollRestoreRequested -= value;
            }
        }
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
        private double _scrollPageFraction = Constants.DefaultSmoothScrollPageFraction;
        private int _scrollDurationMs = Constants.DefaultSmoothScrollDuration;
        private ReaderScrollPreset _scrollPreset = ReaderScrollPreset.Balanced;
        private bool _isApplyingProfile;
        private bool _suppressPresetPropagation;
        private string? _profileKey;
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
                var normalized = NormalizeWidthFactor(value);
                if (SetProperty(ref _widthFactor, normalized))
                {
                    OnPropertyChanged(nameof(ZoomPercent));
                    if (!_isApplyingProfile)
                    {
                        PersistReaderProfile();
                    }
                }
            }
        }

        private static double NormalizeWidthFactor(double value)
        {
            if (!double.IsFinite(value))
            {
                return Constants.DefaultWidthFactor;
            }

            return Math.Clamp(value, 0.3, 1.0);
        }


        private double _maxPageWidth = Constants.DefaultMaxPageWidth;
        public double MaxPageWidth
        {
            get => _maxPageWidth;
            set
            {
                if (SetProperty(ref _maxPageWidth, Math.Max(400, value)) && !_isApplyingProfile)
                {
                    PersistReaderProfile();
                }
            }
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
                    if (!_isApplyingProfile)
                    {
                        PersistReaderProfile();
                    }
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

        public double ZoomPercent
        {
            get
            {
                var factor = double.IsFinite(_widthFactor) ? _widthFactor : Constants.DefaultWidthFactor;
                return Math.Round(factor * 100);
            }
        }
        public double ScrollPageFraction
        {
            get => _scrollPageFraction;
            set
            {
                var clamped = double.IsNaN(value)
                    ? Constants.DefaultSmoothScrollPageFraction
                    : Math.Clamp(value, 0.2, 2.0);
                if (SetProperty(ref _scrollPageFraction, clamped))
                {
                    UpdateScrollPreset();
                    if (!_isApplyingProfile)
                    {
                        PersistReaderProfile();
                    }
                }
            }
        }

        public int ScrollDurationMs
        {
            get => _scrollDurationMs;
            set
            {
                var clamped = Math.Clamp(value, 0, 2000);
                if (SetProperty(ref _scrollDurationMs, clamped))
                {
                    OnPropertyChanged(nameof(ScrollDuration));
                    UpdateScrollPreset();
                    if (!_isApplyingProfile)
                    {
                        PersistReaderProfile();
                    }
                }
            }
        }

        public TimeSpan ScrollDuration => TimeSpan.FromMilliseconds(Math.Max(0, ScrollDurationMs));

        public ReaderScrollPreset ScrollPreset
        {
            get => _scrollPreset;
            set
            {
                if (SetProperty(ref _scrollPreset, value) && !_suppressPresetPropagation)
                {
                    ApplyScrollPreset(value);
                }
            }
        }
        private double _scrollProgress;
        public double ScrollProgress => _scrollProgress;
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
            private set
            {
                if (SetProperty(ref _mangaTitle, value))
                {
                    _profileKey = DetermineProfileKey();
                }
            }
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
                    NotifyAniListProperties();
                }
            }
        }
        public ObservableCollection<ChapterImage> ImageUrls { get; }
        public ICommand GoBackCommand { get; private set; } = new RelayCommand(_ => { }, _ => false);
        public ICommand GoHomeCommand { get; private set; } = new RelayCommand(_ => { }, _ => false);
        public ICommand PreviousChapterCommand { get; private set; } = new AsyncRelayCommand(_ => Task.CompletedTask, _ => false);
        public ICommand NextChapterCommand { get; private set; } = new AsyncRelayCommand(_ => Task.CompletedTask, _ => false);
        public ICommand AniListTrackCommand { get; private set; } = new RelayCommand(_ => { });
        public ICommand AniListOpenInBrowserCommand { get; private set; } = new RelayCommand(_ => { }, _ => false);
        public ICommand AniListRemoveTrackingCommand { get; private set; } = new AsyncRelayCommand(_ => Task.CompletedTask, _ => false);
        public ICommand IncreaseZoomCommand { get; private set; } = new RelayCommand(_ => { }, _ => false);
        public ICommand DecreaseZoomCommand { get; private set; } = new RelayCommand(_ => { }, _ => false);
        public ICommand ResetZoomCommand { get; private set; } = new RelayCommand(_ => { }, _ => false);
        public ICommand SetZoomPresetCommand { get; private set; } = new RelayCommand(_ => { }, _ => false);
        public ICommand SetThemeCommand { get; private set; } = new RelayCommand(_ => { });
        public ICommand SetScrollPresetCommand { get; private set; } = new RelayCommand(_ => { }, _ => false);

        public bool IsAniListAvailable => _aniListService != null;
        public bool IsAniListTracked => TrackingInfo != null;
        public string AniListButtonText => IsAniListTracked ? "Manage" : "Track";
        public string? AniListStatusDisplay => TrackingInfo?.StatusDisplay;
        public string? AniListProgressDisplay
        {
            get
            {
                if (TrackingInfo?.Progress is null or <= 0)
                {
                    return null;
                }

                return TrackingInfo.TotalChapters.HasValue
                    ? $"Progress {TrackingInfo.Progress}/{TrackingInfo.TotalChapters}"
                    : $"Progress {TrackingInfo.Progress}";
            }
        }

        public string? AniListScoreDisplay => TrackingInfo?.Score is > 0
            ? string.Format(CultureInfo.CurrentCulture, "Score {0:0}", TrackingInfo.Score)
            : null;

        public string? AniListUpdatedDisplay => TrackingInfo?.UpdatedAt.HasValue == true
            ? string.Format(CultureInfo.CurrentCulture, "Updated {0:g}", TrackingInfo.UpdatedAt.Value.ToLocalTime())
            : null;

        public bool CanOpenAniList => TrackingInfo != null && !string.IsNullOrWhiteSpace(TrackingInfo.SiteUrl);

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
            InitializeReaderProfile();
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
            InitializeReaderProfile();
            InitializePreferenceCommands();

            Debug.WriteLine($"ReaderViewModel initialized with {_allImages.Count} images");
            Debug.WriteLine($"Current chapter index {_currentChapterIndex}");

            if (_allImages.Count > 0)
            {
                _ = LoadMoreImagesAsync();
            }
            else
            {
                _ = InitializeChapterImagesAsync(_currentChapterIndex);
            }

            _ = _chapterListViewModel?.PrefetchChapterAsync(_currentChapterIndex + 1);
            _ = UpdateAniListProgressAsync();
            RestoreReadingProgress();
        }

        private void InitializeReaderProfile()
        {
            var key = DetermineProfileKey();
            _profileKey = key;
            var profile = _userSettings?.GetReaderProfile(key) ?? ReaderProfile.CreateDefault();

            _isApplyingProfile = true;
            try
            {
                Theme = profile.Theme;
                WidthFactor = profile.WidthFactor;
                MaxPageWidth = profile.MaxPageWidth;
                ScrollPageFraction = profile.ScrollPageFraction;
                ScrollDurationMs = profile.ScrollDurationMs;
            }
            finally
            {
                _isApplyingProfile = false;
            }

            UpdateScrollPreset();
        }

        private string? DetermineProfileKey()
        {
            if (!string.IsNullOrWhiteSpace(_chapterListViewModel?.Manga?.Title))
            {
                return _chapterListViewModel.Manga!.Title;
            }

            return string.IsNullOrWhiteSpace(MangaTitle) ? null : MangaTitle;
        }

        private void PersistReaderProfile()
        {
            if (_userSettings == null)
            {
                return;
            }

            var key = DetermineProfileKey();
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            _profileKey = key;
            var profile = new ReaderProfile
            {
                Theme = Theme,
                WidthFactor = WidthFactor,
                MaxPageWidth = MaxPageWidth,
                ScrollPageFraction = ScrollPageFraction,
                ScrollDurationMs = ScrollDurationMs
            };

            _userSettings.SetReaderProfile(key, profile);
        }

        private void ApplyScrollPreset(ReaderScrollPreset preset)
        {
            if (preset == ReaderScrollPreset.Custom)
            {
                return;
            }

            (double fraction, int duration) = preset switch
            {
                ReaderScrollPreset.Compact => (0.65, 180),
                ReaderScrollPreset.Immersive => (1.1, 320),
                _ => (Constants.DefaultSmoothScrollPageFraction, Constants.DefaultSmoothScrollDuration)
            };

            _isApplyingProfile = true;
            try
            {
                ScrollPageFraction = fraction;
                ScrollDurationMs = duration;
            }
            finally
            {
                _isApplyingProfile = false;
            }

            PersistReaderProfile();
        }

        private void UpdateScrollPreset()
        {
            var preset = DeterminePreset(_scrollPageFraction, _scrollDurationMs);
            _suppressPresetPropagation = true;
            try
            {
                ScrollPreset = preset;
            }
            finally
            {
                _suppressPresetPropagation = false;
            }
        }

        private static ReaderScrollPreset DeterminePreset(double fraction, int duration)
        {
            if (IsClose(fraction, 0.65) && duration == 180)
            {
                return ReaderScrollPreset.Compact;
            }

            if (IsClose(fraction, Constants.DefaultSmoothScrollPageFraction) && duration == Constants.DefaultSmoothScrollDuration)
            {
                return ReaderScrollPreset.Balanced;
            }

            if (IsClose(fraction, 1.1) && duration == 320)
            {
                return ReaderScrollPreset.Immersive;
            }

            return ReaderScrollPreset.Custom;
        }

        private static bool IsClose(double value, double target, double tolerance = 0.01)
        {
            return Math.Abs(value - target) <= tolerance;
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

            SetZoomPresetCommand = new RelayCommand(param =>
            {
                if (param is double direct)
                {
                    WidthFactor = direct;
                }
                else if (param is string str && double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    WidthFactor = parsed;
                }
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

            SetScrollPresetCommand = new RelayCommand(param =>
            {
                if (param is ReaderScrollPreset preset)
                {
                    ScrollPreset = preset;
                }
                else if (param is string str && Enum.TryParse(str, true, out ReaderScrollPreset parsed))
                {
                    ScrollPreset = parsed;
                }
            });

            OnPropertyChanged(nameof(IncreaseZoomCommand));
            OnPropertyChanged(nameof(DecreaseZoomCommand));
            OnPropertyChanged(nameof(ResetZoomCommand));
            OnPropertyChanged(nameof(SetZoomPresetCommand));
            OnPropertyChanged(nameof(SetThemeCommand));
            OnPropertyChanged(nameof(SetScrollPresetCommand));
        }
    }
}
