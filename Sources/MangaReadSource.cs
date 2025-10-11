using System;

namespace MSCS.Sources
{
    public sealed class MangaReadSource : MadaraSourceBase
    {
        private static readonly MadaraSourceSettings Settings = new(new Uri("https://www.mangaread.org/"));

        public MangaReadSource() : base(Settings)
        {
        }
    }
}