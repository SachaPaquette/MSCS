using MSCS.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MSCS.ViewModels
{
    public sealed class TrackingWindowViewModel : BaseViewModel, IDisposable
    {
        private readonly ObservableCollection<ITrackingDialogViewModel> _providers;
        private readonly ReadOnlyObservableCollection<ITrackingDialogViewModel> _readonlyProviders;
        private ITrackingDialogViewModel? _selectedProvider;
        private bool _disposed;

        public TrackingWindowViewModel(string title, IEnumerable<ITrackingDialogViewModel> providers)
        {
            if (providers == null)
            {
                throw new ArgumentNullException(nameof(providers));
            }

            Title = title ?? throw new ArgumentNullException(nameof(title));
            _providers = new ObservableCollection<ITrackingDialogViewModel>(providers);
            _readonlyProviders = new ReadOnlyObservableCollection<ITrackingDialogViewModel>(_providers);

            foreach (var provider in _providers)
            {
                provider.CloseRequested += OnProviderCloseRequested;
            }

            _selectedProvider = _providers.FirstOrDefault();
        }

        public string Title { get; }

        public ReadOnlyObservableCollection<ITrackingDialogViewModel> Providers => _readonlyProviders;

        public ITrackingDialogViewModel? SelectedProvider
        {
            get => _selectedProvider;
            set => SetProperty(ref _selectedProvider, value);
        }

        public event EventHandler<bool>? CloseRequested;

        private void OnProviderCloseRequested(object? sender, bool e)
        {
            CloseRequested?.Invoke(this, e);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var provider in _providers)
            {
                provider.CloseRequested -= OnProviderCloseRequested;
                provider.Dispose();
            }
        }
    }
}