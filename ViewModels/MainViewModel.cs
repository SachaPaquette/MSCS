using System;
using System.Diagnostics;
using MSCS.Interfaces;
using MSCS.Models;
using MSCS.Sources;

namespace MSCS.ViewModels
{
    public class MainViewModel : BaseViewModel, IDisposable
    {
        private BaseViewModel _currentViewModel;
        private readonly INavigationService _navigationService;
        private ChapterListViewModel? _activeChapterViewModel;
        private bool _disposed;

        public MangaListViewModel MangaListVM { get; }

        public BaseViewModel CurrentViewModel
        {
            get => _currentViewModel;
            set => SetProperty(ref _currentViewModel, value);
        }

        public MainViewModel(INavigationService navigationService)
        {
            _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));

            MangaListVM = new MangaListViewModel("mangaread", _navigationService);
            _navigationService.RegisterSingleton(MangaListVM);

            CurrentViewModel = MangaListVM;
            _navigationService.SetRootViewModel(MangaListVM);
            MangaListVM.MangaSelected += OnMangaSelected;
        }

        private void OnMangaSelected(object? sender, Manga? manga)
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

            if (ReferenceEquals(viewModel, MangaListVM))
            {
                MangaListVM.SelectedManga = null;
                DisposeActiveChapterViewModel();
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
            MangaListVM.MangaSelected -= OnMangaSelected;
            DisposeActiveChapterViewModel();
            MangaListVM.Dispose();
        }
    }
}