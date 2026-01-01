using MSCS.Interfaces;
using MSCS.Models;
using MSCS.Services;
using MSCS.Sources;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;

namespace MSCS.ViewModels
{
    public class MainViewModel : BaseViewModel, IDisposable
    {
        private readonly INavigationService _navigationService;
        private readonly UserSettings _userSettings;
        private readonly MediaTrackingServiceRegistry _mediaTrackingRegistry;
        private readonly ThemeService _themeService;
        private readonly LocalSource _localSource;
        private readonly Dictionary<BaseViewModel, MainMenuTab> _tabLookup = new();
        private BaseViewModel _currentViewModel;
        private ChapterListViewModel? _activeChapterViewModel;
        private MainMenuTab? _selectedTab;
        private bool _disposed;
        private bool _suppressTabActivation;
        private bool _isReaderFullscreen;

        public MainViewModel(
            INavigationService navigationService,
            UserSettings userSettings,
            ThemeService themeService,
            MediaTrackingServiceRegistry mediaTrackingRegistry,
            LocalSource localSource,
            HomeViewModel homeViewModel,
            MangaListViewModel mangaListViewModel,
            LocalLibraryViewModel localLibraryViewModel,
            BookmarkLibraryViewModel bookmarkLibraryViewModel,
            TrackingLibrariesViewModel trackingLibrariesViewModel,
            AniListRecommendationsViewModel recommendationsViewModel,
            ContinueReadingViewModel continueReadingViewModel,
            SettingsViewModel settingsViewModel)
        {
            _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
            _userSettings = userSettings ?? throw new ArgumentNullException(nameof(userSettings));
            _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
            _mediaTrackingRegistry = mediaTrackingRegistry ?? throw new ArgumentNullException(nameof(mediaTrackingRegistry));
            _localSource = localSource ?? throw new ArgumentNullException(nameof(localSource));
            HomeVM = homeViewModel ?? throw new ArgumentNullException(nameof(homeViewModel));

            _themeService.ApplyTheme(_userSettings.AppTheme);

            MangaListVM = mangaListViewModel ?? throw new ArgumentNullException(nameof(mangaListViewModel));
            MangaListVM.MangaSelected += OnExternalMangaSelected;
            _navigationService.RegisterSingleton(MangaListVM);

            LocalLibraryVM = localLibraryViewModel ?? throw new ArgumentNullException(nameof(localLibraryViewModel));
            LocalLibraryVM.MangaSelected += OnLocalMangaSelected;

            BookmarksVM = bookmarkLibraryViewModel ?? throw new ArgumentNullException(nameof(bookmarkLibraryViewModel));
            BookmarksVM.BookmarkSelected += OnBookmarkSelected;

            TrackingLibrariesVM = trackingLibrariesViewModel ?? throw new ArgumentNullException(nameof(trackingLibrariesViewModel));
            RecommendationsVM = recommendationsViewModel ?? throw new ArgumentNullException(nameof(recommendationsViewModel));

            ContinueReadingVM = continueReadingViewModel ?? throw new ArgumentNullException(nameof(continueReadingViewModel));
            ContinueReadingVM.ContinueReadingRequested += OnContinueReadingRequested;
            HomeVM.LocalChapterRequested += OnLocalChapterRequested;
            SettingsVM = settingsViewModel ?? throw new ArgumentNullException(nameof(settingsViewModel));

            Tabs = new ObservableCollection<MainMenuTab>
            {
                new("home", "Home", "\uE80F", HomeVM),
                new("external", "External Sources", "\uE774", MangaListVM),
                new("tracking-libraries", "Tracking Libraries", "\uE12B", TrackingLibrariesVM),
                new("recommendations", "AniList Recommendations", "\uE734", RecommendationsVM),
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
        public BookmarkLibraryViewModel BookmarksVM { get; }
        public TrackingLibrariesViewModel TrackingLibrariesVM { get; }
        public AniListRecommendationsViewModel RecommendationsVM { get; }
        public SettingsViewModel SettingsVM { get; }
        public ContinueReadingViewModel ContinueReadingVM { get; }
        public HomeViewModel HomeVM { get; }
        public ObservableCollection<MainMenuTab> Tabs { get; }
        public MediaTrackingServiceRegistry MediaTrackingRegistry => _mediaTrackingRegistry;

        public BaseViewModel CurrentViewModel
        {
            get => _currentViewModel;
            private set => SetProperty(ref _currentViewModel, value);
        }

        public bool IsReaderFullscreen
        {
            get => _isReaderFullscreen;
            set => SetProperty(ref _isReaderFullscreen, value);
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
                return;
            }

            DisposeActiveChapterViewModel();

            if (tab.ViewModel is MangaListViewModel mangaListViewModel)
            {
                mangaListViewModel.SelectedResult = null;
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

            NavigateToChapterList(_localSource, manga, SourceKeyConstants.LocalLibrary);
        }

        private void OnBookmarkSelected(object? sender, BookmarkSelectedEventArgs e)
        {
            if (_disposed || e == null)
            {
                return;
            }

            var entry = e.Entry;
            if (entry == null)
            {
                return;
            }

            var title = entry.Title ?? string.Empty;
            var sourceKey = string.IsNullOrWhiteSpace(entry.SourceKey)
                ? SourceKeyConstants.DefaultExternal
                : entry.SourceKey!;

            if (string.Equals(sourceKey, SourceKeyConstants.LocalLibrary, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(entry.MangaUrl))
                {
                    return;
                }

                var localManga = new Manga
                {
                    Title = title,
                    Url = entry.MangaUrl!,
                    CoverImageUrl = entry.CoverImageUrl ?? string.Empty,
                    Description = "Bookmarked local series"
                };

                NavigateToChapterList(_localSource, localManga, SourceKeyConstants.LocalLibrary);
                return;
            }

            if (string.IsNullOrWhiteSpace(entry.MangaUrl))
            {
                Debug.WriteLine($"Bookmark '{title}' is missing a link and cannot be opened.");
                return;
            }

            var source = ResolveSourceFromKey(sourceKey);
            if (source == null)
            {
                Debug.WriteLine($"Unable to resolve source '{sourceKey}' for bookmark '{title}'.");
                return;
            }

            var manga = new Manga
            {
                Title = title,
                Url = entry.MangaUrl!,
                CoverImageUrl = entry.CoverImageUrl ?? string.Empty,
                Description = string.Empty
            };

            NavigateToChapterList(source, manga, sourceKey);
        }

        private void NavigateToChapterList(IMangaSource source, Manga manga, string sourceKey, bool autoOpenChapter = false, bool skipChapterListNavigation = false, MangaReadingProgress? initialProgress = null)
        {
            DisposeActiveChapterViewModel();

            var sanitizedKey = string.IsNullOrWhiteSpace(sourceKey) ? string.Empty : sourceKey;
            var chapterViewModel = new ChapterListViewModel(
                source,
                _navigationService,
                manga,
                _mediaTrackingRegistry,
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
                return _localSource;
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


        private async void OnLocalChapterRequested(object? sender, LocalChapterRequestedEventArgs e)
        {
            if (_disposed || e == null)
            {
                return;
            }

            var chapterPath = e.ChapterPath;
            if (string.IsNullOrWhiteSpace(chapterPath))
            {
                return;
            }

            var mangaDirectory = GetMangaDirectory(chapterPath);
            if (string.IsNullOrWhiteSpace(mangaDirectory) || !Directory.Exists(mangaDirectory))
            {
                return;
            }

            var normalizedChapterPath = NormalizePathSafe(chapterPath);
            var chapterTitle = Path.GetFileNameWithoutExtension(normalizedChapterPath) ?? Path.GetFileName(normalizedChapterPath) ?? normalizedChapterPath;
            var mangaTitle = new DirectoryInfo(mangaDirectory).Name;

            var chapters = await _localSource.GetChaptersAsync(mangaDirectory).ConfigureAwait(false);
            var chapterIndex = FindChapterIndex(chapters, normalizedChapterPath);

            var progress = new MangaReadingProgress(
                chapterIndex >= 0 ? chapterIndex : 0,
                chapterTitle,
                0,
                DateTimeOffset.UtcNow,
                mangaDirectory,
                SourceKeyConstants.LocalLibrary);

            var manga = new Manga
            {
                Title = mangaTitle,
                Url = mangaDirectory,
                Description = string.Empty
            };

            NavigateToChapterList(
                _localSource,
                manga,
                SourceKeyConstants.LocalLibrary,
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
                    mangaListViewModel.SelectedResult = null;
                    DisposeActiveChapterViewModel();
                }

                _navigationService.SetRootViewModel(viewModel);
            }
            else if (viewModel is ChapterListViewModel chapterViewModel)
            {
                _activeChapterViewModel = chapterViewModel;
            }
        }


        private static int FindChapterIndex(IReadOnlyList<Chapter> chapters, string chapterPath)
        {
            if (chapters == null || chapters.Count == 0 || string.IsNullOrWhiteSpace(chapterPath))
            {
                return -1;
            }

            var normalizedTarget = NormalizePathSafe(chapterPath);

            for (var i = 0; i < chapters.Count; i++)
            {
                var candidate = chapters[i];
                if (string.IsNullOrWhiteSpace(candidate?.Url))
                {
                    continue;
                }

                var normalizedCandidate = NormalizePathSafe(candidate.Url);
                if (PathsEqual(normalizedCandidate, normalizedTarget))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string? GetMangaDirectory(string chapterPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(chapterPath))
                {
                    return null;
                }

                if (Directory.Exists(chapterPath))
                {
                    return Directory.GetParent(chapterPath)?.FullName;
                }

                if (File.Exists(chapterPath))
                {
                    return Path.GetDirectoryName(chapterPath);
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool PathsEqual(string? first, string? second)
        {
            if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
            {
                return false;
            }

            return string.Equals(NormalizePathSafe(first), NormalizePathSafe(second), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePathSafe(string path)
        {
            try
            {
                return Path.GetFullPath(path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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
            BookmarksVM.BookmarkSelected -= OnBookmarkSelected;
            ContinueReadingVM.ContinueReadingRequested -= OnContinueReadingRequested;
            HomeVM.LocalChapterRequested -= OnLocalChapterRequested;
            DisposeActiveChapterViewModel();
            MangaListVM.Dispose();
            LocalLibraryVM.Dispose();
            BookmarksVM.Dispose();
            RecommendationsVM.Dispose();
            TrackingLibrariesVM.Dispose();
            ContinueReadingVM.Dispose();
            SettingsVM.Dispose();
        }
    }
}