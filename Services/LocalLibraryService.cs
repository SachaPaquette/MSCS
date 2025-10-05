using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using MSCS.Models;

namespace MSCS.Services
{
    public class LocalLibraryService : IDisposable
    {
        private static readonly string[] ImageExtensions =
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".cbz"
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
                var directories = Directory.EnumerateDirectories(root)
                    .Select(path =>
                    {
                        var info = new DirectoryInfo(path);
                        var chapters = CountChapters(info);
                        return new LocalMangaEntry(info.Name, info.FullName, chapters, info.LastWriteTimeUtc);
                    })
                    .OrderBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return directories;
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
            if (string.IsNullOrWhiteSpace(chapterPath) || !Directory.Exists(chapterPath))
            {
                return Array.Empty<ChapterImage>();
            }

            try
            {
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
                    .Where(path => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to enumerate images in '{directory}': {ex.Message}");
                return Array.Empty<string>();
            }
        }

        private static int CountChapters(DirectoryInfo info)
        {
            try
            {
                var subDirs = info.GetDirectories();
                if (subDirs.Length > 0)
                {
                    return subDirs.Length;
                }

                var images = EnumerateImageFiles(info.FullName);
                return images.Any() ? 1 : 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}