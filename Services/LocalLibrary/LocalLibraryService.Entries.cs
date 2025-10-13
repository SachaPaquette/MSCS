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

                foreach (var directory in SafeEnumerateDirectories(rootInfo))
                {
                    if (ct.IsCancellationRequested)
                    {
                        return FinalizeResults(results);
                    }

                    if (ContainsChapterContent(directory))
                    {
                        results.Add(CreateEntry(directory));
                        continue;
                    }

                    foreach (var subDirectory in SafeEnumerateDirectories(directory))
                    {
                        if (ct.IsCancellationRequested)
                        {
                            return FinalizeResults(results);
                        }

                        if (ContainsChapterContent(subDirectory))
                        {
                            results.Add(CreateEntry(subDirectory));
                        }
                    }
                }

                if (results.Count == 0 && ContainsChapterContent(rootInfo))
                {
                    results.Add(CreateEntry(rootInfo));
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