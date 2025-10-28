using System;

namespace MSCS.Models
{
    public readonly record struct BookmarkKey
    {
        public BookmarkKey(string? title, string? sourceKey, string? mangaUrl)
        {
            Title = string.IsNullOrWhiteSpace(title) ? string.Empty : title.Trim();
            SourceKey = string.IsNullOrWhiteSpace(sourceKey) ? null : sourceKey.Trim();
            MangaUrl = string.IsNullOrWhiteSpace(mangaUrl) ? null : mangaUrl.Trim();
        }

        public string Title { get; }

        public string? SourceKey { get; }

        public string? MangaUrl { get; }

        public bool HasStableIdentifier => !string.IsNullOrEmpty(SourceKey) && !string.IsNullOrEmpty(MangaUrl);

        public bool IsEmpty => string.IsNullOrEmpty(Title) && !HasStableIdentifier;

        public override string ToString()
        {
            return $"Title: '{Title}', SourceKey: '{SourceKey}', MangaUrl: '{MangaUrl}'";
        }
    }
}