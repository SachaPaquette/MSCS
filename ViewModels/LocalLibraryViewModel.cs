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

        private readonly ObservableCollection<LocalMangaEntry> _entries = new();
        private readonly ObservableCollection<string> _groupFilters = new();
        private readonly ICollectionView _collectionView;

        public LocalLibraryViewModel(LocalLibraryService libraryService)
        {
            _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
            _libraryService.LibraryPathChanged += OnLibraryPathChanged;

            _collectionView = CollectionViewSource.GetDefaultView(_entries);
            _collectionView.Filter = FilterManga;
            _collectionView.SortDescriptions.Add(new SortDescription(nameof(LocalMangaEntry.Title), ListSortDirection.Ascending));

            RefreshCommand = new RelayCommand(_ => ReloadLibrary(), _ => !_isLoading);
            OpenLibraryFolderCommand = new RelayCommand(_ => OpenLibraryFolder(), _ => HasLibraryPath);

            InitializeGroups();
            ReloadLibrary();
        }

        public event EventHandler<Manga?>? MangaSelected;

        public ICollectionView MangaEntries => _collectionView;

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
                    _collectionView.Refresh();
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
                    _collectionView.Refresh();
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

        public bool IsLibraryEmpty => HasLibraryPath && _entries.Count == 0 && !IsLoading;

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

        private bool FilterManga(object obj)    
        {
            if (obj is not LocalMangaEntry entry)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(SearchQuery) &&
                entry.Title.IndexOf(SearchQuery, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            if (!string.Equals(SelectedGroup, "All", StringComparison.OrdinalIgnoreCase))
            {
                if (SelectedGroup == "#")
                {
                    if (!string.Equals(entry.GroupKey, "#", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
                else if (!string.Equals(entry.GroupKey, SelectedGroup, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
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
                var entries = _libraryService.GetMangaEntries();
                SelectedManga = null;
                _entries.Clear();

                HasLibraryPath = _libraryService.LibraryPathExists();

                foreach (var entry in entries)
                {
                    _entries.Add(entry);
                }

                UpdateGroups(entries);
                _collectionView.Refresh();
                OnPropertyChanged(nameof(IsLibraryEmpty));
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
    }
}