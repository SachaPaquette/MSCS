using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using MSCS.Interfaces;
using MSCS.Models;
using MSCS.Services;
using MSCS.Sources;

namespace MSCS.ViewModels
{
    public class MainViewModel : BaseViewModel, IDisposable
    {
        private readonly INavigationService _navigationService;
        private readonly UserSettings _userSettings;
        private readonly LocalLibraryService _localLibraryService;
        private readonly LocalSource _LocalSource;
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
            _localLibraryService = new LocalLibraryService(_userSettings);
            _LocalSource = new LocalSource(_localLibraryService);

            MangaListVM = new MangaListViewModel("mangaread", _navigationService);
            MangaListVM.MangaSelected += OnExternalMangaSelected;
            _navigationService.RegisterSingleton(MangaListVM);

            LocalLibraryVM = new LocalLibraryViewModel(_localLibraryService);
            LocalLibraryVM.MangaSelected += OnLocalMangaSelected;

            SettingsVM = new SettingsViewModel(_localLibraryService);

            Tabs = new ObservableCollection<MainMenuTab>
            {
                new("external", "External Sources", "\uE774", MangaListVM),
                new("local", "Local Library", "\uE8D2", LocalLibraryVM),
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
        public SettingsViewModel SettingsVM { get; }

        public ObservableCollection<MainMenuTab> Tabs { get; }

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

            var selectedSourceKey = MangaListVM.SelectedSourceKey;
            var source = !string.IsNullOrWhiteSpace(selectedSourceKey)
                ? SourceRegistry.Resolve(selectedSourceKey)
                : SourceRegistry.Resolve("mangaread");

            if (source == null)
            {
                Debug.WriteLine($"Unable to resolve manga source '{selectedSourceKey}' for selection '{manga?.Title}'.");
                return;
            }

            NavigateToChapterList(source, manga);
        }

        private void OnLocalMangaSelected(object? sender, Manga? manga)
        {
            if (_disposed || manga == null)
            {
                return;
            }

            NavigateToChapterList(_LocalSource, manga);
        }

        private void NavigateToChapterList(IMangaSource source, Manga manga)
        {
            DisposeActiveChapterViewModel();

            var chapterViewModel = new ChapterListViewModel(source, _navigationService, manga);
            _activeChapterViewModel = chapterViewModel;
            _navigationService.NavigateToViewModel(chapterViewModel);
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
            DisposeActiveChapterViewModel();
            MangaListVM.Dispose();
            LocalLibraryVM.Dispose();
            SettingsVM.Dispose();
            _localLibraryService.Dispose();
        }
    }
}