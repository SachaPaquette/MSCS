using MSCS.Models;
using PdfiumViewer;
using SharpCompress.Archives;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace MSCS.Services
{
    public partial class LocalLibraryService
    {
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
                var archives = EnumerateArchiveFiles(directory.FullName);

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

                var images = EnumerateImageFiles(directory.FullName);
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
                using var stream = File.OpenRead(archivePath);
                using var archive = ArchiveFactory.Open(stream);

                var entryKeys = archive.Entries
                    .Where(e => !e.IsDirectory && !string.IsNullOrEmpty(e.Key))
                    .Select(e => NormalizeArchiveEntryKey(e.Key!))
                    .Where(key => !string.IsNullOrEmpty(key) && IsImageFile(Path.GetFileName(key)))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var images = new List<ChapterImage>(entryKeys.Count);

                foreach (var key in entryKeys)
                {
                    images.Add(new ChapterImage
                    {
                        ImageUrl = $"{archivePath}::{key}",
                        StreamFactory = CreateArchiveEntryStreamFactory(archivePath, key)
                    });
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
    }
}