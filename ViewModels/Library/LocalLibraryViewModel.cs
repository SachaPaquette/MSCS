using MSCS.Commands;
using MSCS.Models;
using MSCS.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using System;

namespace MSCS.ViewModels
{
    public class LocalLibraryViewModel : BaseViewModel, IDisposable
    {
        private readonly LocalLibraryService _libraryService;
        private readonly UserSettings _userSettings;
        private bool _disposed;
        private bool _hasLibraryPath;
        private bool _isLoading;
        private string _searchQuery = string.Empty;
        private string _selectedGroup = "All";
        private LocalMangaEntryItemViewModel? _selectedManga;
        private bool _isLibraryLoaded;
        private const int MangaDisplayBatchSize = 60;
        private readonly ObservableCollection<LocalMangaEntryItemViewModel> _visibleEntries = new();
        private readonly ReadOnlyObservableCollection<LocalMangaEntryItemViewModel> _readOnlyVisibleEntries;
        private readonly List<LocalMangaEntryItemViewModel> _allEntries = new();
        private readonly List<LocalMangaEntryItemViewModel> _filteredEntries = new();
        private readonly ObservableCollection<string> _groupFilters = new();
        private int _visibleEntryCursor;
        private bool _hasMoreResults;
        private CancellationTokenSource? _reloadCancellationTokenSource;

        public LocalLibraryViewModel(LocalLibraryService libraryService, UserSettings userSettings)
        {
            _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
            _userSettings = userSettings ?? throw new ArgumentNullException(nameof(userSettings));
            _libraryService.LibraryPathChanged += OnLibraryPathChanged;
            _userSettings.BookmarksChanged += OnBookmarksChanged;
            _readOnlyVisibleEntries = new ReadOnlyObservableCollection<LocalMangaEntryItemViewModel>(_visibleEntries);

            RefreshCommand = new AsyncRelayCommand(ReloadLibraryAsync, () => !_isLoading);
            OpenLibraryFolderCommand = new RelayCommand(_ => OpenLibraryFolder(), _ => HasLibraryPath);
            LoadMoreMangaCommand = new RelayCommand(_ => LoadMoreVisibleManga(), _ => HasMoreResults);
            InitializeGroups();
            _ = ReloadLibraryAsync();
        }

        public event EventHandler<Manga?>? MangaSelected;

        public ReadOnlyObservableCollection<LocalMangaEntryItemViewModel> MangaEntries => _readOnlyVisibleEntries;

        public ObservableCollection<string> GroupFilters => _groupFilters;

        public bool HasLibraryPath
        {
            get => _hasLibraryPath;
            private set
            {
                if (SetProperty(ref _hasLibraryPath, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                    OnPropertyChanged(nameof(IsLibraryEmpty));
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
                    OnPropertyChanged(nameof(IsLibraryEmpty));
                }
            }
        }

        public string LibraryPath => _libraryService.LibraryPath ?? string.Empty;

        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (SetProperty(ref _searchQuery, value))
                {
                    ApplyFilters(resetCursor: true);
                }
            }
        }

        public string SelectedGroup
        {
            get => _selectedGroup;
            set
            {
                if (SetProperty(ref _selectedGroup, value))
                {
                    ApplyFilters(resetCursor: true);
                }
            }
        }

        public LocalMangaEntryItemViewModel? SelectedManga
        {
            get => _selectedManga;
            set
            {
                if (SetProperty(ref _selectedManga, value) && value != null)
                {
                    MangaSelected?.Invoke(this, value.ToManga());
                }
            }
        }

        public ICommand RefreshCommand { get; }
        public ICommand OpenLibraryFolderCommand { get; }
        public ICommand LoadMoreMangaCommand { get; }

