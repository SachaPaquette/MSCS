using MSCS.Interfaces;
using MSCS.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MSCS.Sources
{
    public class MangaDexSource : IMangaSource
    {
        public Task<List<Manga>> SearchMangaAsync(string query, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<List<Chapter>> GetChaptersAsync(string mangaUrl, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<ChapterImage>> FetchChapterImages(string chapterUrl, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<string> LoadMoreSeriesHtmlAsync(string query, int page, CancellationToken ct = default)
            => throw new NotImplementedException();

        public List<Manga> ParseMangaFromHtmlFragment(string htmlFragment)
            => throw new NotImplementedException();
    }
}