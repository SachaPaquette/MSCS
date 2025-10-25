using MSCS.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace MSCS.Services
{
    public partial class LocalLibraryService
    {
        private static List<string> EnumerateImageFiles(string directory) => EnumerateFilteredFiles(directory, IsImageFile, "images");

        private static bool ContainsChapterContent(DirectoryInfo directory, IDictionary<string, DirectoryScanResult> cache)
        {
            try
            {
                var scan = GetDirectoryScan(directory, cache);
                if (scan.HasChapterFiles)
                {
                    return true;
                }

                foreach (var subDirectory in scan.Subdirectories)
                {
                    var subScan = GetDirectoryScan(subDirectory, cache);
                    if (subScan.ImageFiles.Count > 0)
                    {
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to inspect directory '{directory.FullName}': {ex.Message}");
                return false;
            }
        }

        private static List<string> EnumerateArchiveFiles(string directory) => EnumerateFilteredFiles(directory, IsArchive, "archives");

        private static List<string> EnumerateFilteredFiles(string directory, Func<string, bool> predicate, string description)
        {
            try
            {
                return Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                    .Where(predicate)
                    .OrderBy(path => path, NaturalSortComparer.Instance)
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to enumerate {description} in '{directory}': {ex.Message}");
                return new List<string>();
            }
        }

        private static int CountChapters(DirectoryInfo info, IDictionary<string, DirectoryScanResult> cache)
        {
            try
            {
                var scan = GetDirectoryScan(info, cache);
                if (scan.ArchiveFiles.Count > 0)
                {
                    return scan.ArchiveFiles.Count;
                }

                if (scan.Subdirectories.Length > 0)
                {
                    var chapterDirectories = 0;
                    foreach (var dir in scan.Subdirectories)
                    {
                        var subScan = GetDirectoryScan(dir, cache);
                        if (subScan.ArchiveFiles.Count > 0 || subScan.ImageFiles.Count > 0)
                        {
                            chapterDirectories++;
                        }
                    }

                    if (chapterDirectories > 0)
                    {
                        return chapterDirectories;
                    }

                    return scan.Subdirectories.Length;
                }

                return scan.ImageFiles.Count > 0 ? 1 : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static LocalMangaEntry CreateEntry(DirectoryInfo info, IDictionary<string, DirectoryScanResult> cache)
        {
            var chapters = CountChapters(info, cache);
            return new LocalMangaEntry(info.Name, info.FullName, chapters, info.LastWriteTimeUtc);
        }

        private static IEnumerable<DirectoryInfo> SafeEnumerateDirectories(DirectoryInfo root)
        {
            try
            {
                return root.EnumerateDirectories();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to enumerate directories in '{root.FullName}': {ex.Message}");
                return Array.Empty<DirectoryInfo>();
            }
        }
        private static bool IsImageFile(string path)
        {
            var extension = Path.GetExtension(path);
            return !string.IsNullOrWhiteSpace(extension) && ImageExtensions.Contains(extension);
        }
        private static bool IsArchive(string path)
        {
            var extension = Path.GetExtension(path);
            return !string.IsNullOrWhiteSpace(extension) && ArchiveExtensions.Contains(extension);
        }

        private static DirectoryScanResult GetDirectoryScan(DirectoryInfo directory, IDictionary<string, DirectoryScanResult> cache)
        {
            if (cache.TryGetValue(directory.FullName, out var cached))
            {
                return cached;
            }

            var scan = DirectoryScanResult.Create(directory);
            cache[directory.FullName] = scan;
            return scan;
        }

        private sealed class DirectoryScanResult
        {
            private DirectoryScanResult(IReadOnlyList<string> archiveFiles, IReadOnlyList<string> imageFiles, DirectoryInfo[] subdirectories)
            {
                ArchiveFiles = archiveFiles;
                ImageFiles = imageFiles;
                Subdirectories = subdirectories;
            }

            public static DirectoryScanResult Empty { get; } = new(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<DirectoryInfo>());

            public IReadOnlyList<string> ArchiveFiles { get; }

            public IReadOnlyList<string> ImageFiles { get; }

            public DirectoryInfo[] Subdirectories { get; }

            public bool HasChapterFiles => ArchiveFiles.Count > 0 || ImageFiles.Count > 0;

            public static DirectoryScanResult Create(DirectoryInfo directory)
            {
                try
                {
                    var archiveFiles = new List<string>();
                    var imageFiles = new List<string>();
                    var subdirectories = new List<DirectoryInfo>();

                    foreach (var entry in directory.EnumerateFileSystemInfos())
                    {
                        if (entry is DirectoryInfo subDirectory)
                        {
                            subdirectories.Add(subDirectory);
                            continue;
                        }

                        if (entry is not FileInfo file)
                        {
                            continue;
                        }

                        var path = file.FullName;
                        if (IsArchive(path))
                        {
                            archiveFiles.Add(path);
                        }
                        else if (IsImageFile(path))
                        {
                            imageFiles.Add(path);
                        }
                    }

                    archiveFiles.Sort(NaturalSortComparer.Instance);
                    imageFiles.Sort(NaturalSortComparer.Instance);

                    subdirectories.Sort((left, right) => NaturalSortComparer.Instance.Compare(left.Name, right.Name));

                    return new DirectoryScanResult(archiveFiles, imageFiles, subdirectories.ToArray());
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to scan directory '{directory.FullName}': {ex.Message}");
                    return Empty;
                }
            }
        }
    }
}