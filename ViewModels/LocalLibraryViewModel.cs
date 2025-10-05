using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using MSCS.Commands;
using MSCS.Models;
using MSCS.Services;

namespace MSCS.ViewModels
{
    public class LocalLibraryViewModel : BaseViewModel, IDisposable
    {
        private readonly LocalLibraryService _libraryService;
        private bool _disposed;
        private bool _hasLibraryPath;
        private bool _isLoading;
        private string _searchQuery = string.Empty;
        private string _selectedGroup = "All";
        private LocalMangaEntry? _selectedManga;

        private const int MangaDisplayBatchSize = 60;
        private readonly ObservableCollection<LocalMangaEntry> _visibleEntries = new();
        private readonly ReadOnlyObservableCollection<LocalMangaEntry> _readOnlyVisibleEntries;
        private readonly List<LocalMangaEntry> _allEntries = new();
        private readonly List<LocalMangaEntry> _filteredEntries = new();
        private readonly ObservableCollection<string> _groupFilters = new();
        private int _visibleEntryCursor;
        private bool _hasMoreResults;

        public LocalLibraryViewModel(LocalLibraryService libraryService)
        {
            _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
            _libraryService.LibraryPathChanged += OnLibraryPathChanged;
            _readOnlyVisibleEntries = new ReadOnlyObservableCollection<LocalMangaEntry>(_visibleEntries);


            RefreshCommand = new RelayCommand(_ => ReloadLibrary(), _ => !_isLoading);
            OpenLibraryFolderCommand = new RelayCommand(_ => OpenLibraryFolder(), _ => HasLibraryPath);
            LoadMoreMangaCommand = new RelayCommand(_ => LoadMoreVisibleManga(), _ => HasMoreResults);
            InitializeGroups();
            ReloadLibrary();
        }

        public event EventHandler<Manga?>? MangaSelected;

        public ReadOnlyObservableCollection<LocalMangaEntry> MangaEntries => _readOnlyVisibleEntries;

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

        public LocalMangaEntry? SelectedManga
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
        public bool IsLibraryEmpty => HasLibraryPath && !IsLoading && _filteredEntries.Count == 0;

        public void EnsureLibraryLoaded()
        {
            ReloadLibrary();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _libraryService.LibraryPathChanged -= OnLibraryPathChanged;
        }

        private void ReloadLibrary()
        {
            if (_disposed)
            {
                return;
            }

            IsLoading = true;
            try
            {
                HasLibraryPath = _libraryService.LibraryPathExists();
                var entries = _libraryService.GetMangaEntries();

                _allEntries.Clear();
                _allEntries.AddRange(entries);

                UpdateGroups(entries);
                ApplyFilters(resetCursor: true);
                OnPropertyChanged(nameof(LibraryPath));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load local library: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void UpdateGroups(IReadOnlyCollection<LocalMangaEntry> entries)
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
            if (!HasLibraryPath)
            {
                return;
            }

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
            ReloadLibrary();
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

        private bool MatchesFilters(LocalMangaEntry entry)
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
            var requiredCount = Math.Min(_filteredEntries.Count, _visibleEntryCursor);

            while (_visibleEntries.Count > requiredCount)
            {
                _visibleEntries.RemoveAt(_visibleEntries.Count - 1);
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
    }
}