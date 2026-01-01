using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MSCS.Services;

namespace MSCS.ViewModels.Settings
{
    public class TrackingIntegrationsSettingsSectionViewModel : SettingsSectionViewModel
    {
        private readonly List<TrackingProviderViewModel> _providers;
        private readonly UserSettings _userSettings;
        private string? _myAnimeListClientId;

        public TrackingIntegrationsSettingsSectionViewModel(MediaTrackingServiceRegistry trackingRegistry, UserSettings userSettings)
            : base("trackingIntegrations", "Tracking integrations", "Connect your media-tracking accounts to sync reading progress across services.")
        {
            if (trackingRegistry == null)
            {
                throw new ArgumentNullException(nameof(trackingRegistry));
            }

            if (userSettings == null)
            {
                throw new ArgumentNullException(nameof(userSettings));
            }

            _userSettings = userSettings;
            _myAnimeListClientId = _userSettings.MyAnimeListClientId;
            _userSettings.SettingsChanged += OnUserSettingsChanged;
            _providers = trackingRegistry.Services
                .OrderBy(service => service.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(service => new TrackingProviderViewModel(service))
                .ToList();

            TrackingProviders = new ReadOnlyCollection<TrackingProviderViewModel>(_providers);
            IsVisible = _providers.Count > 0;
        }

        public IReadOnlyList<TrackingProviderViewModel> TrackingProviders { get; }

        public string? MyAnimeListClientId
        {
            get => _myAnimeListClientId;
            set
            {
                var sanitized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                if (SetProperty(ref _myAnimeListClientId, sanitized))
                {
                    _userSettings.MyAnimeListClientId = sanitized;
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            foreach (var provider in _providers)
            {
                provider.Dispose();
            }

            _userSettings.SettingsChanged -= OnUserSettingsChanged;
        }

        private void OnUserSettingsChanged(object? sender, EventArgs e)
        {
            MyAnimeListClientId = _userSettings.MyAnimeListClientId;
        }
    }
}