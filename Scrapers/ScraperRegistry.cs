using MSCS.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSCS.Scrapers
{
    public static class ScraperRegistry
    {
        public static Dictionary<string, IScraper> Sources = new()
    {
        { "mangaread", new MangaReadScraper() },
        { "mangadex", new MangaDexScraper() }
    };
    }

}
