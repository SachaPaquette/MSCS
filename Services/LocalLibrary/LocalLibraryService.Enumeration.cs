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
        private List<string> EnumerateImageFiles(string directory) => EnumerateFilteredFiles(directory, IsImageFile, "images");

        private bool ContainsChapterContent(DirectoryInfo directory, IDictionary<string, DirectoryScanResult> cache)
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

        private List<string> EnumerateArchiveFiles(string directory) => EnumerateFilteredFiles(directory, IsArchive, "archives");

        private List<string> EnumerateFilteredFiles(string directory, Func<string, bool> predicate, string description)
        {
            try
            {
                var files = new List<string>();

                foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
                {
                    FileInfo info;
                    try
                    {
                        info = new FileInfo(path);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Debug.WriteLine($"Failed to inspect file '{path}': {ex.Message}");
                        continue;
                    }

                    if (!ShouldInclude(info))
                    {
                        continue;
                    }

                    if (predicate(path))
                    {
                        files.Add(path);
                    }
                }

                files.Sort(NaturalSortComparer.Instance);
                return files;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to enumerate {description} in '{directory}': {ex.Message}");
                return new List<string>();
            }
        }

        private int CountChapters(DirectoryInfo info, IDictionary<string, DirectoryScanResult> cache)
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

        private LocalMangaEntry CreateEntry(DirectoryInfo info, IDictionary<string, DirectoryScanResult> cache)
        {
            var chapters = CountChapters(info, cache);
            return new LocalMangaEntry(info.Name, info.FullName, chapters, info.LastWriteTimeUtc);
        }

        private IEnumerable<DirectoryInfo> SafeEnumerateDirectories(DirectoryInfo root)
        {
            try
            {
                return root.EnumerateDirectories()
                    .Where(ShouldInclude)
                    .ToArray();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to enumerate directories in '{root.FullName}': {ex.Message}");
                return Array.Empty<DirectoryInfo>();
            }
        }


        private bool ShouldInclude(FileSystemInfo entry)
        {
            if (entry == null)
            {
                return false;
            }

            try
            {
                if (!_settings.IncludeHiddenLibraryItems)
                {
                    var attributes = entry.Attributes;
                    if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                    {
                        return false;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"Failed to inspect attributes for '{entry.FullName}': {ex.Message}");
                return false;
            }

            var ignoreList = _settings.LocalLibraryIgnoreList;
            if (ignoreList.Count == 0)
            {
                return true;
            }

            var comparer = StringComparer.OrdinalIgnoreCase;
            var entryName = entry.Name;
            var fullName = NormalizePathForComparison(entry.FullName);
            var relativePath = TryGetRelativePath(LibraryPath, entry.FullName);

            foreach (var ignore in ignoreList)
            {
                if (string.IsNullOrWhiteSpace(ignore))
                {
                    continue;
                }

                var normalizedIgnore = NormalizePathForComparison(ignore);

                if (comparer.Equals(entryName, normalizedIgnore))
                {
                    return false;
                }

                if (comparer.Equals(fullName, normalizedIgnore))
                {
                    return false;
                }

                var requiresHierarchyMatch = Path.IsPathRooted(normalizedIgnore) || normalizedIgnore.IndexOf(Path.DirectorySeparatorChar) >= 0;

                if (requiresHierarchyMatch && !string.IsNullOrEmpty(fullName))
                {
                    var absolutePrefix = normalizedIgnore + Path.DirectorySeparatorChar;
                    if (fullName.StartsWith(absolutePrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }

                if (requiresHierarchyMatch && !string.IsNullOrEmpty(relativePath))
                {
                    if (comparer.Equals(relativePath, normalizedIgnore))
                    {
                        return false;
                    }

                    var relativePrefix = normalizedIgnore + Path.DirectorySeparatorChar;
                    if (relativePath.StartsWith(relativePrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static string NormalizePathForComparison(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var normalized = path.Trim();
            normalized = normalized.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            return Path.TrimEndingDirectorySeparator(normalized);
        }

        private static string? TryGetRelativePath(string? root, string fullPath)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                return null;
            }

            try
            {
                var relative = Path.GetRelativePath(root, fullPath);
                if (string.IsNullOrEmpty(relative) || string.Equals(relative, ".", StringComparison.Ordinal))
                {
                    return string.Empty;
                }

                if (relative.StartsWith("..", StringComparison.Ordinal))
                {
                    return null;
                }

                return NormalizePathForComparison(relative);
            }
            catch
            {
                return null;
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

        private DirectoryScanResult GetDirectoryScan(DirectoryInfo directory, IDictionary<string, DirectoryScanResult> cache)
        {
            if (cache.TryGetValue(directory.FullName, out var cached))
            {
                return cached;
            }

            var scan = DirectoryScanResult.Create(directory, ShouldInclude);
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

            public static DirectoryScanResult Create(DirectoryInfo directory, Func<FileSystemInfo, bool> includePredicate)
            {
                try
                {
                    var archiveFiles = new List<string>();
                    var imageFiles = new List<string>();
                    var subdirectories = new List<DirectoryInfo>();

                    foreach (var entry in directory.EnumerateFileSystemInfos())
                    {
                        var shouldInclude = true;
                        if (includePredicate != null)
                        {
                            try
                            {
                                shouldInclude = includePredicate(entry);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Failed to evaluate entry '{entry.FullName}': {ex.Message}");
                                shouldInclude = false;
                            }
                        }

                        if (!shouldInclude)
                        {
                            continue;
                        }

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