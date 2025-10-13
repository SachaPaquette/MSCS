using MSCS.Commands;
using MSCS.Interfaces;
using System;
using System.Windows;
using System.Windows.Input;

namespace MSCS.ViewModels
{
    public class TrackingProviderViewModel : BaseViewModel, IDisposable
    {
        private readonly IMediaTrackingService _service;
        private bool _isDisposed;
        private bool _isConnected;
        private string? _userName;

        public TrackingProviderViewModel(IMediaTrackingService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));

            _isConnected = _service.IsAuthenticated;
            _userName = _service.UserName;

            ConnectCommand = new AsyncRelayCommand(_ => ConnectAsync(), _ => !IsConnected);
            DisconnectCommand = new AsyncRelayCommand(_ => DisconnectAsync(), _ => IsConnected);

            _service.AuthenticationChanged += OnAuthenticationChanged;
        }

        public string ServiceId => _service.ServiceId;

        public string DisplayName => _service.DisplayName;

        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (SetProperty(ref _isConnected, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string? UserName
        {
            get => _userName;
            private set
            {
                if (SetProperty(ref _userName, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        public string StatusText
        {
            get
            {
                if (IsConnected)
                {
                    return !string.IsNullOrWhiteSpace(UserName)
                        ? $"Connected as {UserName}"
                        : "Connected";
                }

                return "Not connected";
            }
        }

        public ICommand ConnectCommand { get; }

        public ICommand DisconnectCommand { get; }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _service.AuthenticationChanged -= OnAuthenticationChanged;
        }

        private async System.Threading.Tasks.Task ConnectAsync()
        {
            try
            {
                var owner = System.Windows.Application.Current?.MainWindow;
                var success = await _service.AuthenticateAsync(owner).ConfigureAwait(true);
                if (success)
                {
                    UpdateState();
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, DisplayName, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async System.Threading.Tasks.Task DisconnectAsync()
        {
            try
            {
                await _service.LogoutAsync().ConfigureAwait(true);
                UpdateState();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, DisplayName, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnAuthenticationChanged(object? sender, EventArgs e)
        {
            UpdateState();
        }

        private void UpdateState()
        {
            IsConnected = _service.IsAuthenticated;
            UserName = _service.UserName;
        }
    }
}