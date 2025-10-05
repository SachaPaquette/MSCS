using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace MSCS.Views
{
    public partial class AniListOAuthWindow : Window
    {
        private static readonly TimeSpan DefaultTokenLifetime = TimeSpan.FromDays(365);
        private readonly Uri _authorizationUri;

        public AniListOAuthWindow(string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                throw new ArgumentException("Client id is required", nameof(clientId));
            }

            InitializeComponent();

            var encodedClientId = Uri.EscapeDataString(clientId);
            var redirectUri = Uri.EscapeDataString("https://anilist.co/api/v2/oauth/pin");
            _authorizationUri = new Uri($"https://anilist.co/api/v2/oauth/authorize?client_id={encodedClientId}&redirect_uri={redirectUri}&response_type=token");

            Loaded += OnLoaded;
        }

        public string? AccessToken { get; private set; }
        public TimeSpan TokenLifetime { get; private set; } = DefaultTokenLifetime;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            TokenTextBox.Focus();
        }

        private void ProceedButton_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _authorizationUri.ToString(),
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "AniList", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SubmitButton_OnClick(object sender, RoutedEventArgs e)
        {
            SubmitToken();
        }

        private void TokenTextBox_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SubmitToken();
                e.Handled = true;
            }
        }

        private void SubmitToken()
        {
            var token = TokenTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                System.Windows.MessageBox.Show(this, "Please paste the token from AniList before continuing.", "AniList", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            AccessToken = token;
            TokenLifetime = DefaultTokenLifetime;
            DialogResult = true;
        }
    }
}