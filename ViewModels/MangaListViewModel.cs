using MSCS.Commands;
using MSCS.Interfaces;
using MSCS.Models;
using MSCS.Sources;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Input;

namespace MSCS.ViewModels
{
    public class MangaListViewModel : BaseViewModel
    {
        private IMangaSource _source;
        private Manga _selectedManga;
        private string _searchQuery = string.Empty;
        private string _activeQuery = string.Empty;
        private string _selectedSourceKey = string.Empty;
        private int _currentPage;
        private bool _canLoadMore = true;
        private bool _isLoading;

        public MangaListViewModel(string sourceKey, INavigationService navigationService)
        {
            if (navigationService == null)
            {
                throw new ArgumentNullException(nameof(navigationService));
            }

            AvailableSources = new ObservableCollection<string>
            {
                "mangaread",
                "mangadex"
            };

            SearchCommand = new RelayCommand(async _ => await ExecuteSearchAsync(), _ => CanSearch());
            MangaSelectedCommand = new RelayCommand(OnMangaSelected);

            if (!string.IsNullOrWhiteSpace(sourceKey) && !AvailableSources.Contains(sourceKey))
            {
                AvailableSources.Add(sourceKey);
            }

            SelectedSourceKey = string.IsNullOrWhiteSpace(sourceKey) ? AvailableSources[0] : sourceKey;
        }

        public ObservableCollection<Manga> MangaResults { get; } = new();
        public ObservableCollection<string> AvailableSources { get; }

        public event EventHandler<Manga> MangaSelected;

        public ICommand MangaSelectedCommand { get; }
        public ICommand SearchCommand { get; }

        public Manga SelectedManga
        {
            get => _selectedManga;
            set
            {
                if (SetProperty(ref _selectedManga, value) && value != null)
                {
                    MangaSelected?.Invoke(this, value);
                }
            }
        }

        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (SetProperty(ref _searchQuery, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string SelectedSourceKey
        {
            get => _selectedSourceKey;
            set
            {
                if (SetProperty(ref _selectedSourceKey, value))
                {
                    UpdateSourceFromKey(value);
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool CanLoadMore
        {
            get => _canLoadMore && !IsLoading;
            private set => SetProperty(ref _canLoadMore, value);
        }

        private bool CanSearch()
        {
            return !IsLoading &&
                   !string.IsNullOrWhiteSpace(SelectedSourceKey) &&
                   !string.IsNullOrWhiteSpace(SearchQuery);
        }

        private async Task ExecuteSearchAsync()
        {
            var query = SearchQuery?.Trim() ?? string.Empty;
            var sourceKey = SelectedSourceKey;

            if (string.IsNullOrWhiteSpace(sourceKey) || string.IsNullOrWhiteSpace(query))
            {
                return;
            }

            var source = SourceRegistry.Resolve(sourceKey);
            if (source == null)
            {
                Debug.WriteLine($"Search aborted. Source '{sourceKey}' was not found.");
                return;
            }

            SetSource(source);
            await SearchAsync(query);
        }

        public async Task SearchAsync(string query)
        {
            if (IsLoading)
            {
                return;
            }

            var sanitized = query?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                return;
            }

            SearchQuery = sanitized;
            _activeQuery = sanitized;

            MangaResults.Clear();
            CanLoadMore = true;
            _currentPage = 0;
            IsLoading = true;

            var firstPageResults = await _source.SearchMangaAsync(sanitized);
            foreach (var manga in firstPageResults)
            {
                MangaResults.Add(manga);
            }

            IsLoading = false;
            Debug.WriteLine($"Loaded {MangaResults.Count} manga for query: {sanitized}");
        }

        public async Task LoadMoreAsync()
        {
            if (!CanLoadMore || string.IsNullOrWhiteSpace(_activeQuery))
            {
                return;
            }

            IsLoading = true;
            _currentPage++;

            var moreHtml = await _source.LoadMoreSeriesHtmlAsync(_activeQuery, _currentPage);
            Debug.WriteLine($"Loading more manga for page {_currentPage}: {_activeQuery}");
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
                {
                    MangaResults.Add(manga);
                }
            }

            IsLoading = false;
            Debug.WriteLine($"Loaded {moreManga.Count} more manga, total: {MangaResults.Count}");
        }

        public void SetSource(IMangaSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (ReferenceEquals(_source, source))
            {
                return;
            }

            _source = source;
        }

        public void OnMangaSelected(object obj)
        {
            if (obj is Manga selectedManga)
            {
                SelectedManga = selectedManga;
            }
        }

        private void UpdateSourceFromKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            var resolved = SourceRegistry.Resolve(key);
            if (resolved == null)
            {
                Debug.WriteLine($"Source with key '{key}' could not be resolved.");
                return;
            }

            SetSource(resolved);
        }
    }
}