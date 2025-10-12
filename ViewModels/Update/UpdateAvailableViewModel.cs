using System;
using System.Diagnostics;
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

    public string CurrentVersionDisplay => UpdateInfo.CurrentVersion.ToString();

    public string LatestVersionDisplay => UpdateInfo.LatestVersion.ToString();

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