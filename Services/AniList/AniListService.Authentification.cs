using MSCS.Models;
using System;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace MSCS.Services
{
    public partial class AniListService
    {
        public async Task<bool> AuthenticateAsync(Window? owner)
        {
            var authWindow = new Views.AniListOAuthWindow();
            if (owner != null)
            {
                authWindow.Owner = owner;
            }

            var result = authWindow.ShowDialog();
            if (result != true || string.IsNullOrWhiteSpace(authWindow.AccessToken))
            {
                return false;
            }

            ApplyAccessToken(authWindow.AccessToken!, DateTimeOffset.UtcNow.Add(authWindow.TokenLifetime));
            await FetchViewerAsync().ConfigureAwait(false);
            AuthenticationChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        private void LoadExistingAuthentication()
        {
            _accessToken = _userSettings.AniListAccessToken;
            _tokenExpiry = _userSettings.AniListAccessTokenExpiry;
            _userName = _userSettings.AniListUserName;

            if (IsAuthenticated)
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            }
        }

        public Task LogoutAsync()
        {
            _accessToken = null;
            _tokenExpiry = null;
            _userName = null;
            _httpClient.DefaultRequestHeaders.Authorization = null;

            _userSettings.ClearAniListAuthentication();
            _userSettings.ClearAniListTracking();

            AuthenticationChanged?.Invoke(this, EventArgs.Empty);
            RaiseTrackingChanged(new AniListTrackingChangedEventArgs(null, 0, null));
            return Task.CompletedTask;
        }

        private void ApplyAccessToken(string token, DateTimeOffset expiry)
        {
            _accessToken = token;
            _tokenExpiry = expiry;
            _userSettings.AniListAccessToken = token;
            _userSettings.AniListAccessTokenExpiry = expiry;
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        private async Task FetchViewerAsync()
        {
            const string query = "query { Viewer { id name } }";
            using var document = await TrySendGraphQlRequestAsync(query, new { }, CancellationToken.None).ConfigureAwait(false);
            if (document == null)
            {
                return;
            }
            if (document.RootElement.TryGetProperty("data", out var dataElement) &&
                dataElement.TryGetProperty("Viewer", out var viewerElement) &&
                viewerElement.TryGetProperty("name", out var nameElement))
            {
                _userName = nameElement.GetString();
                _userSettings.AniListUserName = _userName;
            }
        }
    }
}