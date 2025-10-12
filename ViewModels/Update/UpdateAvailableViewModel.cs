using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Input;
using MSCS.Commands;
using MSCS.Models;
using MSCS.ViewModels;

namespace MSCS.ViewModels.Update;

public sealed class UpdateAvailableViewModel : BaseViewModel
{
    private readonly Action _closeAction;

    public UpdateAvailableViewModel(UpdateCheckResult updateInfo, Action closeAction)
    {
        _closeAction = closeAction ?? throw new ArgumentNullException(nameof(closeAction));
        UpdateInfo = updateInfo ?? throw new ArgumentNullException(nameof(updateInfo));
        DownloadCommand = new RelayCommand(_ => OnDownloadRequested());
    }

    public UpdateCheckResult UpdateInfo { get; }

    public ICommand DownloadCommand { get; }

    public string WindowTitle => UpdateInfo.HasNewerBuild ? "New Build Available" : "New Version Available";

    public string UpdateHeadline => UpdateInfo.HasNewerBuild
        ? "A new build is available"
        : "A new update is available";

    public string UpdateDescription => UpdateInfo.HasNewerBuild
        ? "A refreshed build of this version is available with the latest fixes."
        : "Keep your Manga Scraper up to date to get the latest fixes.";

    public string CurrentVersionLabel => UpdateInfo.HasNewerBuild ? "Current build" : "Current version";

    public string LatestVersionLabel => UpdateInfo.HasNewerBuild ? "Latest build" : "Latest version";

    public string CurrentVersionDisplay => FormatVersion(UpdateInfo.CurrentVersion, UpdateInfo.CurrentBuildDate);

    public string LatestVersionDisplay => FormatVersion(UpdateInfo.LatestVersion, UpdateInfo.LatestPublishedAt);

    private static string FormatVersion(Version version, DateTimeOffset? timestamp)
    {
        if (timestamp is null)
        {
            return version.ToString();
        }

        var localTimestamp = timestamp.Value.ToLocalTime();
        var formattedTimestamp = localTimestamp.ToString("g", CultureInfo.CurrentCulture);
        return $"{version} ({formattedTimestamp})";
    }

    private void OnDownloadRequested()
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = UpdateInfo.DownloadUrl,
                UseShellExecute = true
            };
            Process.Start(processInfo);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to launch download URL: {ex}");
        }
        finally
        {
            _closeAction();
        }
    }
}