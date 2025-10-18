using MSCS.Commands;
using MSCS.Enums;
using MSCS.Helpers;
using MSCS.Interfaces;
using MSCS.Models;
using MSCS.Services;
using MSCS.ViewModels;
using MSCS.ViewModels.Reader.Components;
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
        private readonly MediaTrackingServiceRegistry? _trackingRegistry;
        private readonly UserSettings? _userSettings;
        private readonly ObservableCollection<TrackingProvider> _trackingProviders;
        private TrackingProvider? _activeTrackingProvider;
        private static readonly ICommand DisabledCommand = new RelayCommand(_ => { }, _ => false);
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
        private readonly ReaderPreferencesViewModel _preferences;
        public ReaderPreferencesViewModel Preferences => _preferences;
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
                    _preferences.UpdateProfileKey(DetermineProfileKey());
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
        public ObservableCollection<ChapterImage> ImageUrls { get; }
        public ObservableCollection<TrackingProvider> TrackingProviders => _trackingProviders;
        public TrackingProvider? ActiveTrackingProvider
        {
            get => _activeTrackingProvider;
            set
            {
                if (SetProperty(ref _activeTrackingProvider, value))
                {
                    value?.OnActivated();
                    NotifyTrackingProperties();
                }
            }
        }
        public ICommand GoBackCommand { get; private set; } = new RelayCommand(_ => { }, _ => false);
        public ICommand GoHomeCommand { get; private set; } = new RelayCommand(_ => { }, _ => false);
        public ICommand PreviousChapterCommand { get; private set; } = new AsyncRelayCommand(_ => Task.CompletedTask, _ => false);
        public ICommand NextChapterCommand { get; private set; } = new AsyncRelayCommand(_ => Task.CompletedTask, _ => false);
        public ICommand AniListTrackCommand { get; private set; } = new RelayCommand(_ => { });
        public ICommand AniListOpenInBrowserCommand { get; private set; } = new RelayCommand(_ => { }, _ => false);
        public ICommand AniListRemoveTrackingCommand { get; private set; } = new AsyncRelayCommand(_ => Task.CompletedTask, _ => false);
        public bool HasTrackingProviders => _trackingProviders.Count > 0;
        public bool HasMultipleTrackingProviders => _trackingProviders.Count > 1;
        public bool IsTrackingAvailable => ActiveTrackingProvider?.IsAvailable == true;
        public string? ActiveTrackerName => ActiveTrackingProvider?.DisplayName;
        public bool IsTracked => ActiveTrackingProvider?.IsTracked ?? false;
        public ICommand TrackCommand => ActiveTrackingProvider?.TrackCommand ?? DisabledCommand;
        public ICommand OpenInBrowserCommand => ActiveTrackingProvider?.OpenInBrowserCommand ?? DisabledCommand;
        public ICommand RemoveTrackingCommand => ActiveTrackingProvider?.RemoveTrackingCommand ?? DisabledCommand;
        public string TrackButtonText => ActiveTrackingProvider?.TrackButtonText ?? "Track";
        public string OpenTrackerButtonText => ActiveTrackerName != null ? $"Open {ActiveTrackerName}" : "Open tracker";
        public string RemoveTrackerButtonText => ActiveTrackerName != null ? $"Remove {ActiveTrackerName}" : "Remove tracking";
        public string? TrackingStatusDisplay => ActiveTrackingProvider?.StatusDisplay;
        public string? TrackingProgressDisplay => ActiveTrackingProvider?.ProgressDisplay;
        public string? TrackingScoreDisplay => ActiveTrackingProvider?.ScoreDisplay;
        public string? TrackingUpdatedDisplay => ActiveTrackingProvider?.UpdatedDisplay;
        public bool CanOpenTracker => ActiveTrackingProvider?.CanOpenInBrowser ?? false;

        public ReaderViewModel()
        {
            Debug.WriteLine("ReaderViewModel initialized with no images");
            _navigationService = null;
            _chapterListViewModel = null;
            _trackingRegistry = null;
            _userSettings = null;
            _allImages = new List<ChapterImage>();
            ImageUrls = new ObservableCollection<ChapterImage>();
            Chapters = new ObservableCollection<Chapter>();
            _trackingProviders = new ObservableCollection<TrackingProvider>();
            InitializeNavigationCommands();
            InitializeTrackingProviders();
            InitializeReaderProfile();
            InitializePreferenceCommands();
        }

        public ReaderViewModel(
            IEnumerable<ChapterImage>? imageUrls,
            string title,
            INavigationService navigationService,
            ChapterListViewModel chapterListViewModel,
            int currentChapterIndex,
            MediaTrackingServiceRegistry? trackingRegistry,
            UserSettings? userSettings = null,
            MangaReadingProgress? initialProgress = null)
        {
            _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
            _chapterListViewModel = chapterListViewModel ?? throw new ArgumentNullException(nameof(chapterListViewModel));
            _currentChapterIndex = currentChapterIndex;
            _trackingRegistry = trackingRegistry;
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

            _trackingProviders = new ObservableCollection<TrackingProvider>();
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
            InitializeTrackingProviders();
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
            _ = UpdateTrackingProgressAsync();
            RestoreReadingProgress();
        }

        private string? DetermineProfileKey()
        {
            if (!string.IsNullOrWhiteSpace(_chapterListViewModel?.Manga?.Title))
            {
                return _chapterListViewModel.Manga!.Title;
            }

            return string.IsNullOrWhiteSpace(MangaTitle) ? null : MangaTitle;
        }
    }
}
