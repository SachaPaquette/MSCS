using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using MSCS.Models;
using MSCS.Services;
using MSCS.Views.Update;

namespace MSCS;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Dispatcher.InvokeAsync(CheckForUpdatesAsync, DispatcherPriority.ApplicationIdle);
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var updateService = new UpdateService();
            var result = await updateService.CheckForUpdatesAsync().ConfigureAwait(true);
            if (result is null)
            {
                return;
            }

            var userSettings = new UserSettings();
            if (ShouldSkipUpdateNotification(result, userSettings))
            {
                return;
            }

            var updateWindow = new UpdateAvailableWindow(result);
            if (Current?.MainWindow is { IsLoaded: true } mainWindow)
            {
                updateWindow.Owner = mainWindow;
            }

            updateWindow.ShowDialog();
            MarkUpdateAsSeen(result, userSettings);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to check for updates: {ex}");
        }
    }

    private static bool ShouldSkipUpdateNotification(UpdateCheckResult update, UserSettings settings)
    {
        if (settings is null)
        {
            return false;
        }

        var releaseId = update.ReleaseId;
        var publishedAt = update.LatestPublishedAt;

        if (releaseId is long id && settings.LastSeenUpdateId == id)
        {
            if (publishedAt is DateTimeOffset publishedTimestamp)
            {
                var seenTimestamp = settings.LastSeenUpdateTimestamp;
                return seenTimestamp is DateTimeOffset seen && publishedTimestamp <= seen;
            }

            return settings.LastSeenUpdateTimestamp is null;
        }

        if (releaseId is null && publishedAt is DateTimeOffset timestamp)
        {
            var seenTimestamp = settings.LastSeenUpdateTimestamp;
            if (seenTimestamp is DateTimeOffset seen && timestamp <= seen)
            {
                return true;
            }
        }

        return false;
    }

    private static void MarkUpdateAsSeen(UpdateCheckResult update, UserSettings settings)
    {
        if (settings is null)
        {
            return;
        }

        settings.LastSeenUpdateId = update.ReleaseId;
        settings.LastSeenUpdateTimestamp = update.LatestPublishedAt;
    }
}