using MSCS.Interfaces;
using MSCS.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSCS.Scrapers
{
    public class MangaDexScraper : IScraper
    {
        public async Task<List<Manga>> SearchMangaAsync(string query)
        {
            // Implement search logic for MangaDex
            throw new NotImplementedException();
        }
        public async Task<List<Chapter>> GetChaptersAsync(string mangaUrl)
        {
            // Implement chapter retrieval logic for MangaDex
            throw new NotImplementedException();
        }
        public List<Manga> ParseMangaFromHtmlFragment(string htmlFragment)
        {
            // Implement HTML parsing logic for MangaDex
            throw new NotImplementedException();
        }
        public async Task<string> LoadMoreSeriesHtmlAsync(string query, int page)

        {
            // Implement logic to load more series HTML for MangaDex
            throw new NotImplementedException();
        }
        public async Task<ObservableCollection<ChapterImage>> FetchChapterImages(string chapterUrl)
        {
            // Implement logic to fetch chapter images for MangaDex
            throw new NotImplementedException();
        }
        public async Task<List<Chapter>> GetChaptersAsync(string mangaUrl, CancellationToken ct = default)
        {
            // Implement chapter retrieval logic for MangaDex with cancellation support
            throw new NotImplementedException();
        }

        public async Task<List<Manga>> SearchMangaAsync(string query, CancellationToken ct = default)
        {
            // Implement search logic for MangaDex with cancellation support
            throw new NotImplementedException();
        }
        public async Task<IReadOnlyList<ChapterImage>> FetchChapterImages(string chapterUrl, CancellationToken ct = default)
        {
            // Implement logic to fetch chapter images for MangaDex with cancellation support
            throw new NotImplementedException();
        }
        public async Task<string> LoadMoreSeriesHtmlAsync(string query, int page, CancellationToken ct = default)
        {
            // Implement logic to load more series HTML for MangaDex with cancellation support
            throw new NotImplementedException();
        }
        public List<Manga> ParseMangaFromHtmlFragment(string htmlFragment, CancellationToken ct = default)
        {
            // Implement HTML parsing logic for MangaDex with cancellation support
            throw new NotImplementedException();
        }

    }
}
