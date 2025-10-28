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

            try
            {
                var results = new List<LocalMangaEntry>();
                var rootInfo = new DirectoryInfo(root);
                var scanCache = new Dictionary<string, DirectoryScanResult>(StringComparer.OrdinalIgnoreCase);

                foreach (var directory in SafeEnumerateDirectories(rootInfo))
                {
                    if (ct.IsCancellationRequested)
                    {
                        return FinalizeResults(results);
                    }

                    if (ContainsChapterContent(directory, scanCache))
                    {
                        results.Add(CreateEntry(directory, scanCache));
                        continue;
                    }

                    var directoryScan = GetDirectoryScan(directory, scanCache);
                    foreach (var subDirectory in directoryScan.Subdirectories)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            return FinalizeResults(results);
                        }

                        if (ContainsChapterContent(subDirectory, scanCache))
                        {
                            results.Add(CreateEntry(subDirectory, scanCache));
                        }
                    }
                }

                if (results.Count == 0 && ContainsChapterContent(rootInfo, scanCache))
                {
                    results.Add(CreateEntry(rootInfo, scanCache));
                }

                return FinalizeResults(results);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to enumerate local manga entries: {ex.Message}");
                return Array.Empty<LocalMangaEntry>();
            }
        }

        private static IReadOnlyList<LocalMangaEntry> FinalizeResults(List<LocalMangaEntry> results)
        {
            if (results.Count == 0)
            {
                return Array.Empty<LocalMangaEntry>();
            }

            return results
                .OrderBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}