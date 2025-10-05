using MSCS.Interfaces;
using MSCS.Models;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace MSCS.Services
{
    public class AniListService : IAniListService
    {
        private const string GraphQlEndpoint = "https://graphql.anilist.co";
        private readonly HttpClient _httpClient;
        private readonly UserSettings _userSettings;
        private string? _accessToken;
        private DateTimeOffset? _tokenExpiry;
        private string? _userName;

        public AniListService(UserSettings userSettings)
        {
            _userSettings = userSettings ?? throw new ArgumentNullException(nameof(userSettings));
            _httpClient = new HttpClient();

            LoadExistingAuthentication();
        }

        public bool IsAuthenticated => !string.IsNullOrWhiteSpace(_accessToken) &&
                                        (!_tokenExpiry.HasValue || _tokenExpiry > DateTimeOffset.UtcNow);

        public string? UserName => _userName;

        public event EventHandler? AuthenticationChanged;
        public event EventHandler? TrackingChanged;

        public async Task<bool> AuthenticateAsync(Window? owner)
        {
            var clientId = _userSettings.AniListClientId;
            if (string.IsNullOrWhiteSpace(clientId))
            {
                throw new InvalidOperationException("AniList client id is not configured. Set it in Settings before authenticating.");
            }

            var authWindow = new Views.AniListOAuthWindow(clientId);
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

        public async Task<IReadOnlyList<AniListMedia>> SearchSeriesAsync(string query, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Array.Empty<AniListMedia>();
            }

            const string gqlQuery = @"query ($search: String) {
  Page(perPage: 20) {
    media(search: $search, type: MANGA) {
      id
      status
      title {
        romaji
        english
        native
      }
      coverImage {
        large
      }
      bannerImage
      startDate {
        year
        month
        day
      }
    }
  }
}";

            var variables = new
            {
                search = query
            };

            using var document = await SendGraphQlRequestAsync(gqlQuery, variables, cancellationToken).ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty("data", out var dataElement))
            {
                return Array.Empty<AniListMedia>();
            }

            var results = new List<AniListMedia>();
            if (dataElement.TryGetProperty("Page", out var pageElement) &&
                pageElement.TryGetProperty("media", out var mediaArray) &&
                mediaArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var media in mediaArray.EnumerateArray())
                {
                    var id = media.TryGetProperty("id", out var idElement) ? idElement.GetInt32() : 0;
                    if (id == 0)
                    {
                        continue;
                    }

                    var titleElement = media.GetProperty("title");
                    var romaji = titleElement.TryGetProperty("romaji", out var romajiElement) ? romajiElement.GetString() : null;
                    var english = titleElement.TryGetProperty("english", out var englishElement) ? englishElement.GetString() : null;
                    var nativeTitle = titleElement.TryGetProperty("native", out var nativeElement) ? nativeElement.GetString() : null;
                    var status = media.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null;
                    var cover = media.TryGetProperty("coverImage", out var coverElement) &&
                                coverElement.TryGetProperty("large", out var coverUrl) ? coverUrl.GetString() : null;
                    var banner = media.TryGetProperty("bannerImage", out var bannerElement) ? bannerElement.GetString() : null;
                    var startDate = media.TryGetProperty("startDate", out var startDateElement)
                        ? FormatDate(startDateElement)
                        : null;

                    results.Add(new AniListMedia
                    {
                        Id = id,
                        RomajiTitle = romaji,
                        EnglishTitle = english,
                        NativeTitle = nativeTitle,
                        Status = status,
                        CoverImageUrl = cover,
                        BannerImageUrl = banner,
                        StartDateText = startDate
                    });
                }
            }

            return results;
        }

        public async Task<AniListTrackingInfo> TrackSeriesAsync(string mangaTitle, AniListMedia media, CancellationToken cancellationToken = default)
        {
            if (media == null) throw new ArgumentNullException(nameof(media));
            EnsureAuthenticated();

            const string mutation = @"mutation($mediaId: Int!, $status: MediaListStatus) {
  SaveMediaListEntry(mediaId: $mediaId, status: $status) {
    id
    status
  }
}";

            var variables = new
            {
                mediaId = media.Id,
                status = "CURRENT"
            };

            using var _ = await SendGraphQlRequestAsync(mutation, variables, cancellationToken).ConfigureAwait(false);

            var trackingInfo = new AniListTrackingInfo(media.Id, media.DisplayTitle, media.CoverImageUrl);
            _userSettings.SetAniListTracking(mangaTitle, trackingInfo);
            TrackingChanged?.Invoke(this, EventArgs.Empty);
            return trackingInfo;
        }

        public async Task UpdateProgressAsync(string mangaTitle, int progress, CancellationToken cancellationToken = default)
        {
            if (progress <= 0)
            {
                return;
            }

            if (!TryGetTracking(mangaTitle, out var trackingInfo) || trackingInfo == null)
            {
                return;
            }

            EnsureAuthenticated();

            const string mutation = @"mutation($mediaId: Int!, $progress: Int) {
  SaveMediaListEntry(mediaId: $mediaId, progress: $progress) {
    id
    progress
  }
}";

            var variables = new
            {
                mediaId = trackingInfo.MediaId,
                progress
            };

            using var _ = await SendGraphQlRequestAsync(mutation, variables, cancellationToken).ConfigureAwait(false);
        }

        public bool TryGetTracking(string mangaTitle, out AniListTrackingInfo? trackingInfo)
        {
            return _userSettings.TryGetAniListTracking(mangaTitle, out trackingInfo);
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
            using var document = await SendGraphQlRequestAsync(query, new { }, CancellationToken.None).ConfigureAwait(false);
            if (document.RootElement.TryGetProperty("data", out var dataElement) &&
                dataElement.TryGetProperty("Viewer", out var viewerElement) &&
                viewerElement.TryGetProperty("name", out var nameElement))
            {
                _userName = nameElement.GetString();
                _userSettings.AniListUserName = _userName;
            }
        }

        private static string? FormatDate(JsonElement startDateElement)
        {
            var year = startDateElement.TryGetProperty("year", out var yearElement) ? yearElement.GetInt32() : (int?)null;
            var month = startDateElement.TryGetProperty("month", out var monthElement) ? monthElement.GetInt32() : (int?)null;
            var day = startDateElement.TryGetProperty("day", out var dayElement) ? dayElement.GetInt32() : (int?)null;

            if (year == null)
            {
                return null;
            }

            if (month == null)
            {
                return year.ToString();
            }

            if (day == null)
            {
                return $"{year}-{month:00}";
            }

            return $"{year}-{month:00}-{day:00}";
        }

        private async Task<JsonDocument> SendGraphQlRequestAsync(string query, object variables, CancellationToken cancellationToken)
        {
            var payload = JsonSerializer.Serialize(new GraphQlRequest(query, variables));
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(GraphQlEndpoint, content, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (document.RootElement.TryGetProperty("errors", out var errorsElement) &&
                errorsElement.ValueKind == JsonValueKind.Array &&
                errorsElement.GetArrayLength() > 0)
            {
                var firstError = errorsElement[0];
                var message = firstError.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString()
                    : "AniList request failed.";
                throw new InvalidOperationException(message ?? "AniList request failed.");
            }

            return document;
        }

        private void EnsureAuthenticated()
        {
            if (!IsAuthenticated)
            {
                throw new InvalidOperationException("AniList authentication is required for this operation.");
            }
        }

        private sealed record GraphQlRequest(string query, object variables);
    }
}