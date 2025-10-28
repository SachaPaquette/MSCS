using MSCS.Commands;
using MSCS.Interfaces;
using MSCS.Models;
using MSCS.Services;
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
        private readonly UserSettings _userSettings;
        private IMangaSource _source;
        private MangaSearchResultItemViewModel? _selectedResult;
        private string _searchQuery = string.Empty;
        private string _activeQuery = string.Empty;
        private string _selectedSourceKey = string.Empty;
        private int _currentPage;
        private bool _canLoadMore = true;
        private bool _isLoading;
        private CancellationTokenSource? _searchCts;
        private bool _disposed;

        public MangaListViewModel(string sourceKey, INavigationService navigationService, UserSettings userSettings)
        {
            _navigation = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
            _userSettings = userSettings ?? throw new ArgumentNullException(nameof(userSettings));
            AvailableSources = new ObservableCollection<SourceDescriptor>(SourceRegistry.GetAllDescriptors());

            MangaResults = new ObservableCollection<MangaSearchResultItemViewModel>();

            SearchCommand = new RelayCommand(async _ => await ExecuteSearchAsync(), _ => CanSearch());
            MangaSelectedCommand = new RelayCommand(OnMangaSelected);
            _userSettings.BookmarksChanged += OnBookmarksChanged;

            InitializeSelectedSource(sourceKey);
        }

        #region Public Properties
        public event EventHandler<Manga?>? MangaSelected;
        public ObservableCollection<MangaSearchResultItemViewModel> MangaResults { get; }
        public ObservableCollection<SourceDescriptor> AvailableSources { get; }

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
                    DisposeResults();
                    MangaResults.Clear();
                    CancelActiveSearch(resetLoading: true);
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public MangaSearchResultItemViewModel? SelectedResult
        {
            get => _selectedResult;
            set
            {
                if (SetProperty(ref _selectedResult, value))
                {
                    MangaSelected?.Invoke(this, value?.Manga);
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
        private void InitializeSelectedSource(string sourceKey)
        {
            if (AvailableSources.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(sourceKey))
                {
                    var descriptor = new SourceDescriptor(sourceKey, sourceKey);
                    AvailableSources.Add(descriptor);
                    SelectedSourceKey = descriptor.Key;
                }

                return;
            }

            var initialKey = string.IsNullOrWhiteSpace(sourceKey) ? SourceKeyConstants.DefaultExternal : sourceKey;

            if (!string.IsNullOrWhiteSpace(initialKey))
            {
                var descriptor = SourceRegistry.GetDescriptor(initialKey) ?? new SourceDescriptor(initialKey, initialKey);
                if (!AvailableSources.Any(source => source.Key.Equals(descriptor.Key, StringComparison.OrdinalIgnoreCase)))
                {
                    AvailableSources.Add(descriptor);
                }

                SelectedSourceKey = descriptor.Key;
            }
            else
            {
                SelectedSourceKey = AvailableSources[0].Key;
            }
        }
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

            DisposeResults();
            MangaResults.Clear();
            CanLoadMore = true;
            _currentPage = 0;

            var cts = CreateSearchToken();

            IsLoading = true;
            try
            {
                var firstPageResults = await _source.SearchMangaAsync(sanitized, cts.Token).ConfigureAwait(false);

                Debug.WriteLine(firstPageResults.Count == 0
                    ? $"No results found for query: {sanitized}"
                    : $"Search returned {firstPageResults.Count} results for query: {sanitized}");

                await RunOnUiThreadAsync(() =>
                {
                    DisposeResults();
                    MangaResults.Clear();
                    foreach (var manga in firstPageResults)
                    {
                        MangaResults.Add(CreateResultItem(manga, SelectedSourceKey));
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
            var activeSourceKey = SelectedSourceKey;
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
                        MangaResults.Add(CreateResultItem(manga, activeSourceKey));
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
            switch (obj)
            {
                case MangaSearchResultItemViewModel item:
                    SelectedResult = item;
                    break;
                case Manga selectedManga:
                    var match = MangaResults.FirstOrDefault(result => ReferenceEquals(result.Manga, selectedManga));
                    if (match != null)
                    {
                        SelectedResult = match;
                    }
                    break;
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
            _userSettings.BookmarksChanged -= OnBookmarksChanged;
            CancelActiveSearch(resetLoading: true);
            DisposeResults();
            MangaResults.Clear();
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


        private MangaSearchResultItemViewModel CreateResultItem(Manga manga, string sourceKey)
        {
            var effectiveSourceKey = string.IsNullOrWhiteSpace(sourceKey) ? string.Empty : sourceKey;
            var item = new MangaSearchResultItemViewModel(manga, effectiveSourceKey, _userSettings);
            return item;
        }

        private void DisposeResults()
        {
            foreach (var item in MangaResults)
            {
                item.Dispose();
            }
        }

        private void OnBookmarksChanged(object? sender, EventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(UpdateBookmarkStates);
            }
            else
            {
                UpdateBookmarkStates();
            }
        }

        private void UpdateBookmarkStates()
        {
            foreach (var item in MangaResults)
            {
                item.UpdateBookmarkState();
            }
        }

        private static Task RunOnUiThreadAsync(Action action, CancellationToken cancellationToken)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;

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
