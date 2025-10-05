using System;

namespace MSCS.Models
{
    public class LocalMangaEntry
    {
        public LocalMangaEntry(string title, string path, int chapterCount, DateTime lastModifiedUtc)
        {
            Title = title;
            Path = path;
            ChapterCount = chapterCount;
            LastModifiedUtc = lastModifiedUtc;
            GroupKey = DetermineGroupKey(title);
        }

        public string Title { get; }
        public string Path { get; }
        public int ChapterCount { get; }
        public DateTime LastModifiedUtc { get; }
        public string GroupKey { get; }

        public Manga ToManga()
        {
            return new Manga
            {
                Title = Title,
                Url = Path,
                CoverImageUrl = string.Empty,
                LastUpdated = LastModifiedUtc,
                TotalChapters = ChapterCount > 0 ? ChapterCount : null,
                Description = "Local library entry"
            };
        }

        private static string DetermineGroupKey(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return "#";
            }

            var first = char.ToUpperInvariant(title.TrimStart()[0]);
            return first is >= 'A' and <= 'Z' ? first.ToString() : "#";
        }
    }
}