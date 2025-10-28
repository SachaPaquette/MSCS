using MSCS.Models;
using SharpCompress.Archives;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
                    .OrderBy(dir => dir.Name, NaturalSortComparer.Instance)
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

                var entryDescriptors = archive.Entries
                    .Where(e => !e.IsDirectory && !string.IsNullOrEmpty(e.Key))
                    .Select(entry =>
                    {
                        var archiveKey = entry.Key!.Replace('\\', '/');
                        var normalized = NormalizeArchiveEntryKey(archiveKey);
                        return new { Entry = entry, ArchiveKey = archiveKey, NormalizedKey = normalized };
                    })
                    .Where(item => !string.IsNullOrEmpty(item.NormalizedKey) &&
                                   IsImageFile(Path.GetFileName(item.NormalizedKey)))
                    .GroupBy(item => item.NormalizedKey!, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group
                        .OrderBy(item => item.ArchiveKey, StringComparer.OrdinalIgnoreCase)
                        .First())
                    .OrderBy(item => item.NormalizedKey, StringComparer.OrdinalIgnoreCase)
                    .Select(item => new ArchiveEntryDescriptor(
                        archivePath,
                        item.NormalizedKey!,
                        item.ArchiveKey,
                        archive.Type,
                        archive.IsSolid || item.Entry.IsSolid || !item.Entry.IsComplete || item.Entry.IsEncrypted))
                    .ToList();

                var images = new List<ChapterImage>(entryDescriptors.Count);

                foreach (var descriptor in entryDescriptors)
                {
                    var factory = CreateArchiveEntryStreamFactory(descriptor);
                    images.Add(new ChapterImage
                    {
                        ImageUrl = $"{archivePath}::{descriptor.NormalizedKey}",
                        StreamFactory = factory.StreamFactory,
                        ReleaseResources = factory.Cleanup
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
    }
}