using MSCS.Commands;
using MSCS.Models;
using MSCS.Interfaces;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Diagnostics;
using MSCS.Views;

namespace MSCS.ViewModels
{
    public class MangaListViewModel : BaseViewModel
    {
        private IScraper _scraper;
        private readonly INavigationService _navigationService;
        public ObservableCollection<Manga> MangaResults { get; } = new ObservableCollection<Manga>();
        public ICommand MangaSelectedCommand { get; }
        public event EventHandler<Manga> MangaSelected;
        private int currentPage = 0;
        private bool canLoadMore = true;
        private bool isLoading = false;
        private Manga _selectedManga;
        private string currentQuery;

        public Manga SelectedManga
        {
            get => _selectedManga;
            set
            {
                if (_selectedManga != value)
                {
                    _selectedManga = value;
                    OnPropertyChanged();
                    if (_selectedManga != null)
                    {
                        MangaSelected?.Invoke(this, _selectedManga);
                    }
                }
            }
        }

        public bool IsLoading
        {
            get => isLoading;
            private set => SetProperty(ref isLoading, value);
        }

        public bool CanLoadMore
        {
            get => canLoadMore && !IsLoading;
            private set => SetProperty(ref canLoadMore, value);
        }

        public MangaListViewModel(IScraper scraper, INavigationService navigationService)
        {
            _scraper = scraper;
            _navigationService = navigationService;
            MangaSelectedCommand = new RelayCommand(OnMangaSelected);
        }
        public MangaListViewModel()
        {
            _navigationService = App.Current.MainWindow?.DataContext as INavigationService ?? throw new InvalidOperationException("NavigationService is not available.");
            _scraper = null!;
            MangaSelectedCommand = new RelayCommand(OnMangaSelected);
            _selectedManga = null!;
            currentQuery = string.Empty;
        }

        public void SetScraper(IScraper scraper)
        {
            _scraper = scraper ?? throw new ArgumentNullException(nameof(scraper));
        }

        public void OnMangaSelected(object obj)
        {
            if (obj is Manga selectedManga)
            {
                MangaSelected?.Invoke(this, selectedManga);
            }
        }

        public async Task SearchAsync(string query)
        {
            if (IsLoading) return;

            currentQuery = query;
            MangaResults.Clear();
            CanLoadMore = true;
            currentPage = 0;
            IsLoading = true;

            var firstPageResults = await _scraper.SearchMangaAsync(query);
            foreach (var manga in firstPageResults)
                MangaResults.Add(manga);

            IsLoading = false;
            Debug.WriteLine($"Loaded {MangaResults.Count} manga for query: {query}");
        }

        public async Task LoadMoreAsync()
        {
            if (!CanLoadMore) return;

            IsLoading = true;
            currentPage++;

            var moreHtml = await _scraper.LoadMoreSeriesHtmlAsync(currentQuery, currentPage);
            Debug.WriteLine($"Loading more manga for page {currentPage}: {currentQuery}");
            if (string.IsNullOrEmpty(moreHtml))
            {
                CanLoadMore = false;
                IsLoading = false;
                return;
            }

            var moreManga = _scraper.ParseMangaFromHtmlFragment(moreHtml);
            if (moreManga.Count == 0)
            {
                CanLoadMore = false;
            }
            else
            {
                foreach (var manga in moreManga)
                    MangaResults.Add(manga);
            }

            IsLoading = false;
            Debug.WriteLine($"Loaded {moreManga.Count} more manga, total: {MangaResults.Count}");
        }
    }
}