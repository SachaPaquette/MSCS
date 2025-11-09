using MSCS.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MSCS.Services
{
    internal sealed class LibraryManifest
    {
        private readonly string _manifestPath;
        private readonly object _sync = new();
        private readonly Dictionary<string, ManifestEntry> _entries;
        private bool _dirty;

        public LibraryManifest(string manifestPath)
        {
            if (string.IsNullOrWhiteSpace(manifestPath))
            {
                throw new ArgumentException("Manifest path cannot be null or empty.", nameof(manifestPath));
            }

            _manifestPath = manifestPath;
            Directory.CreateDirectory(Path.GetDirectoryName(_manifestPath)!);
            _entries = LoadInternal();
        }

        public bool TryGetEntry(DirectoryInfo directory, out LocalMangaEntry entry)
        {
            if (directory == null)
            {
                throw new ArgumentNullException(nameof(directory));
            }

            lock (_sync)
            {
                if (_entries.TryGetValue(directory.FullName, out var manifestEntry) &&
                    manifestEntry.DirectoryWriteTimeUtcTicks == directory.LastWriteTimeUtc.Ticks &&
                    manifestEntry.ChapterCount.HasValue)
                {
                    var lastModifiedTicks = manifestEntry.EntryLastModifiedUtcTicks ?? directory.LastWriteTimeUtc.Ticks;
                    entry = new LocalMangaEntry(
                        directory.Name,
                        directory.FullName,
                        manifestEntry.ChapterCount.Value,
                        new DateTime(lastModifiedTicks, DateTimeKind.Utc));
                    return true;
                }
            }

            entry = null!;
            return false;
        }

        public void UpdateEntry(DirectoryInfo directory, LocalMangaEntry entry)
        {
            if (directory == null)
            {
                throw new ArgumentNullException(nameof(directory));
            }

            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            lock (_sync)
            {
                _entries[directory.FullName] = new ManifestEntry
                {
                    DirectoryWriteTimeUtcTicks = directory.LastWriteTimeUtc.Ticks,
                    ChapterCount = entry.ChapterCount,
                    EntryLastModifiedUtcTicks = entry.LastModifiedUtc.Ticks
                };
                _dirty = true;
            }
        }

        public void RemoveEntry(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return;
            }

            lock (_sync)
            {
                if (_entries.Remove(directoryPath))
                {
                    _dirty = true;
                }
            }
        }

        public void PruneEntries(string rootPath, ISet<string> activePaths)
        {
            lock (_sync)
            {
                var candidates = _entries.Keys
                    .Where(path => IsUnderRoot(path, rootPath) && (activePaths == null || !activePaths.Contains(path)))
                    .ToList();

                foreach (var path in candidates)
                {
                    if (!Directory.Exists(path) || activePaths == null || !activePaths.Contains(path))
                    {
                        _entries.Remove(path);
                        _dirty = true;
                    }
                }
            }
        }

        public void SaveIfDirty()
        {
            lock (_sync)
            {
                if (!_dirty)
                {
                    return;
                }

                var payload = new ManifestPersistence
                {
                    Entries = _entries
                };

                var json = JsonSerializer.Serialize(payload, SerializerOptions);
                File.WriteAllText(_manifestPath, json);
                _dirty = false;
            }
        }

        public string? FindEntryPathForChange(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return null;
            }

            lock (_sync)
            {
                string? bestMatch = null;
                foreach (var path in _entries.Keys)
                {
                    if (!fullPath.StartsWith(path, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (bestMatch == null || path.Length > bestMatch.Length)
                    {
                        bestMatch = path;
                    }
                }

                return bestMatch;
            }
        }

        private Dictionary<string, ManifestEntry> LoadInternal()
        {
            try
            {
                if (!File.Exists(_manifestPath))
                {
                    return new Dictionary<string, ManifestEntry>(StringComparer.OrdinalIgnoreCase);
                }

                using var stream = File.OpenRead(_manifestPath);
                var payload = JsonSerializer.Deserialize<ManifestPersistence>(stream, SerializerOptions);
                if (payload?.Entries != null)
                {
                    return new Dictionary<string, ManifestEntry>(payload.Entries, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch
            {
                // Ignore manifest loading issues and start from a clean state.
            }

            return new Dictionary<string, ManifestEntry>(StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsUnderRoot(string path, string? rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                return true;
            }

            return path.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase);
        }

        private sealed class ManifestPersistence
        {
            public Dictionary<string, ManifestEntry>? Entries { get; set; }
        }

        private sealed class ManifestEntry
        {
            public long DirectoryWriteTimeUtcTicks { get; set; }
            public int? ChapterCount { get; set; }
            public long? EntryLastModifiedUtcTicks { get; set; }
        }

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = false
        };
    }
}