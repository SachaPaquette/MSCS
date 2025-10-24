using System;

namespace MSCS.Models
{
    public sealed record BookmarkEntry(
        string StorageKey,
        string Title,
        string? SourceKey,
        string? MangaUrl,
        string? CoverImageUrl,
        DateTimeOffset AddedUtc);
}