using MSCS.Commands;
using MSCS.Models;
using MSCS.Interfaces;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Diagnostics;
using MSCS.Views;
using MSCS.Sources;

namespace MSCS.ViewModels
{
    public class MangaListViewModel : BaseViewModel
    {
        private IMangaSource _source;
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

        public MangaListViewModel(IMangaSource source, INavigationService navigationService)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _navigationService = navigationService;
            MangaSelectedCommand = new RelayCommand(OnMangaSelected);
        }
        public MangaListViewModel()
        {
            _navigationService = App.Current.MainWindow?.DataContext as INavigationService ?? throw new InvalidOperationException("NavigationService is not available.");
            _source = SourceRegistry.Resolve("mangaread") ?? throw new InvalidOperationException("Source not registered.");
            MangaSelectedCommand = new RelayCommand(OnMangaSelected);
            _selectedManga = null!;
            currentQuery = string.Empty;
        }
        public MangaListViewModel(string sourceKey, INavigationService navigationService)
            : this(SourceRegistry.Resolve(sourceKey) ?? throw new InvalidOperationException("Source not registered."), navigationService)
        {
        }


        public void SetSource(IMangaSource source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
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

            var firstPageResults = await _source.SearchMangaAsync(query);
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

            var moreHtml = await _source.LoadMoreSeriesHtmlAsync(currentQuery, currentPage);
            Debug.WriteLine($"Loading more manga for page {currentPage}: {currentQuery}");
            if (string.IsNullOrEmpty(moreHtml))
            {
                CanLoadMore = false;
                IsLoading = false;
                return;
            }

            var moreManga = _source.ParseMangaFromHtmlFragment(moreHtml);
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