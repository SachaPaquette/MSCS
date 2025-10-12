using MSCS.Models;
using System;
using System.Diagnostics;
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

            if (latestVersion <= currentVersion)
            {
                return null;
            }

            var releasePage = root.TryGetProperty("html_url", out var htmlUrlElement) && htmlUrlElement.ValueKind == JsonValueKind.String
                ? htmlUrlElement.GetString()
                : null;

            releasePage ??= downloadPage;

            return new UpdateCheckResult(currentVersion, latestVersion, releasePage);
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
}