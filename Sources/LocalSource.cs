using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MSCS.Interfaces;
using MSCS.Models;
using MSCS.Services;

namespace MSCS.Sources
{
    public class LocalSource : IMangaSource
    {
        private readonly LocalLibraryService _libraryService;

        public LocalSource(LocalLibraryService libraryService)
        {
            _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
        }

        public async Task<List<Manga>> SearchMangaAsync(string query, CancellationToken ct = default)
        {
            var entries = await _libraryService.GetMangaEntriesAsync(ct).ConfigureAwait(false);
            var results = new List<Manga>();
            if (entries.Count == 0)
            {
                return results;
            }

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(query) ||
                    entry.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(entry.ToManga());
                }
            }

            return results;
        }

        public async Task<List<Chapter>> GetChaptersAsync(string mangaUrl, CancellationToken ct = default)
        {
            var chapters = await _libraryService.GetChaptersAsync(mangaUrl, ct).ConfigureAwait(false);
            return new List<Chapter>(chapters);
        }

        public async Task<IReadOnlyList<ChapterImage>> FetchChapterImages(string chapterUrl, CancellationToken ct = default)
        {
            var images = await _libraryService.GetChapterImagesAsync(chapterUrl, ct).ConfigureAwait(false);
            return images;
        }

        public Task<string> LoadMoreSeriesHtmlAsync(string query, int page, CancellationToken ct = default)
        {
            return Task.FromResult(string.Empty);
        }

        public List<Manga> ParseMangaFromHtmlFragment(string htmlFragment)
        {
            return new List<Manga>();
        }
    }
}