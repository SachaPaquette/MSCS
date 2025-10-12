using System;

namespace MSCS.Models;

public sealed record UpdateCheckResult(Version CurrentVersion, Version LatestVersion, string DownloadUrl);