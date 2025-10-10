using System;
using System.Globalization;
using MSCS.Enums;

namespace MSCS.Helpers;

public static class AniListFormatting
{
    public static string ToApiValue(this AniListMediaListStatus status) => status switch
    {
        AniListMediaListStatus.Current => "CURRENT",
        AniListMediaListStatus.Planning => "PLANNING",
        AniListMediaListStatus.Completed => "COMPLETED",
        AniListMediaListStatus.Paused => "PAUSED",
        AniListMediaListStatus.Dropped => "DROPPED",
        AniListMediaListStatus.Repeating => "REPEATING",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    public static string ToDisplayString(this AniListMediaListStatus status) => status switch
    {
        AniListMediaListStatus.Current => "Currently reading",
        AniListMediaListStatus.Planning => "Planning",
        AniListMediaListStatus.Completed => "Completed",
        AniListMediaListStatus.Paused => "Paused",
        AniListMediaListStatus.Dropped => "Dropped",
        AniListMediaListStatus.Repeating => "Re-reading",
        _ => status.ToString()
    };

    public static AniListMediaListStatus? FromApiValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.ToUpperInvariant() switch
        {
            "CURRENT" => AniListMediaListStatus.Current,
            "PLANNING" => AniListMediaListStatus.Planning,
            "COMPLETED" => AniListMediaListStatus.Completed,
            "PAUSED" => AniListMediaListStatus.Paused,
            "DROPPED" => AniListMediaListStatus.Dropped,
            "REPEATING" => AniListMediaListStatus.Repeating,
            _ => null
        };
    }

    public static string? ToDisplayTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var textInfo = CultureInfo.CurrentCulture.TextInfo;
        return textInfo.ToTitleCase(value.Replace('_', ' ').ToLowerInvariant());
    }
}