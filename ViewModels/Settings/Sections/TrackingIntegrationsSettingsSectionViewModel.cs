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

        public TrackingIntegrationsSettingsSectionViewModel(MediaTrackingServiceRegistry trackingRegistry)
            : base("trackingIntegrations", "Tracking integrations", "Connect your media-tracking accounts to sync reading progress across services.")
        {
            if (trackingRegistry == null)
            {
                throw new ArgumentNullException(nameof(trackingRegistry));
            }

            _providers = trackingRegistry.Services
                .OrderBy(service => service.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(service => new TrackingProviderViewModel(service))
                .ToList();

            TrackingProviders = new ReadOnlyCollection<TrackingProviderViewModel>(_providers);
            IsVisible = _providers.Count > 0;
        }

        public IReadOnlyList<TrackingProviderViewModel> TrackingProviders { get; }

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
        }
    }
}