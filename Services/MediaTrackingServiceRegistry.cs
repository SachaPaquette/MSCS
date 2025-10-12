using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MSCS.Interfaces;

namespace MSCS.Services
{
    /// <summary>
    /// Maintains a registry of tracking providers so that features such as list import/export or
    /// progress mirroring can operate across multiple catalog services.
    /// </summary>
    public class MediaTrackingServiceRegistry
    {
        private readonly Dictionary<string, IMediaTrackingService> _servicesById = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<IMediaTrackingService> _services = new();
        private readonly ReadOnlyCollection<IMediaTrackingService> _readOnlyView;

        public MediaTrackingServiceRegistry()
        {
            _readOnlyView = new ReadOnlyCollection<IMediaTrackingService>(_services);
        }

        /// <summary>
        /// Gets all registered providers in registration order.
        /// </summary>
        public IReadOnlyCollection<IMediaTrackingService> Services => _readOnlyView;

        /// <summary>
        /// Registers a provider instance. Duplicate identifiers are rejected to prevent accidental overrides.
        /// </summary>
        /// <param name="service">The provider to register.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="service"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when a provider with the same <see cref="IMediaTrackingService.ServiceId"/> has already been registered.</exception>
        public void Register(IMediaTrackingService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            if (_servicesById.ContainsKey(service.ServiceId))
            {
                throw new InvalidOperationException($"A tracking service with the id '{service.ServiceId}' has already been registered.");
            }

            _servicesById[service.ServiceId] = service;
            _services.Add(service);
        }

        /// <summary>
        /// Attempts to retrieve a provider using its identifier.
        /// </summary>
        public bool TryGet(string serviceId, out IMediaTrackingService? service)
        {
            if (string.IsNullOrWhiteSpace(serviceId))
            {
                service = null;
                return false;
            }

            return _servicesById.TryGetValue(serviceId, out service);
        }

        /// <summary>
        /// Attempts to retrieve a provider using its identifier and cast it to a typed instance.
        /// </summary>
        public bool TryGet<TMedia, TTrackingInfo, TStatus>(
            string serviceId,
            out IMediaTrackingService<TMedia, TTrackingInfo, TStatus>? service)
            where TStatus : struct
        {
            service = null;
            if (!TryGet(serviceId, out var untyped) || untyped is not IMediaTrackingService<TMedia, TTrackingInfo, TStatus> typed)
            {
                return false;
            }

            service = typed;
            return true;
        }

        /// <summary>
        /// Enumerates all registered providers that can be cast to the specified generic interface.
        /// </summary>
        public IEnumerable<IMediaTrackingService<TMedia, TTrackingInfo, TStatus>> GetAll<TMedia, TTrackingInfo, TStatus>()
            where TStatus : struct
        {
            return _services.OfType<IMediaTrackingService<TMedia, TTrackingInfo, TStatus>>();
        }
    }
}