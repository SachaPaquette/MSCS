using MSCS.Interfaces;
using MSCS.Models;
using MSCS.Services;
using MSCS.Services.Kitsu;
using MSCS.Services.MyAnimeList;
using MSCS.Sources;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;

namespace MSCS.ViewModels
{
    public class MainViewModel : BaseViewModel, IDisposable
    {
        private readonly INavigationService _navigationService;
        private readonly UserSettings _userSettings;
        private readonly LocalLibraryService _localLibraryService;
        private readonly AniListService _aniListService;
        private readonly MediaTrackingServiceRegistry _mediaTrackingRegistry;
        private readonly ReadingListService _readingListService;
        private readonly LocalSource _LocalSource;
        private readonly ThemeService _themeService;
        private readonly Dictionary<BaseViewModel, MainMenuTab> _tabLookup = new();
        private BaseViewModel _currentViewModel;
        private ChapterListViewModel? _activeChapterViewModel;
        private MainMenuTab? _selectedTab;
        private bool _disposed;
        private bool _suppressTabActivation;


        public MainViewModel(INavigationService navigationService)
        {
            _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));

            _userSettings = new UserSettings();
            _themeService = new ThemeService();
            _themeService.ApplyTheme(_userSettings.AppTheme);
            _localLibraryService = new LocalLibraryService(_userSettings);
            _mediaTrackingRegistry = new MediaTrackingServiceRegistry();
            _aniListService = new AniListService(_userSettings);
            _mediaTrackingRegistry.Register(_aniListService);
            _mediaTrackingRegistry.Register(new MyAnimeListService(_userSettings));
            _mediaTrackingRegistry.Register(new KitsuService(_userSettings));
            _readingListService = new ReadingListService(_userSettings);
            _LocalSource = new LocalSource(_localLibraryService);
            MangaListVM = new MangaListViewModel(SourceKeyConstants.DefaultExternal, _navigationService);
            MangaListVM.MangaSelected += OnExternalMangaSelected;
            _navigationService.RegisterSingleton(MangaListVM);

            RecommendationsVM = new AniListRecommendationsViewModel(_aniListService);
            AniListCollectionVM = new AniListCollectionViewModel(_aniListService);

            LocalLibraryVM = new LocalLibraryViewModel(_localLibraryService);
            LocalLibraryVM.MangaSelected += OnLocalMangaSelected;

            ContinueReadingVM = new ContinueReadingViewModel(_userSettings, _readingListService);
            ContinueReadingVM.ContinueReadingRequested += OnContinueReadingRequested;

            SettingsVM = new SettingsViewModel(_localLibraryService, _userSettings, _themeService, _mediaTrackingRegistry);

            Tabs = new ObservableCollection<MainMenuTab>
            {
                new("external", "External Sources", "\uE774", MangaListVM),
                new("local", "Local Library", "\uE8D2", LocalLibraryVM),
                new("anilist-library", "AniList Library", "\uE12B", AniListCollectionVM),
                new("recommendations", "AniList Recommendations", "\uE734", RecommendationsVM),
                new("continue", "Continue Reading", "\uE823", ContinueReadingVM),
                new("settings", "Settings", "\uE713", SettingsVM)
            };

            foreach (var tab in Tabs)
            {
                _tabLookup[tab.ViewModel] = tab;
            }

            _suppressTabActivation = true;
            SelectedTab = Tabs[0];
            _suppressTabActivation = false;

