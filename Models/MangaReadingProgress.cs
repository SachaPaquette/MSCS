namespace MSCS.Models
{
    public sealed record MangaReadingProgress(
        int ChapterIndex,
        string? ChapterTitle,
        double? LegacyScrollProgress,
        DateTimeOffset LastUpdatedUtc,
        string? MangaUrl = null,
        string? SourceKey = null,
        double? ScrollOffset = null,
        double? ScrollableHeight = null,
        string? AnchorImageUrl = null,
        double? AnchorImageProgress = null)
    {
        public double ScrollProgress
        {
            get
            {
                if (ScrollOffset.HasValue && ScrollableHeight.HasValue && ScrollableHeight.Value > 0)
                {
                    var normalized = ScrollOffset.Value / ScrollableHeight.Value;
                    return Math.Clamp(normalized, 0.0, 1.0);
                }

                return Math.Clamp(LegacyScrollProgress ?? 0.0, 0.0, 1.0);
            }
        }
    }
}