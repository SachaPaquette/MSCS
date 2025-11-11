using MSCS.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Timer = System.Threading.Timer;

namespace MSCS.Services
{
    public partial class LocalLibraryService : IDisposable
    {
        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".gif",
            ".bmp",
            ".webp"
        };

        private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".cbz",
            ".cbr",
            ".cb7",
            ".zip",
            ".rar",
            ".7z",
        };

        private readonly UserSettings _settings;
        private readonly LibraryManifest _manifest;
        private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<PollingMonitor> _pollingMonitors = new();
        private readonly object _watcherLock = new();
        private bool _disposed;

        public LocalLibraryService(UserSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _settings.SettingsChanged += OnSettingsChanged;

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var manifestPath = Path.Combine(appData, "MSCS", "localLibraryManifest.json");
            _manifest = new LibraryManifest(manifestPath);

            RefreshLibraryWatchers();
        }

        public event EventHandler<LibraryChangedEventArgs>? LibraryPathChanged;

        public string? LibraryPath => _settings.LocalLibraryPath;

        public void SetLibraryPath(string? path)
        {
            _settings.LocalLibraryPath = path;
        }

        public Task<IReadOnlyList<LocalMangaEntry>> GetMangaEntriesAsync(CancellationToken ct = default)
        {
            return Task.Run(() => GetMangaEntriesInternal(ct), ct);
        }

        public Task<LocalMangaEntry?> GetMangaEntryAsync(string directoryPath, CancellationToken ct = default)
        {
            return Task.Run(() => GetMangaEntryInternal(directoryPath, ct), ct);
        }

        public Task<IReadOnlyList<Chapter>> GetChaptersAsync(string mangaPath, CancellationToken ct = default)
        {
            return Task.Run(() => GetChaptersInternal(mangaPath, ct), ct);
        }

        public Task<IReadOnlyList<ChapterImage>> GetChapterImagesAsync(string chapterPath, CancellationToken ct = default)
        {
            return Task.Run(() => GetChapterImagesInternal(chapterPath, ct), ct);
        }

        public bool LibraryPathExists()
        {
            var path = LibraryPath;
            return !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);
        }


        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _settings.SettingsChanged -= OnSettingsChanged;

            lock (_watcherLock)
            {
                foreach (var watcher in _watchers.Values)
                {
                    watcher.Dispose();
                }

                _watchers.Clear();

                foreach (var monitor in _pollingMonitors)
                {
                    monitor.Dispose();
                }

                _pollingMonitors.Clear();
            }

            try
            {
                _manifest.SaveIfDirty();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to persist library manifest during dispose: {ex.Message}");
            }
        }

        private void OnSettingsChanged(object? sender, EventArgs e)
        {
            RefreshLibraryWatchers();
            LibraryPathChanged?.Invoke(this, new LibraryChangedEventArgs(LibraryChangeKind.Reset, LibraryPath, entryPath: LibraryPath));
        }


        private void RefreshLibraryWatchers()
        {
            lock (_watcherLock)
            {
                foreach (var watcher in _watchers.Values)
                {
                    watcher.Created -= OnFileSystemCreated;
                    watcher.Changed -= OnFileSystemChanged;
                    watcher.Deleted -= OnFileSystemDeleted;
                    watcher.Renamed -= OnFileSystemRenamed;
                    watcher.Dispose();
                }

                _watchers.Clear();

                foreach (var monitor in _pollingMonitors)
                {
                    monitor.Dispose();
                }

                _pollingMonitors.Clear();

                var root = LibraryPath;
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                {
                    return;
                }

                try
                {
                    var watcher = new FileSystemWatcher(root)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.LastWrite,
                        EnableRaisingEvents = true
                    };

                    watcher.Created += OnFileSystemCreated;
                    watcher.Changed += OnFileSystemChanged;
                    watcher.Deleted += OnFileSystemDeleted;
                    watcher.Renamed += OnFileSystemRenamed;

                    _watchers[root] = watcher;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to attach file system watcher for '{root}': {ex.Message}");
                    _pollingMonitors.Add(new PollingMonitor(root, OnPollingDetectedChange));
                }
            }
        }

        private void OnPollingDetectedChange(string path)
        {
            var entryPath = ResolveEntryPath(path);
            LibraryPathChanged?.Invoke(this, new LibraryChangedEventArgs(LibraryChangeKind.DirectoryChanged, path, entryPath: entryPath));
        }

        private void OnFileSystemCreated(object sender, FileSystemEventArgs e)
        {
            var kind = Directory.Exists(e.FullPath) ? LibraryChangeKind.DirectoryAdded : LibraryChangeKind.FileChanged;
            var entryPath = ResolveEntryPath(e.FullPath);
            LibraryPathChanged?.Invoke(this, new LibraryChangedEventArgs(kind, e.FullPath, entryPath: entryPath));
        }

        private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
        {
            var entryPath = ResolveEntryPath(e.FullPath);
            LibraryPathChanged?.Invoke(this, new LibraryChangedEventArgs(LibraryChangeKind.FileChanged, e.FullPath, entryPath: entryPath));
        }

        private void OnFileSystemDeleted(object sender, FileSystemEventArgs e)
        {
            var entryPath = ResolveEntryPath(e.FullPath);
            LibraryPathChanged?.Invoke(this, new LibraryChangedEventArgs(LibraryChangeKind.DirectoryRemoved, e.FullPath, entryPath: entryPath));
        }

        private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
        {
            var entryPath = ResolveEntryPath(e.FullPath);
            var oldEntryPath = ResolveEntryPath(e.OldFullPath);
            LibraryPathChanged?.Invoke(this, new LibraryChangedEventArgs(LibraryChangeKind.Renamed, e.FullPath, e.OldFullPath, entryPath, oldEntryPath));
        }

        private sealed class PollingMonitor : IDisposable
        {
            private readonly string _path;
            private readonly Action<string> _callback;
            private readonly Timer _timer;
            private DateTime _lastWriteUtc;

            public PollingMonitor(string path, Action<string> callback)
            {
                _path = path;
                _callback = callback;
                _lastWriteUtc = Directory.Exists(path) ? Directory.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                _timer = new Timer(OnTick, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
            }

            private void OnTick(object? state)
            {
                try
                {
                    if (!Directory.Exists(_path))
                    {
                        if (_lastWriteUtc != DateTime.MinValue)
                        {
                            _lastWriteUtc = DateTime.MinValue;
                            _callback(_path);
                        }

                        return;
                    }

                    var current = Directory.GetLastWriteTimeUtc(_path);
                    if (current != _lastWriteUtc)
                    {
                        _lastWriteUtc = current;
                        _callback(_path);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Polling monitor failed for '{_path}': {ex.Message}");
                }
            }

            public void Dispose()
            {
                _timer.Dispose();
            }
        }

        public string? ResolveEntryPath(string? changedPath)
        {
            if (string.IsNullOrWhiteSpace(changedPath))
            {
                return null;
            }

            var root = LibraryPath;
            if (string.IsNullOrWhiteSpace(root))
            {
                return null;
            }

            try
            {
                var normalizedRoot = Path.GetFullPath(root);
                var normalizedChanged = Path.GetFullPath(changedPath);

                if (!normalizedChanged.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                var manifestMatch = _manifest.FindEntryPathForChange(normalizedChanged);
                if (!string.IsNullOrEmpty(manifestMatch))
                {
                    return manifestMatch;
                }

                if (string.Equals(normalizedChanged, normalizedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return normalizedRoot;
                }

                var relative = normalizedChanged.Substring(normalizedRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.IsNullOrEmpty(relative))
                {
                    return normalizedRoot;
                }

                var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
                var segments = relative.Split(separators, StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length == 0)
                {
                    return normalizedRoot;
                }

                return Path.Combine(normalizedRoot, segments[0]);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to resolve entry path for '{changedPath}': {ex.Message}");
                return null;
            }
        }
    }
}