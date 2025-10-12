using MSCS.Models;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
        var currentVersion = GetCurrentVersion();
        var currentBuildDate = TryGetCurrentBuildDate(out var buildDate)
            ? buildDate
            : (DateTimeOffset?)null;

        var apiEndpoint = Environment.GetEnvironmentVariable(LatestReleaseApiEnvironmentVariable) ?? DefaultLatestReleaseApiEndpoint;
        var downloadPage = Environment.GetEnvironmentVariable(ReleasePageEnvironmentVariable) ?? DefaultReleasePageUrl;

        if (string.IsNullOrWhiteSpace(apiEndpoint))
        {
            return null;
        }

        try
        {
            using var response = await HttpClient.GetAsync(apiEndpoint, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;

            var versionText = GetVersionText(root);
            if (!TryParseVersion(versionText, out var latestVersion))
            {
                return null;
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
                return null;
            }

            var releaseId = GetReleaseId(root);

            return new UpdateCheckResult(
                currentVersion,
                latestVersion,
                releasePage,
                hasNewerBuild,
                releasePublishedAt,
                currentBuildDate,
                releaseId);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

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

        return client;
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