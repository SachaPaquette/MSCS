using MSCS.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Threading;

namespace MSCS.Services
{
    public partial class LocalLibraryService
    {

        private IReadOnlyList<LocalMangaEntry> GetMangaEntriesInternal(CancellationToken ct)
        {
            var root = LibraryPath;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                return Array.Empty<LocalMangaEntry>();
            }

            var visitedManifestPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var results = new List<LocalMangaEntry>();
                var rootInfo = new DirectoryInfo(root);
                var scanCache = new Dictionary<string, DirectoryScanResult>(StringComparer.OrdinalIgnoreCase);

                foreach (var directory in SafeEnumerateDirectories(rootInfo))
                {
                    if (ct.IsCancellationRequested)
                    {
                        PersistManifestSafely();
                        return SortResults(results);
                    }

                    if (TryGetCachedEntry(directory, visitedManifestPaths, out var cachedEntry))
                    {
                        results.Add(cachedEntry);
                        continue;
                    }

                    if (ContainsChapterContent(directory, scanCache))
                    {
                        var entry = CreateEntry(directory, scanCache);
                        results.Add(entry);
                        _manifest.UpdateEntry(directory, entry);
                        visitedManifestPaths.Add(directory.FullName);
                        continue;
                    }

                    _manifest.RemoveEntry(directory.FullName);

                    var directoryScan = GetDirectoryScan(directory, scanCache);
                    foreach (var subDirectory in directoryScan.Subdirectories)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            PersistManifestSafely();
                            return SortResults(results);
                        }

                        if (TryGetCachedEntry(subDirectory, visitedManifestPaths, out var cachedSubEntry))
                        {
                            results.Add(cachedSubEntry);
                            continue;
                        }

                        if (ContainsChapterContent(subDirectory, scanCache))
                        {
                            var entry = CreateEntry(subDirectory, scanCache);
                            results.Add(entry);
                            _manifest.UpdateEntry(subDirectory, entry);
                            visitedManifestPaths.Add(subDirectory.FullName);
                        }
                        else
                        {
                            _manifest.RemoveEntry(subDirectory.FullName);
                        }
                    }
                }

                if (results.Count == 0)
                {
                    if (TryGetCachedEntry(rootInfo, visitedManifestPaths, out var cachedRoot))
                    {
                        results.Add(cachedRoot);
                    }
                    else if (ContainsChapterContent(rootInfo, scanCache))
                    {
                        var entry = CreateEntry(rootInfo, scanCache);
                        results.Add(entry);
                        _manifest.UpdateEntry(rootInfo, entry);
                        visitedManifestPaths.Add(rootInfo.FullName);
                    }
                    else
                    {
                        _manifest.RemoveEntry(rootInfo.FullName);
                    }
                }

                return FinalizeResults(results, rootInfo, visitedManifestPaths);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to enumerate local manga entries: {ex.Message}");
                PersistManifestSafely();
                return Array.Empty<LocalMangaEntry>();
            }
        }


        private bool TryGetCachedEntry(DirectoryInfo directory, ISet<string> visitedPaths, out LocalMangaEntry entry)
        {
            if (_manifest.TryGetEntry(directory, out entry))
            {
                visitedPaths.Add(directory.FullName);
                return true;
            }

            entry = null!;
            return false;
        }

        private IReadOnlyList<LocalMangaEntry> FinalizeResults(List<LocalMangaEntry> results, DirectoryInfo rootInfo, ISet<string> visitedPaths)
        {
            try
            {
                _manifest.PruneEntries(rootInfo.FullName, visitedPaths);
                _manifest.SaveIfDirty();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to update library manifest: {ex.Message}");
            }

            return SortResults(results);
        }

        private void PersistManifestSafely()
        {
            try
            {
                _manifest.SaveIfDirty();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to persist library manifest: {ex.Message}");
            }
        }

        private static IReadOnlyList<LocalMangaEntry> SortResults(List<LocalMangaEntry> results)
        {
            if (results.Count == 0)
            {
                return Array.Empty<LocalMangaEntry>();
            }

            return results
                .OrderBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private LocalMangaEntry? GetMangaEntryInternal(string directoryPath, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return null;
            }

            try
            {
                ct.ThrowIfCancellationRequested();

                var info = new DirectoryInfo(directoryPath);
                if (!info.Exists)
                {
                    _manifest.RemoveEntry(directoryPath);
                    PersistManifestSafely();
                    return null;
                }

                var cache = new Dictionary<string, DirectoryScanResult>(StringComparer.OrdinalIgnoreCase);
                if (!ContainsChapterContent(info, cache))
                {
                    _manifest.RemoveEntry(info.FullName);
                    PersistManifestSafely();
                    return null;
                }

                var entry = CreateEntry(info, cache);
                _manifest.UpdateEntry(info, entry);
                PersistManifestSafely();
                return entry;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to refresh manga entry '{directoryPath}': {ex.Message}");
                return null;
            }
        }
    }
}