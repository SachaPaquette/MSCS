using System;

namespace MSCS.Sources
{
    public sealed class MangaReadSource : MadaraSourceBase
    {
        private static readonly MadaraSourceSettings Settings = new(new Uri("https://www.mangaread.org/"))
        {
            ReaderImageXPath = "//div[contains(@class,'reading-content')]//img[contains(@class,'wp-manga-chapter-img')]"
        };
        public MangaReadSource() : base(Settings)
        {
        }
    }
}