        public bool HasMoreResults
        {
            get => _hasMoreResults;
            private set
            {
                if (SetProperty(ref _hasMoreResults, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool IsLibraryEmpty => HasLibraryPath && _isLibraryLoaded && !IsLoading && _visibleEntries.Count == 0;

        public void EnsureLibraryLoaded()
        {
            if (_disposed) return;
            if (!_isLibraryLoaded && !_isLoading)
            {
                _ = ReloadLibraryAsync();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _libraryService.LibraryPathChanged -= OnLibraryPathChanged;
            _userSettings.BookmarksChanged -= OnBookmarksChanged;
            _reloadCancellationTokenSource?.Cancel();
            DisposeEntries(_allEntries);
            _allEntries.Clear();
            _filteredEntries.Clear();
            _visibleEntries.Clear();
        }

        private async Task ReloadLibraryAsync()
        {
            if (_disposed) return;

            _reloadCancellationTokenSource?.Cancel();
            var currentCts = new CancellationTokenSource();
            _reloadCancellationTokenSource = currentCts;

            IsLoading = true;
            if (_isLibraryLoaded)
            {
                _isLibraryLoaded = false;
                OnPropertyChanged(nameof(IsLibraryEmpty));
            }

            var loadSucceeded = false;
            try
            {
                HasLibraryPath = _libraryService.LibraryPathExists();

                IReadOnlyList<LocalMangaEntry> rawEntries = Array.Empty<LocalMangaEntry>();
                if (HasLibraryPath)
                {
                    // Do I/O off the UI thread.
                    rawEntries = await _libraryService.GetMangaEntriesAsync(currentCts.Token).ConfigureAwait(false);
                }

                var newItems = new List<LocalMangaEntryItemViewModel>(rawEntries.Count);
                foreach (var entry in rawEntries)
                {
                    newItems.Add(new LocalMangaEntryItemViewModel(entry, _userSettings));
                }

                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    await dispatcher.InvokeAsync(() =>
                    {
                        ReplaceAllEntries(newItems);
                    });
                }
                else
                {
                    ReplaceAllEntries(newItems);
                }

                loadSucceeded = true;
            }
            catch (OperationCanceledException) when (currentCts.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load local library: {ex}");
            }
            finally
            {
                if (ReferenceEquals(_reloadCancellationTokenSource, currentCts))
                {
                    _reloadCancellationTokenSource = null;
                    IsLoading = false;
                    if (_isLibraryLoaded != loadSucceeded)
                    {
                        _isLibraryLoaded = loadSucceeded;
                        OnPropertyChanged(nameof(IsLibraryEmpty));
                    }
                }
                currentCts.Dispose();
            }
        }

        private void ReplaceAllEntries(List<LocalMangaEntryItemViewModel> newItems)
        {
            DisposeEntries(_allEntries);
            _allEntries.Clear();
            _allEntries.AddRange(newItems);

            UpdateGroups(_allEntries);
            UpdateBookmarkStates();
            ApplyFilters(resetCursor: true);
            OnPropertyChanged(nameof(LibraryPath));
        }

        private void UpdateGroups(IReadOnlyCollection<LocalMangaEntryItemViewModel> entries)
        {
            var previouslySelected = SelectedGroup;

            _groupFilters.Clear();
            _groupFilters.Add("All");

            if (entries.Any(e => string.Equals(e.GroupKey, "#", StringComparison.OrdinalIgnoreCase)))
            {
                _groupFilters.Add("#");
            }

            foreach (var letter in Enumerable.Range('A', 26).Select(i => ((char)i).ToString()))
            {
                if (entries.Any(e => string.Equals(e.GroupKey, letter, StringComparison.OrdinalIgnoreCase)))
                {
                    _groupFilters.Add(letter);
                }
            }

            if (!_groupFilters.Contains(previouslySelected))
            {
                SelectedGroup = "All";
            }
        }

        private void InitializeGroups()
        {
            _groupFilters.Clear();
            _groupFilters.Add("All");
        }

        private void OpenLibraryFolder()
        {
            if (!HasLibraryPath) return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = LibraryPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to open folder '{LibraryPath}': {ex.Message}");
            }
        }

        private void OnLibraryPathChanged(object? sender, EventArgs e)
        {
            if (_isLibraryLoaded)
            {
                _isLibraryLoaded = false;
                OnPropertyChanged(nameof(IsLibraryEmpty));
            }
            _ = ReloadLibraryAsync();
        }

        private void ApplyFilters(bool resetCursor)
        {
            _filteredEntries.Clear();

            foreach (var entry in _allEntries)
            {
                if (MatchesFilters(entry))
                {
                    _filteredEntries.Add(entry);
                }
            }

            if (resetCursor)
            {
                SelectedManga = null;
                ResetVisibleManga();
            }
            else
            {
                TrimVisibleManga();
            }

            OnPropertyChanged(nameof(IsLibraryEmpty));
        }

        private bool MatchesFilters(LocalMangaEntryItemViewModel entry)
        {
            if (!string.IsNullOrWhiteSpace(SearchQuery) &&
                entry.Title.IndexOf(SearchQuery, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            if (string.Equals(SelectedGroup, "All", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(SelectedGroup, "#", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(entry.GroupKey, "#", StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(entry.GroupKey, SelectedGroup, StringComparison.OrdinalIgnoreCase);
        }

        private void ResetVisibleManga()
        {
            _visibleEntryCursor = 0;
            _visibleEntries.Clear();
            LoadMoreVisibleManga();
        }

        private void TrimVisibleManga()
        {
            if (_filteredEntries.Count == 0)
            {
                _visibleEntries.Clear();
                HasMoreResults = false;
                return;
            }

            if (_visibleEntryCursor >= _filteredEntries.Count)
            {
                HasMoreResults = false;
                return;
            }

            var requiredCount = Math.Min(_filteredEntries.Count, _visibleEntryCursor);

            if (_visibleEntryCursor < _filteredEntries.Count)
            {
                var item = _filteredEntries[_visibleEntryCursor];
                if (!_visibleEntries.Contains(item))
                {
                    _visibleEntries.Add(item);
                }
            }

            if (_visibleEntries.Count < requiredCount)
            {
                for (var i = _visibleEntries.Count; i < requiredCount; i++)
                {
                    _visibleEntries.Add(_filteredEntries[i]);
                }
            }

            HasMoreResults = _visibleEntryCursor < _filteredEntries.Count;
        }

        private void LoadMoreVisibleManga()
        {
            if (_filteredEntries.Count == 0)
            {
                HasMoreResults = false;
                return;
            }

            var target = Math.Min(_filteredEntries.Count, _visibleEntryCursor + MangaDisplayBatchSize);

            while (_visibleEntryCursor < target)
            {
                _visibleEntries.Add(_filteredEntries[_visibleEntryCursor]);
                _visibleEntryCursor++;
            }

            HasMoreResults = _visibleEntryCursor < _filteredEntries.Count;
        }

        private void OnBookmarksChanged(object? sender, EventArgs e)
        {
            if (_disposed) return;

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
            foreach (var entry in _allEntries)
            {
                entry.UpdateBookmarkState();
            }
        }

        private static void DisposeEntries(IEnumerable<LocalMangaEntryItemViewModel> entries)
        {
            foreach (var entry in entries)
            {
                entry.Dispose();
            }
        }
    }
}