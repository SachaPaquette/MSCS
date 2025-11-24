using MSCS.Commands;
using MSCS.Models;
using MSCS.Services;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;

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
        private readonly ObservableCollection<object> _previewEntries = new();
        private readonly ReadOnlyObservableCollection<object> _readOnlyPreviewEntries;
        private readonly List<LocalMangaEntryItemViewModel> _allEntries = new();
        private readonly List<LocalMangaEntryItemViewModel> _filteredEntries = new();
        private readonly ObservableCollection<string> _groupFilters = new();
        private readonly ObservableCollection<LocalLibraryFolderEntryViewModel> _folderEntries = new();
        private readonly ReadOnlyObservableCollection<LocalLibraryFolderEntryViewModel> _readOnlyFolderEntries;
        private readonly ObservableCollection<LocalLibraryChapterEntryViewModel> _chapterFileEntries = new();
        private readonly ReadOnlyObservableCollection<LocalLibraryChapterEntryViewModel> _readOnlyChapterFileEntries;
        private int _visibleEntryCursor;
        private bool _hasMoreResults;
        private CancellationTokenSource? _reloadCancellationTokenSource;
        private readonly SemaphoreSlim _incrementalUpdateSemaphore = new(1, 1);
        private bool _isIncrementalRefresh;
        private string? _incrementalStatusMessage;
        private string? _currentFolderPath = string.Empty;
        private LocalLibraryFolderEntryViewModel? _selectedFolder;
        public LocalLibraryViewModel(LocalLibraryService libraryService, UserSettings userSettings)
        {
            _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
            _userSettings = userSettings ?? throw new ArgumentNullException(nameof(userSettings));
            _libraryService.LibraryPathChanged += OnLibraryPathChanged;
            _userSettings.BookmarksChanged += OnBookmarksChanged;
            _readOnlyVisibleEntries = new ReadOnlyObservableCollection<LocalMangaEntryItemViewModel>(_visibleEntries);
            _readOnlyPreviewEntries = new ReadOnlyObservableCollection<object>(_previewEntries);
            _readOnlyFolderEntries = new ReadOnlyObservableCollection<LocalLibraryFolderEntryViewModel>(_folderEntries);
            _readOnlyChapterFileEntries = new ReadOnlyObservableCollection<LocalLibraryChapterEntryViewModel>(_chapterFileEntries);

            _visibleEntries.CollectionChanged += OnPreviewSourceCollectionChanged;
            _folderEntries.CollectionChanged += OnPreviewSourceCollectionChanged;
            _chapterFileEntries.CollectionChanged += OnPreviewSourceCollectionChanged;

            RefreshCommand = new AsyncRelayCommand(ReloadLibraryAsync, () => !_isLoading);
            OpenLibraryFolderCommand = new RelayCommand(_ => OpenLibraryFolder(), _ => HasLibraryPath);
            LoadMoreMangaCommand = new RelayCommand(_ => LoadMoreVisibleManga(), _ => HasMoreResults);
            NavigateUpCommand = new RelayCommand(_ => NavigateUpOneLevel(), _ => CanNavigateUp);
            NavigateToFolderCommand = new RelayCommand(folder => NavigateToFolder(folder as LocalLibraryFolderEntryViewModel), folder => folder is LocalLibraryFolderEntryViewModel);
            InitializeGroups();
            _ = ReloadLibraryAsync();
        }

        public event EventHandler<Manga?>? MangaSelected;

        public ReadOnlyObservableCollection<LocalMangaEntryItemViewModel> MangaEntries => _readOnlyVisibleEntries;

        public ReadOnlyObservableCollection<object> PreviewEntries => _readOnlyPreviewEntries;

        public ObservableCollection<string> GroupFilters => _groupFilters;

        public ReadOnlyObservableCollection<LocalLibraryFolderEntryViewModel> FolderEntries => _readOnlyFolderEntries;

        public bool HasLibraryPath
        {
            get => _hasLibraryPath;
            private set
            {
                if (SetProperty(ref _hasLibraryPath, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                    OnPropertyChanged(nameof(IsLibraryEmpty));
                    OnPropertyChanged(nameof(CanNavigateUp));
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
                var normalizedValue = string.IsNullOrWhiteSpace(value) ? (_selectedGroup ?? "All") : value;

                if (SetProperty(ref _selectedGroup, normalizedValue))
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

        public LocalLibraryFolderEntryViewModel? SelectedFolder
        {
            get => _selectedFolder;
            set => SetProperty(ref _selectedFolder, value);
        }

        public ICommand RefreshCommand { get; }
        public ICommand OpenLibraryFolderCommand { get; }
        public ICommand LoadMoreMangaCommand { get; }
        public ICommand NavigateUpCommand { get; }
        public ICommand NavigateToFolderCommand { get; }

        public void NavigateToPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                if (!Directory.Exists(path) || !IsWithinLibrary(path))
                {
                    return;
                }

                CurrentFolderPath = path;
                SelectedFolder = null;
                LoadFolderEntries();
                ApplyFilters(resetCursor: true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to open folder '{path}': {ex.Message}");
            }
        }

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

        public bool IsIncrementalRefresh
        {
            get => _isIncrementalRefresh;
            private set
            {
                if (SetProperty(ref _isIncrementalRefresh, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string? IncrementalStatusMessage
        {
            get => _incrementalStatusMessage;
            private set => SetProperty(ref _incrementalStatusMessage, value);
        }


        public string CurrentFolderPath
        {
            get => _currentFolderPath ?? string.Empty;
            private set
            {
                if (SetProperty(ref _currentFolderPath, value))
                {
                    OnPropertyChanged(nameof(CanNavigateUp));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool CanNavigateUp
        {
            get
            {
                if (!HasLibraryPath || string.IsNullOrWhiteSpace(CurrentFolderPath) || string.IsNullOrWhiteSpace(LibraryPath))
                {
                    return false;
                }

                return !PathsEqual(CurrentFolderPath, LibraryPath);
            }
        }

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
            _visibleEntries.CollectionChanged -= OnPreviewSourceCollectionChanged;
            _folderEntries.CollectionChanged -= OnPreviewSourceCollectionChanged;
            _chapterFileEntries.CollectionChanged -= OnPreviewSourceCollectionChanged;

            _reloadCancellationTokenSource?.Cancel();
            DisposeEntries(_allEntries);
            _allEntries.Clear();
            _filteredEntries.Clear();
            _visibleEntries.Clear();
            _folderEntries.Clear();
            _incrementalUpdateSemaphore.Dispose();
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

                    void CompleteReload()
                    {
                        IsLoading = false;
                        if (_isLibraryLoaded != loadSucceeded)
                        {
                            _isLibraryLoaded = loadSucceeded;
                            OnPropertyChanged(nameof(IsLibraryEmpty));
                        }
                    }

                    var dispatcher = System.Windows.Application.Current?.Dispatcher;
                    if (dispatcher != null && !dispatcher.CheckAccess())
                    {
                        dispatcher.Invoke(CompleteReload);
                    }
                    else
                    {
                        CompleteReload();
                    }
                }
                currentCts.Dispose();
            }
        }

        private async Task HandleLibraryChangeAsync(LibraryChangedEventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            if (!_libraryService.LibraryPathExists())
            {
                await ReloadLibraryAsync();
                return;
            }

            var entryPath = e.EntryPath ?? (!string.IsNullOrEmpty(e.FullPath) ? _libraryService.ResolveEntryPath(e.FullPath) : null);
            var oldEntryPath = e.OldEntryPath ?? (!string.IsNullOrEmpty(e.OldPath) ? _libraryService.ResolveEntryPath(e.OldPath) : null);

            if (string.IsNullOrEmpty(entryPath) && string.IsNullOrEmpty(oldEntryPath))
            {
                await ReloadLibraryAsync();
                return;
            }

            await _incrementalUpdateSemaphore.WaitAsync();
            try
            {
                var targetPath = entryPath ?? oldEntryPath;
                if (string.IsNullOrEmpty(targetPath))
                {
                    return;
                }

                UpdateIncrementalStatus(true, CreateIncrementalStatusMessage(targetPath));

                if (e.Kind == LibraryChangeKind.DirectoryRemoved)
                {
                    await ApplyEntryRemovalAsync(oldEntryPath ?? targetPath);
                    return;
                }

                if (e.Kind == LibraryChangeKind.Renamed &&
                    !string.IsNullOrEmpty(oldEntryPath) &&
                    !string.Equals(oldEntryPath, entryPath, StringComparison.OrdinalIgnoreCase))
                {
                    await ApplyEntryRemovalAsync(oldEntryPath);
                }

                var refreshedEntry = await _libraryService.GetMangaEntryAsync(targetPath);
                await ApplyEntryUpdateAsync(targetPath, refreshedEntry);
            }
            finally
            {
                UpdateIncrementalStatus(false, null);
                _incrementalUpdateSemaphore.Release();
            }
        }

        private void ReplaceAllEntries(List<LocalMangaEntryItemViewModel> newItems)
        {
            DisposeEntries(_allEntries);
            _allEntries.Clear();
            _allEntries.AddRange(newItems);

            UpdateGroups(_allEntries);
            UpdateBookmarkStates();
            EnsureCurrentFolderValid();
            LoadFolderEntries();
            LoadChapterFileEntries();
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


        private void OnLibraryPathChanged(object? sender, LibraryChangedEventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            if (e.Kind == LibraryChangeKind.Reset)
            {
                if (_isLibraryLoaded)
                {
                    _isLibraryLoaded = false;
                    OnPropertyChanged(nameof(IsLibraryEmpty));
                }

                _ = ReloadLibraryAsync();
                return;
            }

            _ = HandleLibraryChangeAsync(e);
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

            if (!IsEntryInCurrentFolder(entry))
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


        private async Task ApplyEntryUpdateAsync(string path, LocalMangaEntry? entry)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                await dispatcher.InvokeAsync(() => ApplyEntryUpdateInternal(path, entry));
            }
            else
            {
                ApplyEntryUpdateInternal(path, entry);
            }
        }

        private async Task ApplyEntryRemovalAsync(string? path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                await dispatcher.InvokeAsync(() => RemoveEntry(path));
            }
            else
            {
                RemoveEntry(path);
            }
        }

        private void ApplyEntryUpdateInternal(string path, LocalMangaEntry? entry)
        {
            if (entry == null)
            {
                RemoveEntry(path);
                return;
            }

            UpsertEntry(entry);
        }

        private void UpsertEntry(LocalMangaEntry entry)
        {
            var existingIndex = _allEntries.FindIndex(vm => string.Equals(vm.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                var existing = _allEntries[existingIndex];
                var wasSelected = ReferenceEquals(SelectedManga, existing);
                _allEntries.RemoveAt(existingIndex);
                existing.UpdateEntry(entry);
                InsertSorted(_allEntries, existing);

                if (wasSelected)
                {
                    SelectedManga = existing;
                }
            }
            else
            {
                var viewModel = new LocalMangaEntryItemViewModel(entry, _userSettings);
                InsertSorted(_allEntries, viewModel);
            }

            UpdateGroups(_allEntries);
            UpdateBookmarkStates();
            ApplyFilters(resetCursor: false);
            LoadFolderEntries();
        }

        private void RemoveEntry(string path)
        {
            var index = _allEntries.FindIndex(vm => string.Equals(vm.Path, path, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return;
            }

            var entry = _allEntries[index];
            var wasSelected = ReferenceEquals(SelectedManga, entry);

            _allEntries.RemoveAt(index);
            entry.Dispose();

            if (wasSelected)
            {
                SelectedManga = null;
            }

            UpdateGroups(_allEntries);
            ApplyFilters(resetCursor: false);
            LoadFolderEntries();
        }

        private static void InsertSorted(List<LocalMangaEntryItemViewModel> entries, LocalMangaEntryItemViewModel item)
        {
            var insertIndex = entries.FindIndex(existing => string.Compare(existing.Title, item.Title, StringComparison.OrdinalIgnoreCase) > 0);
            if (insertIndex >= 0)
            {
                entries.Insert(insertIndex, item);
            }
            else
            {
                entries.Add(item);
            }
        }

        private void UpdateIncrementalStatus(bool isActive, string? message)
        {
            IsIncrementalRefresh = isActive;
            IncrementalStatusMessage = message;
        }

        private static string CreateIncrementalStatusMessage(string path)
        {
            var name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name))
            {
                name = path;
            }

            return $"Refreshing {name}...";
        }



        private void NavigateUpOneLevel()
        {
            if (!CanNavigateUp)
            {
                return;
            }

            try
            {
                var parent = Directory.GetParent(CurrentFolderPath);
                if (parent == null || !IsWithinLibrary(parent.FullName))
                {
                    return;
                }

                CurrentFolderPath = parent.FullName;
                SelectedFolder = null;
                LoadFolderEntries();
                LoadChapterFileEntries();
                ApplyFilters(resetCursor: true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to navigate up from '{CurrentFolderPath}': {ex.Message}");
            }
        }

        private void NavigateToFolder(LocalLibraryFolderEntryViewModel? folder)
        {
            if (folder == null)
            {
                return;
            }

            NavigateToPath(folder.FullPath);
        }

        private bool IsWithinLibrary(string path)
        {
            if (string.IsNullOrWhiteSpace(LibraryPath))
            {
                return false;
            }

            try
            {
                var normalizedLibrary = NormalizePath(LibraryPath);
                var normalizedPath = NormalizePath(path);
                return normalizedPath.StartsWith(normalizedLibrary, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private bool PathsEqual(string first, string second)
        {
            try
            {
                return string.Equals(NormalizePath(first), NormalizePath(second), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string NormalizePath(string path)
        {
            return Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        private void LoadFolderEntries()
        {
            _folderEntries.Clear();

            if (!HasLibraryPath || string.IsNullOrWhiteSpace(CurrentFolderPath) || !Directory.Exists(CurrentFolderPath))
            {
                return;
            }

            foreach (var directoryPath in _libraryService.GetChildDirectories(CurrentFolderPath))
            {
                _folderEntries.Add(new LocalLibraryFolderEntryViewModel(directoryPath));
            }
        }

        private void LoadChapterFileEntries()
        {
            _chapterFileEntries.Clear();

            if (!HasLibraryPath || string.IsNullOrWhiteSpace(CurrentFolderPath) || !Directory.Exists(CurrentFolderPath))
            {
                return;
            }

            foreach (var filePath in _libraryService.GetChildArchiveFiles(CurrentFolderPath))
            {
                _chapterFileEntries.Add(new LocalLibraryChapterEntryViewModel(filePath));
            }
        }

        private void OnPreviewSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RebuildPreviewEntries();
        }

        private void RebuildPreviewEntries()
        {
            _previewEntries.Clear();

            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in _visibleEntries)
            {
                if (seenPaths.Add(entry.Path))
                {
                    _previewEntries.Add(entry);
                }
            }

            foreach (var folder in _folderEntries)
            {
                if (seenPaths.Add(folder.FullPath))
                {
                    _previewEntries.Add(folder);
                }
            }

            foreach (var chapterFile in _chapterFileEntries)
            {
                if (seenPaths.Add(chapterFile.FullPath))
                {
                    _previewEntries.Add(chapterFile);
                }
            }
        }

        private void EnsureCurrentFolderValid()
        {
            if (!HasLibraryPath || string.IsNullOrWhiteSpace(LibraryPath))
            {
                CurrentFolderPath = string.Empty;
                _folderEntries.Clear();
                return;
            }

            if (string.IsNullOrWhiteSpace(CurrentFolderPath) || !IsWithinLibrary(CurrentFolderPath) || !Directory.Exists(CurrentFolderPath))
            {
                CurrentFolderPath = LibraryPath;
            }
        }

        private bool IsEntryInCurrentFolder(LocalMangaEntryItemViewModel entry)
        {
            if (string.IsNullOrWhiteSpace(CurrentFolderPath))
            {
                return true;
            }

            try
            {
                var normalizedEntryPath = NormalizePath(entry.Path);
                var parent = Path.GetDirectoryName(normalizedEntryPath);
                return !string.IsNullOrEmpty(parent) && PathsEqual(parent, CurrentFolderPath);
            }
            catch
            {
                return false;
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