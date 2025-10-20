using MSCS.Models;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MSCS.Services;

public sealed class UpdateService
{
    private const string DefaultLatestReleaseApiEndpoint = "https://api.github.com/repos/MSCS-Project/MSCS/releases/latest";
    private const string DefaultReleasePageUrl = "https://github.com/MSCS-Project/MSCS/releases/latest";
    private const string LatestReleaseApiEnvironmentVariable = "MSCS_UPDATE_LATEST_API";
    private const string ReleasePageEnvironmentVariable = "MSCS_UPDATE_DOWNLOAD_URL";

    private static readonly HttpClient HttpClient = CreateHttpClient();

    public async Task<UpdateCheckResult?> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var response = await CheckForUpdatesWithOutcomeAsync(cancellationToken).ConfigureAwait(false);
        return response.Outcome == UpdateCheckOutcome.UpdateAvailable ? response.Update : null;
    }

    public async Task<UpdateCheckResponse> CheckForUpdatesWithOutcomeAsync(CancellationToken cancellationToken = default)
    {
        var currentVersion = GetCurrentVersion();
        var currentBuildDate = TryGetCurrentBuildDate(out var buildDate)
            ? buildDate
            : (DateTimeOffset?)null;

        var apiEndpoint = Environment.GetEnvironmentVariable(LatestReleaseApiEnvironmentVariable) ?? DefaultLatestReleaseApiEndpoint;
        var downloadPage = Environment.GetEnvironmentVariable(ReleasePageEnvironmentVariable) ?? DefaultReleasePageUrl;

        if (string.IsNullOrWhiteSpace(apiEndpoint))
        {
            return UpdateCheckResponse.Disabled();
        }

        try
        {
            var (document, fetchStatus) = await FetchLatestReleaseDocumentAsync(apiEndpoint, cancellationToken).ConfigureAwait(false);

            if (fetchStatus != ReleaseFetchStatus.Success || document is null)
            {
                return fetchStatus switch
                {
                    ReleaseFetchStatus.NotFound => UpdateCheckResponse.UpToDate(),
                    _ => UpdateCheckResponse.Failed()
                };
            }

            using var doc = document;
            var root = doc.RootElement;

            var versionText = GetVersionText(root);
            if (!TryParseVersion(versionText, out var latestVersion))
            {
                return UpdateCheckResponse.UpToDate();
            }

            var releasePage = root.TryGetProperty("html_url", out var htmlUrlElement) && htmlUrlElement.ValueKind == JsonValueKind.String
                ? htmlUrlElement.GetString()
                : null;

            releasePage ??= downloadPage;

            var releasePublishedAt = GetReleasePublishedAt(root)
                                     ?? GetLatestAssetUpdatedAt(root);
            var hasNewerBuild = IsNewerBuildAvailable(currentVersion, latestVersion, releasePublishedAt, currentBuildDate);

            if (!IsUpdateAvailable(currentVersion, latestVersion, hasNewerBuild))
            {
                return UpdateCheckResponse.UpToDate();
            }

            var releaseId = GetReleaseId(root);

            var update = new UpdateCheckResult(
                currentVersion,
                latestVersion,
                releasePage,
                hasNewerBuild,
                releasePublishedAt,
                currentBuildDate,
                releaseId);

            return UpdateCheckResponse.Available(update);
        }
        catch (HttpRequestException)
        {
            return UpdateCheckResponse.Failed();
        }
        catch (TaskCanceledException)
        {
            return UpdateCheckResponse.Failed();
        }
    }

    public Version GetInstalledVersion() => GetCurrentVersion();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        if (!client.DefaultRequestHeaders.UserAgent.TryParseAdd("MSCS Update Service"))
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MSCS");
        }

        if (!client.DefaultRequestHeaders.Accept.TryParseAdd("application/vnd.github+json"))
        {
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }

        if (!client.DefaultRequestHeaders.Contains("X-GitHub-Api-Version"))
        {
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        }

        return client;
    }

    private static async Task<(JsonDocument? Document, ReleaseFetchStatus Status)> FetchLatestReleaseDocumentAsync(string apiEndpoint, CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(apiEndpoint, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                return (null, ReleaseFetchStatus.Failed);
            }

            return (JsonDocument.Parse(json), ReleaseFetchStatus.Success);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var fallbackEndpoint = GetFallbackReleasesEndpoint(apiEndpoint);
            if (fallbackEndpoint is null)
            {
                return (null, ReleaseFetchStatus.NotFound);
            }

            using var fallbackResponse = await HttpClient.GetAsync(fallbackEndpoint, cancellationToken).ConfigureAwait(false);
            if (!fallbackResponse.IsSuccessStatusCode)
            {
                return fallbackResponse.StatusCode == HttpStatusCode.NotFound
                    ? (null, ReleaseFetchStatus.NotFound)
                    : (null, ReleaseFetchStatus.Failed);
            }

            var fallbackJson = await fallbackResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(fallbackJson))
            {
                return (null, ReleaseFetchStatus.NotFound);
            }

            using var fallbackDocument = JsonDocument.Parse(fallbackJson);
            if (fallbackDocument.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in fallbackDocument.RootElement.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var latestJson = element.GetRawText();
                    if (!string.IsNullOrWhiteSpace(latestJson))
                    {
                        return (JsonDocument.Parse(latestJson), ReleaseFetchStatus.Success);
                    }
                }
            }

            return (null, ReleaseFetchStatus.NotFound);
        }

        return (null, ReleaseFetchStatus.Failed);
    }

    private static string? GetFallbackReleasesEndpoint(string apiEndpoint)
    {
        if (string.IsNullOrWhiteSpace(apiEndpoint))
        {
            return null;
        }

        const string latestSuffix = "/latest";
        if (!apiEndpoint.EndsWith(latestSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var baseEndpoint = apiEndpoint[..^latestSuffix.Length];
        if (string.IsNullOrWhiteSpace(baseEndpoint))
        {
            return null;
        }

        return baseEndpoint + "?per_page=1";
    }

    private enum ReleaseFetchStatus
    {
        Success,
        NotFound,
        Failed
    }


    private static Version GetCurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

        if (TryParseVersion(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                out var infoVersion))
        {
            return infoVersion;
        }

        if (TryParseVersion(assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version, out var fileVersion))
        {
            return fileVersion;
        }

        var assemblyVersion = assembly.GetName().Version;
        if (assemblyVersion is not null)
        {
            return assemblyVersion;
        }

        if (!string.IsNullOrWhiteSpace(assembly.Location))
        {
            var fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
            if (TryParseVersion(fileVersionInfo.ProductVersion, out var productVersion))
            {
                return productVersion;
            }
        }

        return new Version(0, 0, 0, 0);
    }

    private static string? GetVersionText(JsonElement root)
    {
        if (root.TryGetProperty("tag_name", out var tagElement) && tagElement.ValueKind == JsonValueKind.String)
        {
            return tagElement.GetString();
        }

        if (root.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
        {
            return nameElement.GetString();
        }

        return null;
    }

    private static bool TryParseVersion(string? versionText, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(versionText))
        {
            return false;
        }

        versionText = versionText.Trim();
        if (versionText.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            versionText = versionText[1..];
        }

        var separatorIndex = versionText.IndexOfAny(new[] { '-', '+' });
        if (separatorIndex > 0)
        {
            versionText = versionText[..separatorIndex];
        }

        if (Version.TryParse(versionText, out var parsedVersion))
        {
            version = NormalizeVersion(parsedVersion);
            return true;
        }

        var parts = versionText.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2)
        {
            var expandedVersion = string.Join('.', parts[0], parts[1], "0");
            if (Version.TryParse(expandedVersion, out parsedVersion))
            {
                version = NormalizeVersion(parsedVersion);
                return true;
            }
        }

        return false;
    }

    private static Version NormalizeVersion(Version version)
    {
        var build = version.Build >= 0 ? version.Build : 0;
        var revision = version.Revision >= 0 ? version.Revision : 0;

        return new Version(version.Major, version.Minor, build, revision);
    }
    private static DateTimeOffset? GetReleasePublishedAt(JsonElement root)
    {
        if (TryGetDateTime(root, "published_at", out var published))
        {
            return published;
        }

        if (TryGetDateTime(root, "created_at", out var created))
        {
            return created;
        }

        return null;
    }

    private static DateTimeOffset? GetLatestAssetUpdatedAt(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assetsElement) || assetsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        DateTimeOffset? latest = null;
        foreach (var asset in assetsElement.EnumerateArray())
        {
            if (TryGetDateTime(asset, "updated_at", out var updated))
            {
                if (latest is null || updated > latest)
                {
                    latest = updated;
                }
            }
        }

        return latest;
    }

    private static bool TryGetDateTime(JsonElement element, string propertyName, out DateTimeOffset value)
    {
        value = default;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = property.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out value);
    }

    private static bool TryGetCurrentBuildDate(out DateTimeOffset buildDate)
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

        if (TryGetAssemblyLocation(assembly, out var assemblyPath))
        {
            buildDate = File.GetLastWriteTimeUtc(assemblyPath);
            return true;
        }

        try
        {
            var processModule = Process.GetCurrentProcess().MainModule;
            if (!string.IsNullOrWhiteSpace(processModule?.FileName) && File.Exists(processModule.FileName))
            {
                buildDate = File.GetLastWriteTimeUtc(processModule.FileName);
                return true;
            }
        }
        catch
        {
            // ignored – we fall back to returning false below.
        }

        buildDate = default;
        return false;
    }

    private static bool TryGetAssemblyLocation(Assembly assembly, out string path)
    {
        path = assembly.Location;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            return true;
        }

        path = AppContext.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(path))
        {
            var moduleName = assembly.ManifestModule?.Name;
            if (!string.IsNullOrWhiteSpace(moduleName))
            {
                var fileName = Path.GetFileName(moduleName);
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    var potentialPath = Path.Combine(path, fileName);
                    if (File.Exists(potentialPath))
                    {
                        path = potentialPath;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool IsNewerBuildAvailable(
        Version currentVersion,
        Version latestVersion,
        DateTimeOffset? releasePublishedAt,
        DateTimeOffset? currentBuildDate)
    {
        if (latestVersion != currentVersion)
        {
            return false;
        }

        if (releasePublishedAt is null || currentBuildDate is null)
        {
            return false;
        }

        // Add a small tolerance to ignore file system rounding differences.
        return releasePublishedAt > currentBuildDate.Value.AddMinutes(1);
    }

    private static bool IsUpdateAvailable(Version currentVersion, Version latestVersion, bool hasNewerBuild)
    {
        if (latestVersion > currentVersion)
        {
            return true;
        }

        return hasNewerBuild;
    }

    private static long? GetReleaseId(JsonElement root)
    {
        if (root.TryGetProperty("id", out var idElement) &&
            idElement.ValueKind == JsonValueKind.Number &&
            idElement.TryGetInt64(out var id))
        {
            return id;
        }

        return null;
    }
}