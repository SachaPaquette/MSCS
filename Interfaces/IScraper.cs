using MSCS.Models;
using MSCS.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSCS.Interfaces
{
    public interface IScraper
    {
        Task<List<Manga>> SearchMangaAsync(string query, CancellationToken ct = default);
        Task<List<Chapter>> GetChaptersAsync(string mangaUrl, CancellationToken ct = default);
        Task<IReadOnlyList<ChapterImage>> FetchChapterImages(string chapterUrl, CancellationToken ct = default);
        Task<string> LoadMoreSeriesHtmlAsync(string query, int page, CancellationToken ct = default);
        List<Manga> ParseMangaFromHtmlFragment(string htmlFragment);
    }
}
