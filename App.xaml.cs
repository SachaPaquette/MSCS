using MSCS.Interfaces;
using MSCS.Models;
using MSCS.Services;
using MSCS.Services.Kitsu;
using MSCS.Services.MyAnimeList;
using MSCS.Sources;
using MSCS.ViewModels;
using MSCS.Views;
using MSCS.Views.Update;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace MSCS;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{

    private IHost? _host;

    public static IServiceProvider Services =>
        (Current as App)?._host?.Services
        ?? throw new InvalidOperationException("The application host has not been initialized.");

    public static T GetRequiredService<T>() where T : notnull => Services.GetRequiredService<T>();

    protected override async void OnStartup(StartupEventArgs e)
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(ConfigureServices)
            .Build();

        await _host.StartAsync().ConfigureAwait(true);

        base.OnStartup(e);

        var mainWindow = Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();

        Dispatcher.InvokeAsync(CheckForUpdatesAsync, DispatcherPriority.ApplicationIdle);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host != null)
        {
            _host.StopAsync().GetAwaiter().GetResult();
            _host.Dispose();
            _host = null;
        }

        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<UserSettings>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<LocalLibraryService>();
        services.AddSingleton<ReadingListService>();
        services.AddSingleton<LocalSource>();

        services.AddSingleton<IAniListService, AniListService>();
        services.AddSingleton<MyAnimeListService>();
        services.AddSingleton<KitsuService>();

        services.AddSingleton<MediaTrackingServiceRegistry>(provider =>
        {
            var registry = new MediaTrackingServiceRegistry();
            registry.Register(provider.GetRequiredService<IAniListService>());
            registry.Register(provider.GetRequiredService<MyAnimeListService>());
            registry.Register(provider.GetRequiredService<KitsuService>());
            return registry;
        });

        services.AddSingleton<NavigationService>(provider =>
            new NavigationService(type =>
            {
                if (provider.GetService(type) is BaseViewModel resolved)
                {
                    return resolved;
                }

                return (BaseViewModel)Activator.CreateInstance(type)!;
            }));
        services.AddSingleton<INavigationService>(provider => provider.GetRequiredService<NavigationService>());

        services.AddSingleton(provider => new MangaListViewModel(
        SourceKeyConstants.DefaultExternal,
        provider.GetRequiredService<INavigationService>(),
        provider.GetRequiredService<UserSettings>()));
        services.AddSingleton<LocalLibraryViewModel>();
        services.AddSingleton<BookmarkLibraryViewModel>();
        services.AddSingleton<AniListTrackingLibraryViewModel>();
        services.AddSingleton<MyAnimeListTrackingLibraryViewModel>();
        services.AddSingleton<KitsuTrackingLibraryViewModel>();
        services.AddSingleton<TrackingLibrariesViewModel>();
        services.AddSingleton(provider => new AniListRecommendationsViewModel(provider.GetRequiredService<IAniListService>()));
        services.AddSingleton(provider => new ContinueReadingViewModel(
        provider.GetRequiredService<UserSettings>(),
        provider.GetRequiredService<ReadingListService>()));
        services.AddSingleton(provider => new SettingsViewModel(
        provider.GetRequiredService<LocalLibraryService>(),
        provider.GetRequiredService<UserSettings>(),
        provider.GetRequiredService<ThemeService>(),
        provider.GetRequiredService<MediaTrackingServiceRegistry>(),
        provider.GetRequiredService<UpdateService>()));
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }


    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var updateService = Services.GetRequiredService<UpdateService>();
            var userSettings = Services.GetRequiredService<UserSettings>();
            var result = await updateService.CheckForUpdatesAsync().ConfigureAwait(true);
            if (result is null)
            {
                return;
            }

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