using MSCS.Models;
using PdfiumViewer;
using SharpCompress.Archives;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;

namespace MSCS.Services
{
    public partial class LocalLibraryService : IDisposable
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

        public Task<IReadOnlyList<LocalMangaEntry>> GetMangaEntriesAsync(CancellationToken ct = default)
        {
            return Task.Run(() => GetMangaEntriesInternal(ct), ct);
        }

        public Task<IReadOnlyList<Chapter>> GetChaptersAsync(string mangaPath, CancellationToken ct = default)
        {
            return Task.Run(() => GetChaptersInternal(mangaPath, ct), ct);
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
    }
}