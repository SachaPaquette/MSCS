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

        public Task<List<Manga>> SearchMangaAsync(string query, CancellationToken ct = default)
        {
            var entries = _libraryService.GetMangaEntries();
            var results = new List<Manga>();
            if (entries.Count == 0)
            {
                return Task.FromResult(results);
            }

            foreach (var entry in entries)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(query) ||
                    entry.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(entry.ToManga());
                }
            }

            return Task.FromResult(results);
        }

        public Task<List<Chapter>> GetChaptersAsync(string mangaUrl, CancellationToken ct = default)
        {
            var chapters = _libraryService.GetChapters(mangaUrl);
            return Task.FromResult(new List<Chapter>(chapters));
        }

        public Task<IReadOnlyList<ChapterImage>> FetchChapterImages(string chapterUrl, CancellationToken ct = default)
        {
            var images = _libraryService.GetChapterImages(chapterUrl);
            return Task.FromResult((IReadOnlyList<ChapterImage>)images);
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