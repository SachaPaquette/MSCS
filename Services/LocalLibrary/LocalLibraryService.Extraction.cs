using PdfiumViewer;
using SharpCompress.Archives;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;

namespace MSCS.Services
{
    public partial class LocalLibraryService
    {
        private string EnsureExtractionDirectory(string archivePath)
        {
            var fileInfo = new FileInfo(archivePath);
            var safeName = SanitizeFileName(Path.GetFileNameWithoutExtension(fileInfo.Name));
            var suffix = fileInfo.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
            var tempRoot = Path.Combine(Path.GetTempPath(), "MSCS", "LocalCache");
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
                            DeleteDirectory(directory);
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
        private static string NormalizeArchiveEntryKey(string entryKey)
        {
            if (string.IsNullOrEmpty(entryKey))
            {
                return string.Empty;
            }

            var normalized = entryKey.Replace('\\', '/');

            while (normalized.StartsWith("./", StringComparison.Ordinal))
            {
                normalized = normalized[2..];
            }

            normalized = normalized.TrimStart('/');

            if (normalized.Contains("../", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            return normalized;
        }
        private static void DeleteDirectory(DirectoryInfo directory)
        {
            if (!directory.Exists)
            {
                return;
            }

            foreach (var file in directory.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                if (file.IsReadOnly)
                {
                    file.IsReadOnly = false;
                }
            }

            foreach (var subDirectory in directory.EnumerateDirectories("*", SearchOption.AllDirectories))
            {
                if ((subDirectory.Attributes & FileAttributes.ReadOnly) != 0)
                {
                    subDirectory.Attributes &= ~FileAttributes.ReadOnly;
                }
            }

            if ((directory.Attributes & FileAttributes.ReadOnly) != 0)
            {
                directory.Attributes &= ~FileAttributes.ReadOnly;
            }

            directory.Delete(true);
        }
        private static Func<Stream> CreateArchiveEntryStreamFactory(string archivePath, string entryKey)
        {
            var normalizedKey = NormalizeArchiveEntryKey(entryKey);

            return () =>
            {
                var memory = new MemoryStream();

                using var fileStream = File.OpenRead(archivePath);
                using var archive = ArchiveFactory.Open(fileStream);

                var match = archive.Entries
                    .FirstOrDefault(e => !e.IsDirectory && !string.IsNullOrEmpty(e.Key) &&
                                         string.Equals(NormalizeArchiveEntryKey(e.Key!), normalizedKey, StringComparison.OrdinalIgnoreCase));

                if (match == null)
                {
                    throw new FileNotFoundException($"Entry '{normalizedKey}' was not found in '{archivePath}'.", normalizedKey);
                }

                using var entryStream = match.OpenEntryStream();
                entryStream.CopyTo(memory);
                memory.Position = 0;
                return memory;
            };
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