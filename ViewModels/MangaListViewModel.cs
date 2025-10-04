using MSCS.Commands;
using MSCS.Interfaces;
using MSCS.Models;
using MSCS.Sources;
using MSCS.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace MSCS.ViewModels
{
    public class MangaListViewModel : BaseViewModel, IDisposable
    {
        private readonly INavigationService _navigation;

        private IMangaSource _source;
        private Manga? _selectedManga;
        private string _searchQuery = string.Empty;
        private string _activeQuery = string.Empty;
        private string _selectedSourceKey = string.Empty;
        private int _currentPage;
        private bool _canLoadMore = true;
        private bool _isLoading;
        private CancellationTokenSource? _searchCts;
        private bool _disposed;

        public MangaListViewModel(string sourceKey, INavigationService navigationService)
        {
            _navigation = navigationService ?? throw new ArgumentNullException(nameof(navigationService));

            AvailableSources = new ObservableCollection<string>
            {
                "mangaread",
                "mangadex"
            };

            MangaResults = new ObservableCollection<Manga>();

            SearchCommand = new RelayCommand(async _ => await ExecuteSearchAsync(), _ => CanSearch());
            MangaSelectedCommand = new RelayCommand(OnMangaSelected);

            if (!string.IsNullOrWhiteSpace(sourceKey) && !AvailableSources.Contains(sourceKey))
            {
                AvailableSources.Add(sourceKey);
            }

            SelectedSourceKey = string.IsNullOrWhiteSpace(sourceKey) ? AvailableSources[0] : sourceKey;
        }

        #region Public Properties
        public event EventHandler<Manga?>? MangaSelected;
        public ObservableCollection<string> AvailableSources { get; }

        public ObservableCollection<Manga> MangaResults { get; }

        public ICommand SearchCommand { get; }

        public ICommand MangaSelectedCommand { get; }

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
                    // reset paging state when source changes
                    _activeQuery = string.Empty;
                    _currentPage = 0;
                    CanLoadMore = true;
                    MangaResults.Clear();
                    CancelActiveSearch(resetLoading: true);
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public Manga? SelectedManga
        {
            get => _selectedManga;
            set
            {
                if (SetProperty(ref _selectedManga, value))
                {
                    MangaSelected?.Invoke(this, value);
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

        #endregion

        #region Commands / Actions

        private bool CanSearch()
        {
            return !_disposed &&
                   !IsLoading &&
                   !string.IsNullOrWhiteSpace(SelectedSourceKey) &&
                   !string.IsNullOrWhiteSpace(SearchQuery);
        }

        private async Task ExecuteSearchAsync()
        {
            ThrowIfDisposed();
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
            ThrowIfDisposed();

            if (IsLoading)
            {
                return;
            }

            var sanitized = query?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                return;
            }

            if (_source == null)
            {
                Debug.WriteLine("Search aborted. No source configured.");
                return;
            }

            SearchQuery = sanitized;
            _activeQuery = sanitized;

            MangaResults.Clear();
            CanLoadMore = true;
            _currentPage = 0;

            var cts = CreateSearchToken();

            IsLoading = true;
            try
            {
                var firstPageResults = await _source.SearchMangaAsync(sanitized, cts.Token).ConfigureAwait(false);

                await RunOnUiThreadAsync(() =>
                {
                    foreach (var manga in firstPageResults)
                    {
                        MangaResults.Add(manga);
                    }
                }, cts.Token).ConfigureAwait(false);

                Debug.WriteLine($"Loaded {MangaResults.Count} manga for query: {sanitized}");
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"Search cancelled for query: {sanitized}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Search failed for query {sanitized}: {ex}");
                throw;
            }
            finally
            {
                if (ReferenceEquals(_searchCts, cts))
                {
                    IsLoading = false;
                }
            }
        }

        public async Task LoadMoreAsync()
        {
            ThrowIfDisposed();

            if (!CanLoadMore || string.IsNullOrWhiteSpace(_activeQuery))
            {
                return;
            }

            var tokenSource = _searchCts;
            if (tokenSource == null || tokenSource.IsCancellationRequested)
            {
                return;
            }
            if (_source == null)
            {
                Debug.WriteLine("Load more aborted. No source configured.");
                return;
            }

            var nextPage = _currentPage + 1;
            IsLoading = true;

            try
            {
                var moreHtml = await _source.LoadMoreSeriesHtmlAsync(_activeQuery, nextPage, tokenSource.Token).ConfigureAwait(false);
                Debug.WriteLine($"Loading more manga for page {nextPage}: {_activeQuery}");
                if (string.IsNullOrWhiteSpace(moreHtml))
                {
                    CanLoadMore = false;
                    return;
                }

                var moreManga = await Task.Run(() => _source.ParseMangaFromHtmlFragment(moreHtml), tokenSource.Token).ConfigureAwait(false);
                if (moreManga.Count == 0)
                {
                    CanLoadMore = false;
                    return;
                }

                _currentPage = nextPage;

                await RunOnUiThreadAsync(() =>
                {
                    foreach (var manga in moreManga)
                    {
                        MangaResults.Add(manga);
                    }
                }, tokenSource.Token).ConfigureAwait(false);

                Debug.WriteLine($"Loaded {moreManga.Count} more manga, total: {MangaResults.Count}");
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"Load more cancelled for query {_activeQuery} page {nextPage}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Load more failed for query {_activeQuery} page {nextPage}: {ex}");
                throw;
            }
            finally
            {
                IsLoading = false;
            }
        }

        public void OnMangaSelected(object obj)
        {
            ThrowIfDisposed();
            if (obj is Manga selectedManga)
            {
                SelectedManga = selectedManga;
                // Optionally navigate:
                // _navigation.NavigateTo("MangaDetails", selectedManga);
            }
        }

        #endregion

        #region Source / Lifecycle

        public void SetSource(IMangaSource source)
        {
            ThrowIfDisposed();
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (ReferenceEquals(_source, source))
            {
                return;
            }

            _source = source;
        }

        private void UpdateSourceFromKey(string key)
        {
            if (_disposed) return;
            if (string.IsNullOrWhiteSpace(key)) return;

            var resolved = SourceRegistry.Resolve(key);
            if (resolved == null)
            {
                Debug.WriteLine($"Source with key '{key}' could not be resolved.");
                return;
            }

            SetSource(resolved);
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            CancelActiveSearch(resetLoading: true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Helpers

        private CancellationTokenSource CreateSearchToken()
        {
            var next = new CancellationTokenSource();
            var previous = Interlocked.Exchange(ref _searchCts, next);
            if (previous != null)
            {
                try
                {
                    if (!previous.IsCancellationRequested)
                    {
                        previous.Cancel();
                    }
                }
                catch (ObjectDisposedException)
                {
                }
                finally
                {
                    previous.Dispose();
                }
            }

            return next;
        }

        private void CancelActiveSearch(bool resetLoading)
        {
            var current = Interlocked.Exchange(ref _searchCts, null);
            if (current != null)
            {
                try
                {
                    if (!current.IsCancellationRequested)
                    {
                        current.Cancel();
                    }
                }
                catch (ObjectDisposedException)
                {
                }
                finally
                {
                    current.Dispose();
                }
            }

            if (resetLoading)
            {
                IsLoading = false;
            }
        }

        private static Task RunOnUiThreadAsync(Action action, CancellationToken cancellationToken)
        {
            var dispatcher = Application.Current?.Dispatcher;

            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
                return Task.CompletedTask;
            }

            // Note: not all frameworks have InvokeAsync(Action, DispatcherPriority, CancellationToken).
            // Use the widely available overload and just ignore the token here.
            return dispatcher.InvokeAsync(action, DispatcherPriority.DataBind).Task;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MangaListViewModel));
            }
        }

        #endregion
    }
}
