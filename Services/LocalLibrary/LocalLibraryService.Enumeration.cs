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

        private static bool ContainsChapterContent(DirectoryInfo directory)
        {
            try
            {
                if (EnumerateArchiveFiles(directory.FullName).Count > 0)
                {
                    return true;
                }

                if (EnumerateImageFiles(directory.FullName).Count > 0)
                {
                    return true;
                }

                return directory.GetDirectories()
                    .Any(sub => EnumerateImageFiles(sub.FullName).Count > 0);
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
        private static int CountChapters(DirectoryInfo info)
        {
            try
            {
                var archives = EnumerateArchiveFiles(info.FullName);
                if (archives.Count > 0)
                {
                    return archives.Count;
                }

                var subDirs = info.GetDirectories();
                if (subDirs.Length > 0)
                {
                    var chapterDirectories = 0;
                    foreach (var dir in subDirs)
                    {
                        if (EnumerateArchiveFiles(dir.FullName).Count > 0 || EnumerateImageFiles(dir.FullName).Count > 0)
                        {
                            chapterDirectories++;
                        }
                    }

                    if (chapterDirectories > 0)
                    {
                        return chapterDirectories;
                    }

                    return subDirs.Length;
                }

                return EnumerateImageFiles(info.FullName).Count > 0 ? 1 : 0;
            }
            catch
            {
                return 0;
            }
        }
        private static LocalMangaEntry CreateEntry(DirectoryInfo info)
        {
            var chapters = CountChapters(info);
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
    }
}