            CurrentViewModel = Tabs[0].ViewModel;
            _navigationService.SetRootViewModel(CurrentViewModel);
        }

        public MangaListViewModel MangaListVM { get; }
        public LocalLibraryViewModel LocalLibraryVM { get; }
        public AniListCollectionViewModel AniListCollectionVM { get; }
        public AniListRecommendationsViewModel RecommendationsVM { get; }
        public SettingsViewModel SettingsVM { get; }
        public ContinueReadingViewModel ContinueReadingVM { get; }
        public ObservableCollection<MainMenuTab> Tabs { get; }
        public MediaTrackingServiceRegistry MediaTrackingRegistry => _mediaTrackingRegistry;

        public BaseViewModel CurrentViewModel
        {
            get => _currentViewModel;
            private set => SetProperty(ref _currentViewModel, value);
        }

        public MainMenuTab? SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (SetProperty(ref _selectedTab, value) && !_suppressTabActivation && value != null)
                {
                    ActivateTab(value);
                }
            }
        }

        private void ActivateTab(MainMenuTab tab)
        {
            if (_disposed)
            {
                return;
            }

            if (ReferenceEquals(CurrentViewModel, tab.ViewModel))
            {
                if (tab.ViewModel is LocalLibraryViewModel alreadyActiveLocal)
                {
                    alreadyActiveLocal.EnsureLibraryLoaded();
                }

                return;
            }

            DisposeActiveChapterViewModel();

            if (tab.ViewModel is MangaListViewModel mangaListViewModel)
            {
                mangaListViewModel.SelectedManga = null;
            }
            else if (tab.ViewModel is LocalLibraryViewModel localLibraryViewModel)
            {
                localLibraryViewModel.SelectedManga = null;
                localLibraryViewModel.EnsureLibraryLoaded();
            }

            CurrentViewModel = tab.ViewModel;
            _navigationService.SetRootViewModel(tab.ViewModel);
        }

        private void OnExternalMangaSelected(object? sender, Manga? manga)
        {
            if (_disposed || manga == null)
            {
                return;
            }

            var selectedSourceKey = string.IsNullOrWhiteSpace(MangaListVM.SelectedSourceKey)
                ? SourceKeyConstants.DefaultExternal
                : MangaListVM.SelectedSourceKey;

            var source = ResolveSourceFromKey(selectedSourceKey);

            if (source == null)
            {
                Debug.WriteLine($"Unable to resolve manga source '{selectedSourceKey}' for selection '{manga?.Title}'.");
                return;
            }

            NavigateToChapterList(source, manga, selectedSourceKey);
        }

        private void OnLocalMangaSelected(object? sender, Manga? manga)
        {
            if (_disposed || manga == null)
            {
                return;
            }

            NavigateToChapterList(_LocalSource, manga, SourceKeyConstants.LocalLibrary);
        }

        private void NavigateToChapterList(IMangaSource source, Manga manga, string sourceKey, bool autoOpenChapter = false, bool skipChapterListNavigation = false, MangaReadingProgress? initialProgress = null)
        {
            DisposeActiveChapterViewModel();

            var sanitizedKey = string.IsNullOrWhiteSpace(sourceKey) ? string.Empty : sourceKey;
            var chapterViewModel = new ChapterListViewModel(
                source,
                _navigationService,
                manga,
                _aniListService,
                _userSettings,
                sanitizedKey,
                autoOpenChapter,
                initialProgress);
            _activeChapterViewModel = chapterViewModel;
            if (!skipChapterListNavigation)
            {
                _navigationService.NavigateToViewModel(chapterViewModel);
            }
        }


        private IMangaSource? ResolveSourceFromKey(string? sourceKey)
        {
            if (string.IsNullOrWhiteSpace(sourceKey))
            {
                return SourceRegistry.Resolve(SourceKeyConstants.DefaultExternal);
            }

            if (string.Equals(sourceKey, SourceKeyConstants.LocalLibrary, StringComparison.OrdinalIgnoreCase))
            {
                return _LocalSource;
            }

            return SourceRegistry.Resolve(sourceKey);
        }

        private void OnContinueReadingRequested(object? sender, ContinueReadingRequestedEventArgs e)
        {
            if (_disposed || e == null)
            {
                return;
            }

            var progress = e.Progress;
            if (progress == null || string.IsNullOrWhiteSpace(progress.MangaUrl))
            {
                return;
            }

            var sourceKey = string.IsNullOrWhiteSpace(progress.SourceKey)
                ? SourceKeyConstants.DefaultExternal
                : progress.SourceKey!;

            var source = ResolveSourceFromKey(sourceKey);
            if (source == null)
            {
                Debug.WriteLine($"Unable to resolve source '{sourceKey}' for continue reading entry '{e.MangaTitle}'.");
                return;
            }

            var manga = new Manga
            {
                Title = e.MangaTitle ?? string.Empty,
                Url = progress.MangaUrl!,
                Description = string.Empty
            };

            NavigateToChapterList(
    source,
    manga,
    sourceKey,
    autoOpenChapter: true,
    skipChapterListNavigation: true,
    initialProgress: progress);
        }


        public void NavigateTo<TViewModel>() where TViewModel : BaseViewModel
        {
            if (_disposed)
            {
                return;
            }

            _navigationService.NavigateTo<TViewModel>();
        }

        public void NavigateTo<TViewModel>(object parameter) where TViewModel : BaseViewModel
        {
            if (_disposed)
            {
                return;
            }

            _navigationService.NavigateTo<TViewModel>(parameter);
        }

        public void NavigateToViewModel(BaseViewModel viewModel)
        {
            if (_disposed)
            {
                return;
            }

            CurrentViewModel = viewModel;

            if (_tabLookup.TryGetValue(viewModel, out var tab))
            {
                try
                {
                    _suppressTabActivation = true;
                    SelectedTab = tab;
                }
                finally
                {
                    _suppressTabActivation = false;
                }

                if (viewModel is MangaListViewModel mangaListViewModel)
                {
                    mangaListViewModel.SelectedManga = null;
                    DisposeActiveChapterViewModel();
                }
                else if (viewModel is LocalLibraryViewModel localLibraryViewModel)
                {
                    localLibraryViewModel.SelectedManga = null;
                    localLibraryViewModel.EnsureLibraryLoaded();
                    DisposeActiveChapterViewModel();
                }

                _navigationService.SetRootViewModel(viewModel);
            }
            else if (viewModel is ChapterListViewModel chapterViewModel)
            {
                _activeChapterViewModel = chapterViewModel;
            }
        }

        private void DisposeActiveChapterViewModel()
        {
            var existing = _activeChapterViewModel;
            if (existing != null)
            {
                _activeChapterViewModel = null;
                existing.Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            MangaListVM.MangaSelected -= OnExternalMangaSelected;
            LocalLibraryVM.MangaSelected -= OnLocalMangaSelected;
            ContinueReadingVM.ContinueReadingRequested -= OnContinueReadingRequested;
            DisposeActiveChapterViewModel();
            MangaListVM.Dispose();
            LocalLibraryVM.Dispose();
            RecommendationsVM.Dispose();
            AniListCollectionVM.Dispose();
            ContinueReadingVM.Dispose();
            SettingsVM.Dispose();
            _localLibraryService.Dispose();
        }
    }
}