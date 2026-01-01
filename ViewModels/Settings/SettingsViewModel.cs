using System;
using System.Collections.ObjectModel;
using System.Linq;
using MSCS.Services;
using MSCS.ViewModels.Settings;

namespace MSCS.ViewModels
{
    public class SettingsViewModel : BaseViewModel, IDisposable
    {
        private bool _disposed;

        public SettingsViewModel(
            LocalLibraryService libraryService,
            UserSettings userSettings,
            ThemeService themeService,
            MediaTrackingServiceRegistry trackingRegistry,
            UpdateService updateService)
        {
            if (libraryService == null)
            {
                throw new ArgumentNullException(nameof(libraryService));
            }

            if (userSettings == null)
            {
                throw new ArgumentNullException(nameof(userSettings));
            }

            if (themeService == null)
            {
                throw new ArgumentNullException(nameof(themeService));
            }

            if (trackingRegistry == null)
            {
                throw new ArgumentNullException(nameof(trackingRegistry));
            }

            if (updateService == null)
            {
                throw new ArgumentNullException(nameof(updateService));
            }

            Sections = new ObservableCollection<SettingsSectionViewModel>
            {
                new AppearanceSettingsSectionViewModel(themeService, userSettings),
                new ReaderDefaultsSettingsSectionViewModel(userSettings),
                new LibraryFolderSettingsSectionViewModel(libraryService),
                new TrackingIntegrationsSettingsSectionViewModel(trackingRegistry, userSettings),
                new UpdateSettingsSectionViewModel(updateService, userSettings),
            };
        }

        public ObservableCollection<SettingsSectionViewModel> Sections { get; }

        public SettingsSectionViewModel? GetSection(string key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            return Sections.FirstOrDefault(section => string.Equals(section.Key, key, StringComparison.OrdinalIgnoreCase));
        }

        public T? GetSection<T>() where T : SettingsSectionViewModel
        {
            return Sections.OfType<T>().FirstOrDefault();
        }

        public bool TrySetSectionVisibility(string key, bool isVisible)
        {
            var section = GetSection(key);
            if (section == null)
            {
                return false;
            }

            section.IsVisible = isVisible;
            return true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            foreach (var section in Sections)
            {
                section.Dispose();
            }
        }
    }
}