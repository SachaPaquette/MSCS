using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
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

            var updateWindow = new UpdateAvailableWindow(result);
            if (Current?.MainWindow is { IsLoaded: true } mainWindow)
            {
                updateWindow.Owner = mainWindow;
            }

            updateWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to check for updates: {ex}");
        }
    }
}