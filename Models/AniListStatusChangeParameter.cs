using MSCS.Enums;

namespace MSCS.Models
{
    public sealed record AniListStatusChangeParameter(AniListMedia Media, AniListMediaListStatus Status);
}