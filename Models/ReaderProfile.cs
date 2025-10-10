using MSCS.Enums;
using MSCS.Helpers;

namespace MSCS.Models
{
    public sealed record ReaderProfile
    {
        public ReaderTheme Theme { get; init; } = ReaderTheme.Midnight;
        public double WidthFactor { get; init; } = Constants.DefaultWidthFactor;
        public double MaxPageWidth { get; init; } = Constants.DefaultMaxPageWidth;
        public double ScrollPageFraction { get; init; } = Constants.DefaultSmoothScrollPageFraction;
        public int ScrollDurationMs { get; init; } = Constants.DefaultSmoothScrollDuration;

        public static ReaderProfile CreateDefault() => new();
    }
}