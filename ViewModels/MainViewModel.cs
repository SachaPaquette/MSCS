using MSCS.Interfaces;
using MSCS.Scrapers;
using MSCS.Models;
using System.Diagnostics;
using MSCS.Services;

namespace MSCS.ViewModels
{
    public class MainViewModel : BaseViewModel
    {
        private BaseViewModel _currentViewModel;
        private readonly INavigationService _navigationService;
        public MangaListViewModel MangaListVM { get; }
        public BaseViewModel CurrentViewModel
        {
            get => _currentViewModel;
            set => SetProperty(ref _currentViewModel, value);
        }

        public MainViewModel(INavigationService navigationService)
        {
            _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));

            MangaListVM = new MangaListViewModel(new MangaReadScraper(), _navigationService);
            _navigationService.RegisterSingleton(MangaListVM); 

            CurrentViewModel = MangaListVM;
            MangaListVM.MangaSelected += OnMangaSelected;
        }


        private void OnMangaSelected(object s, Manga m)
        {
            var vm = new ChapterListViewModel(new MangaReadScraper(), _navigationService, m);
            _navigationService.NavigateToViewModel(vm); 
        }

        public void Initialize(INavigationService navigationService)
        {
            var mangaListVM = new MangaListViewModel(new MangaReadScraper(), navigationService);
            mangaListVM.MangaSelected += OnMangaSelected;

            CurrentViewModel = mangaListVM;
        }
        public void NavigateTo<TViewModel>() where TViewModel : BaseViewModel
        {
            _navigationService.NavigateTo<TViewModel>();
        }
        public void NavigateTo<TViewModel>(object parameter) where TViewModel : BaseViewModel
        {
            _navigationService.NavigateTo<TViewModel>(parameter);
        }
        public void NavigateToViewModel(BaseViewModel viewModel)
        {
            CurrentViewModel = viewModel;
        }
    }
}