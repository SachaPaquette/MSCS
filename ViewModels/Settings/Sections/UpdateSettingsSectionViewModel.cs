using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using MSCS.Commands;
using MSCS.Models;
using MSCS.Services;
using MSCS.Views.Update;
using Application = System.Windows.Application;

namespace MSCS.ViewModels.Settings
{
    public sealed class UpdateSettingsSectionViewModel : SettingsSectionViewModel
    {
        private readonly UpdateService _updateService;
        private readonly UserSettings _userSettings;
        private readonly Version _installedVersion;
        private bool _isChecking;
        private string? _statusMessage;
        private DateTimeOffset? _lastCheckedAt;

        public UpdateSettingsSectionViewModel(UpdateService updateService, UserSettings userSettings)
            : base("updates", "Updates", "Check manually for new MSCS releases.")
        {
            _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));
            _userSettings = userSettings ?? throw new ArgumentNullException(nameof(userSettings));
            _installedVersion = _updateService.GetInstalledVersion();

            CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync);
        }

        public AsyncRelayCommand CheckForUpdatesCommand { get; }

        public bool IsChecking
        {
            get => _isChecking;
            private set
            {
                if (SetProperty(ref _isChecking, value))
                {
                    OnPropertyChanged(nameof(IsCheckEnabled));
                }
            }
        }

        public bool IsCheckEnabled => !IsChecking;

        public string? StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public DateTimeOffset? LastCheckedAt
        {
            get => _lastCheckedAt;
            private set
            {
                if (SetProperty(ref _lastCheckedAt, value))
                {
                    OnPropertyChanged(nameof(LastCheckedDisplay));
                }
            }
        }

        public string? LastCheckedDisplay => LastCheckedAt.HasValue
            ? string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                "Last checked: {0:g}",
                LastCheckedAt.Value.ToLocalTime())
            : null;

        public string InstalledVersionDisplay => string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            "Current version: {0}",
            _installedVersion);

        private async Task CheckForUpdatesAsync()
        {
            IsChecking = true;
            StatusMessage = "Checking for updates…";

            try
            {
                var response = await _updateService.CheckForUpdatesWithOutcomeAsync().ConfigureAwait(true);

                switch (response.Outcome)
                {
                    case UpdateCheckOutcome.UpdateAvailable when response.Update is not null:
                        ShowUpdateDialog(response.Update);
                        StatusMessage = response.Update.HasNewerBuild
                            ? "A newer build is available."
                            : string.Format(
                                System.Globalization.CultureInfo.CurrentCulture,
                                "Version {0} is available.",
                                response.Update.LatestVersion);
                        break;
                    case UpdateCheckOutcome.UpToDate:
                        StatusMessage = string.Format(
                            System.Globalization.CultureInfo.CurrentCulture,
                            "You're up to date on version {0}.",
                            _installedVersion);
                        break;
                    case UpdateCheckOutcome.Disabled:
                        StatusMessage = "Update checks are disabled.";
                        break;
                    default:
                        StatusMessage = "Unable to check for updates.";
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Manual update check failed: {ex}");
                StatusMessage = "Unable to check for updates.";
            }
            finally
            {
                LastCheckedAt = DateTimeOffset.UtcNow;
                IsChecking = false;
            }
        }

        private void ShowUpdateDialog(UpdateCheckResult update)
        {
            var updateWindow = new UpdateAvailableWindow(update);
            if (Application.Current?.MainWindow is { IsLoaded: true } owner)
            {
                updateWindow.Owner = owner;
            }

            updateWindow.ShowDialog();

            _userSettings.LastSeenUpdateId = update.ReleaseId;
            _userSettings.LastSeenUpdateTimestamp = update.LatestPublishedAt;
        }
    }
}