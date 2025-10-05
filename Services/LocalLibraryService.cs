using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using MSCS.Models;

namespace MSCS.Services
{
    public class LocalLibraryService : IDisposable
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
            ".cbz"
        };

        private readonly UserSettings _settings;
        private bool _disposed;

        public LocalLibraryService(UserSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _settings.SettingsChanged += OnSettingsChanged;
        }

        public event EventHandler? LibraryPathChanged;

        public string? LibraryPath => _settings.LocalLibraryPath;

        public void SetLibraryPath(string? path)
        {
            _settings.LocalLibraryPath = path;
        }

        public IReadOnlyList<LocalMangaEntry> GetMangaEntries()
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
                    if (ContainsChapterContent(directory))
                    {
                        results.Add(CreateEntry(directory));
                        continue;
                    }

                    foreach (var subDirectory in SafeEnumerateDirectories(directory))
                    {
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

                return results
                    .OrderBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to enumerate local manga entries: {ex.Message}");
                return Array.Empty<LocalMangaEntry>();
            }
        }

        public IReadOnlyList<Chapter> GetChapters(string mangaPath)
        {
            if (string.IsNullOrWhiteSpace(mangaPath) || !Directory.Exists(mangaPath))
            {
                return Array.Empty<Chapter>();
            }

            try
            {
                var directory = new DirectoryInfo(mangaPath);
                var archives = EnumerateArchiveFiles(directory.FullName)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (archives.Count > 0)
                {
                    return archives
                        .Select((path, index) => new Chapter
                        {
                            Title = Path.GetFileNameWithoutExtension(path) ?? Path.GetFileName(path),
                            Url = path,
                            Number = index + 1
                        })
                        .ToList();
                }

                var subDirectories = directory.GetDirectories()
                    .OrderBy(dir => dir.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (subDirectories.Count > 0)
                {
                    return subDirectories
                        .Select((dir, index) => new Chapter
                        {
                            Title = dir.Name,
                            Url = dir.FullName,
                            Number = index + 1
                        })
                        .ToList();
                }

                var images = EnumerateImageFiles(directory.FullName).ToList();
                if (images.Count > 0)
                {
                    return new List<Chapter>
                    {
                        new()
                        {
                            Title = directory.Name,
                            Url = directory.FullName,
                            Number = 1
                        }
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to enumerate chapters for '{mangaPath}': {ex.Message}");
            }

            return Array.Empty<Chapter>();
        }

        public IReadOnlyList<ChapterImage> GetChapterImages(string chapterPath)
        {
            if (string.IsNullOrWhiteSpace(chapterPath))
            {
                return Array.Empty<ChapterImage>();
            }

            try
            {
                if (File.Exists(chapterPath) && IsArchive(chapterPath))
                {
                    return ExtractArchiveImages(chapterPath);
                }

                if (!Directory.Exists(chapterPath))
                {
                    return Array.Empty<ChapterImage>();
                }

                return EnumerateImageFiles(chapterPath)
                    .Select(path => new ChapterImage { ImageUrl = path })
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to enumerate chapter images for '{chapterPath}': {ex.Message}");
                return Array.Empty<ChapterImage>();
            }
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
        }

        private void OnSettingsChanged(object? sender, EventArgs e)
        {
            LibraryPathChanged?.Invoke(this, EventArgs.Empty);
        }

        private static IEnumerable<string> EnumerateImageFiles(string directory)
        {
            try
            {
                return Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                    .Where(IsImageFile)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to enumerate images in '{directory}': {ex.Message}");
                return Array.Empty<string>();
            }
        }

        private static IEnumerable<string> EnumerateArchiveFiles(string directory)
        {
            try
            {
                return Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                    .Where(IsArchive)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to enumerate archives in '{directory}': {ex.Message}");
                return Array.Empty<string>();
            }
        }

        private static int CountChapters(DirectoryInfo info)
        {
            try
            {
                var archives = EnumerateArchiveFiles(info.FullName).ToList();
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
                        if (EnumerateArchiveFiles(dir.FullName).Any() || EnumerateImageFiles(dir.FullName).Any())
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

                return EnumerateImageFiles(info.FullName).Any() ? 1 : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static bool ContainsChapterContent(DirectoryInfo directory)
        {
            try
            {
                if (EnumerateArchiveFiles(directory.FullName).Any())
                {
                    return true;
                }

                if (EnumerateImageFiles(directory.FullName).Any())
                {
                    return true;
                }

                return directory.GetDirectories()
                    .Any(sub => EnumerateImageFiles(sub.FullName).Any());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to inspect directory '{directory.FullName}': {ex.Message}");
                return false;
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

        private static IReadOnlyList<ChapterImage> ExtractArchiveImages(string archivePath)
        {
            try
            {
                var images = new List<ChapterImage>();
                var extractionRoot = EnsureExtractionDirectory(archivePath);

                using var stream = File.OpenRead(archivePath);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

                foreach (var entry in archive.Entries
                             .Where(e => !string.IsNullOrEmpty(e.Name))
                             .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase))
                {
                    if (!IsImageFile(entry.Name))
                    {
                        continue;
                    }

                    var destinationPath = BuildExtractionPath(extractionRoot, entry.FullName);
                    if (destinationPath == null)
                    {
                        continue;
                    }

                    var destinationDirectory = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(destinationDirectory))
                    {
                        Directory.CreateDirectory(destinationDirectory);
                    }

                    if (!File.Exists(destinationPath))
                    {
                        using var entryStream = entry.Open();
                        using var fileStream = File.Create(destinationPath);
                        entryStream.CopyTo(fileStream);
                    }

                    images.Add(new ChapterImage { ImageUrl = destinationPath });
                }

                return images;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to extract images from '{archivePath}': {ex.Message}");
                return Array.Empty<ChapterImage>();
            }
        }

        private static string EnsureExtractionDirectory(string archivePath)
        {
            var fileInfo = new FileInfo(archivePath);
            var safeName = SanitizeFileName(Path.GetFileNameWithoutExtension(fileInfo.Name));
            var suffix = fileInfo.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
            var tempRoot = Path.Combine(Path.GetTempPath(), "MSCS", "LocalCache");
            Directory.CreateDirectory(tempRoot);
            var extractionRoot = Path.Combine(tempRoot, $"{safeName}_{suffix}");
            Directory.CreateDirectory(extractionRoot);
            return extractionRoot;
        }

        private static string? BuildExtractionPath(string root, string entryPath)
        {
            var segments = entryPath
                .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(SanitizeFileName)
                .Where(segment => !string.IsNullOrEmpty(segment))
                .ToArray();

            if (segments.Length == 0)
            {
                return null;
            }

            var combined = Path.Combine(new[] { root }.Concat(segments).ToArray());

            if (!combined.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return combined;
        }

        private static string SanitizeFileName(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                builder.Append(invalidChars.Contains(ch) ? '_' : ch);
            }

            return builder.ToString();
        }
    }
}