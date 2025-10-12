using System;

namespace MSCS.Models;

public sealed record UpdateCheckResult(
    Version CurrentVersion,
    Version LatestVersion,
    string DownloadUrl,
    bool HasNewerBuild = false,
    DateTimeOffset? LatestPublishedAt = null,
    DateTimeOffset? CurrentBuildDate = null,
    long? ReleaseId = null);