using MSCS.Models;
using PdfiumViewer;
using SharpCompress.Archives;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
            ".cbz",
            ".cbr",
            ".cb7",
            ".pdf"
        };


        private readonly UserSettings _settings;
        private bool _disposed;
        private static readonly object ExtractionCleanupLock = new();
        private static DateTime _lastCleanup = DateTime.MinValue;
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan ExtractionLifetime = TimeSpan.FromHours(6);
        private const int MaxExtractionDirectories = 32;
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

        [Obsolete("Use GetMangaEntriesAsync to avoid blocking the UI thread.")]
        public IReadOnlyList<LocalMangaEntry> GetMangaEntries()
        {
            return GetMangaEntriesAsync().GetAwaiter().GetResult();
        }

        public Task<IReadOnlyList<LocalMangaEntry>> GetMangaEntriesAsync(CancellationToken ct = default)
        {
            return Task.Run(() => GetMangaEntriesInternal(ct), ct);
        }

        [Obsolete("Use GetChaptersAsync to avoid blocking the UI thread.")]
        public IReadOnlyList<Chapter> GetChapters(string mangaPath)
        {
            return GetChaptersAsync(mangaPath).GetAwaiter().GetResult();
        }

        public Task<IReadOnlyList<Chapter>> GetChaptersAsync(string mangaPath, CancellationToken ct = default)
        {
            return Task.Run(() => GetChaptersInternal(mangaPath, ct), ct);
        }

        [Obsolete("Use GetChapterImagesAsync to avoid blocking the UI thread.")]
        public IReadOnlyList<ChapterImage> GetChapterImages(string chapterPath)
        {
            return GetChapterImagesAsync(chapterPath).GetAwaiter().GetResult();
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

        private IReadOnlyList<Chapter> GetChaptersInternal(string mangaPath, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(mangaPath) || !Directory.Exists(mangaPath))
            {
                return Array.Empty<Chapter>();
            }

            try
            {
                ct.ThrowIfCancellationRequested();
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
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to enumerate chapters for '{mangaPath}': {ex.Message}");
            }

            return Array.Empty<Chapter>();
        }

        private IReadOnlyList<ChapterImage> GetChapterImagesInternal(string chapterPath, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(chapterPath))
            {
                return Array.Empty<ChapterImage>();
            }

            try
            {
                ct.ThrowIfCancellationRequested();

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
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to enumerate chapter images for '{chapterPath}': {ex.Message}");
                return Array.Empty<ChapterImage>();
            }
        }

        private IReadOnlyList<ChapterImage> ExtractArchiveImages(string archivePath)
        {
            try
            {
                var extension = Path.GetExtension(archivePath);
                if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    return ExtractPdfImages(archivePath);
                }

                return ExtractCompressedArchiveImages(archivePath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to extract images from '{archivePath}': {ex.Message}");
                return Array.Empty<ChapterImage>();
            }
        }


        private IReadOnlyList<ChapterImage> ExtractCompressedArchiveImages(string archivePath)
        {
            try
            {
                var images = new List<ChapterImage>();
                var extractionRoot = EnsureExtractionDirectory(archivePath);

                using var stream = File.OpenRead(archivePath);
                using var archive = ArchiveFactory.Open(stream);

                foreach (var entry in archive.Entries
                             .Where(e => !e.IsDirectory && !string.IsNullOrEmpty(e.Key))
                             .OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
                {
                    if (!IsImageFile(entry.Key!))
                    {
                        continue;
                    }

                    var destinationPath = BuildExtractionPath(extractionRoot, entry.Key!);
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
                        using var entryStream = entry.OpenEntryStream();
                        using var fileStream = File.Create(destinationPath);
                        entryStream.CopyTo(fileStream);
                    }

                    images.Add(new ChapterImage { ImageUrl = destinationPath });
                }

                return images;
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine($"Unsupported archive format for '{archivePath}': {ex.Message}");
                return Array.Empty<ChapterImage>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to extract compressed archive '{archivePath}': {ex.Message}");
                return Array.Empty<ChapterImage>();
            }
        }

        private IReadOnlyList<ChapterImage> ExtractPdfImages(string pdfPath)
        {
            try
            {
                var images = new List<ChapterImage>();
                var extractionRoot = EnsureExtractionDirectory(pdfPath);

                using var document = PdfDocument.Load(pdfPath);
                for (var pageIndex = 0; pageIndex < document.PageCount; pageIndex++)
                {
                    var fileName = $"page_{pageIndex + 1:D4}.png";
                    var destinationPath = Path.Combine(extractionRoot, fileName);

                    if (!File.Exists(destinationPath))
                    {
                        using var bitmap = document.Render(pageIndex, 300, 300, true);
                        Directory.CreateDirectory(extractionRoot);
                        bitmap.Save(destinationPath, ImageFormat.Png);
                    }

                    images.Add(new ChapterImage { ImageUrl = destinationPath });
                }

                return images;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to extract images from PDF '{pdfPath}': {ex.Message}");
                return Array.Empty<ChapterImage>();
            }
        }

        private string EnsureExtractionDirectory(string archivePath)
        {
            var fileInfo = new FileInfo(archivePath);
            var safeName = SanitizeFileName(Path.GetFileNameWithoutExtension(fileInfo.Name));
            var suffix = fileInfo.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
            var tempRoot = Path.Combine(Path.GetTempPath(), "MSCS", "LocalCache");
            Directory.CreateDirectory(tempRoot);
            var extractionRoot = Path.Combine(tempRoot, $"{safeName}_{suffix}");
            Directory.CreateDirectory(extractionRoot);
            ScheduleExtractionCleanup(tempRoot);
            return extractionRoot;
        }

        private static void ScheduleExtractionCleanup(string cacheRoot)
        {
            lock (ExtractionCleanupLock)
            {
                if (DateTime.UtcNow - _lastCleanup < CleanupInterval)
                {
                    return;
                }

                _lastCleanup = DateTime.UtcNow;
            }

            Task.Run(() => CleanupExtractionCache(cacheRoot));
        }

        private static void CleanupExtractionCache(string cacheRoot)
        {
            try
            {
                if (!Directory.Exists(cacheRoot))
                {
                    return;
                }

                var expiration = DateTime.UtcNow - ExtractionLifetime;
                var directories = Directory.EnumerateDirectories(cacheRoot)
                    .Select(path => new DirectoryInfo(path))
                    .OrderByDescending(dir => dir.LastWriteTimeUtc)
                    .ToList();

                var retained = 0;
                foreach (var directory in directories)
                {
                    var shouldDelete = directory.LastWriteTimeUtc < expiration || retained >= MaxExtractionDirectories;

                    if (shouldDelete)
                    {
                        try
                        {
                            directory.Delete(true);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to clean cache directory '{directory.FullName}': {ex.Message}");
                        }
                    }
                    else
                    {
                        retained++;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to clean extraction cache '{cacheRoot}': {ex.Message}");
            }
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