using MSCS.Interfaces;
using System.Collections.Generic;
using System.Windows.Media;

namespace MSCS.Sources
{
    public static class SourceRegistry
    {
        private static readonly Dictionary<string, IMangaSource> Sources = new();

        static SourceRegistry()
        {
            Register("mangaread", new MangaReadSource());
            Register("mangadex", new MangaDexSource());
        }

        public static void Register(string key, IMangaSource source)
        {
            Sources[key] = source;
        }

        public static IMangaSource? Resolve(string key)
        {
            return Sources.TryGetValue(key, out var source) ? source : null;
        }
    }
}