using SharpCompress.Archives;
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;

namespace MSCS.Services
{
    public partial class LocalLibraryService
    {
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