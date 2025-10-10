using System;

namespace MSCS.Models
{
    public sealed record MangaReadingProgress(
        int ChapterIndex,
        string? ChapterTitle,
        double ScrollProgress,
        DateTimeOffset LastUpdatedUtc,
        string? MangaUrl = null,
        string? SourceKey = null,
        double? ScrollOffset = null
        );
}