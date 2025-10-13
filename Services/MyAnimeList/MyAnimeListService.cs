using MSCS.Enums;
using MSCS.Helpers;
using MSCS.Interfaces;
using MSCS.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace MSCS.Services.MyAnimeList
{
    public class MyAnimeListService : IMediaTrackingService<MyAnimeListMedia, MyAnimeListTrackingInfo, MyAnimeListStatus>
    {
        private const string ApiBaseUrl = "https://api.myanimelist.net/v2/";
        private const string AuthorizationEndpoint = "https://myanimelist.net/v1/oauth2/authorize";
        private const string TokenEndpoint = "https://myanimelist.net/v1/oauth2/token";
        private const string RedirectUri = "http://127.0.0.1:51789/callback";
        private static readonly TimeSpan TokenRefreshSkew = TimeSpan.FromMinutes(1);

        private readonly Dictionary<string, MyAnimeListTrackingInfo> _trackedSeries = new(StringComparer.OrdinalIgnoreCase);
        private readonly UserSettings _userSettings;
        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _authLock = new(1, 1);
        private readonly string _clientId;

        private bool _isAuthenticated;
        private string? _accessToken;
        private string? _refreshToken;
        private DateTimeOffset? _tokenExpiry;
        private string? _userName;

        public MyAnimeListService(UserSettings userSettings)
        {
            _userSettings = userSettings ?? throw new ArgumentNullException(nameof(userSettings));
            _httpClient = CreateHttpClient();
            _clientId = Constants.MyAnimeListClientId;

            if (!string.IsNullOrWhiteSpace(_clientId))
            {
                _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-MAL-CLIENT-ID", _clientId);
            }

            LoadExistingAuthentication();

            var snapshot = _userSettings.GetExternalTrackingSnapshot(ServiceId);
            foreach (var kvp in snapshot)
            {
                var info = ConvertFromEntry(kvp.Key, kvp.Value);
                if (info != null)
                {
                    _trackedSeries[kvp.Key] = info;
                }
            }
        }

        public string ServiceId => "MyAnimeList";

        public string DisplayName => "MyAnimeList";

        public bool IsAuthenticated => _isAuthenticated;

        public string? UserName => _userName;

        public event EventHandler? AuthenticationChanged;

        public event EventHandler<MediaTrackingChangedEventArgs<MyAnimeListTrackingInfo>>? MediaTrackingChanged;

        public async Task<bool> AuthenticateAsync(Window? owner)
        {
            if (string.IsNullOrWhiteSpace(_clientId))
            {
                System.Windows.MessageBox.Show(owner ?? System.Windows.Application.Current?.MainWindow,
                    "A MyAnimeList client id is required. Set the MSCS_MYANIMELIST_CLIENT_ID environment variable and try again.",
                    "MyAnimeList", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            // PKCE
            var codeVerifier = GenerateCodeVerifier();
            var codeChallenge = CreateCodeChallenge(codeVerifier);
            var state = GenerateState();

            var authorizationUri = BuildAuthorizationUri(codeChallenge, state);

            using var listener = new HttpListener();
            listener.Prefixes.Add("http://127.0.0.1:51789/callback/");
            try { listener.Start(); }
            catch (HttpListenerException)
            {
                System.Windows.MessageBox.Show(owner ?? System.Windows.Application.Current?.MainWindow,
                    "Unable to start local redirect listener on http://127.0.0.1:51789/callback.\nMake sure the port is free and try again.",
                    "MyAnimeList", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = authorizationUri.ToString(),
                    UseShellExecute = true
                });
            }
            catch
            {
                listener.Stop();
                System.Windows.MessageBox.Show(owner ?? System.Windows.Application.Current?.MainWindow,
                    "Failed to open the browser for MyAnimeList sign-in.",
                    "MyAnimeList", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            HttpListenerContext ctx;
            try
            {
                ctx = await listener.GetContextAsync();
            }
            catch
            {
                listener.Stop();
                return false;
            }

            var req = ctx.Request;
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var code = query["code"];
            var st = query["state"];

            var html = "<html><body style='font-family:sans-serif'>You can close this window and return to the app.</body></html>";
            var bytes = Encoding.UTF8.GetBytes(html);
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
            listener.Stop();

            if (string.IsNullOrWhiteSpace(code) || !string.Equals(st, state, StringComparison.Ordinal))
            {
                System.Windows.MessageBox.Show(owner ?? System.Windows.Application.Current?.MainWindow,
                    "Sign-in was cancelled or invalid (state mismatch).",
                    "MyAnimeList", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            var token = await RequestTokenAsync(new Dictionary<string, string>
            {
                ["client_id"] = _clientId,
                ["grant_type"] = "authorization_code",
                ["code"] = code!,
                ["code_verifier"] = codeVerifier,
                ["redirect_uri"] = RedirectUri
            }, CancellationToken.None);

            if (token == null)
            {
                System.Windows.MessageBox.Show(owner ?? System.Windows.Application.Current?.MainWindow,
                    "Unable to authenticate with MyAnimeList. Please try again.",
                    "MyAnimeList", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            ApplyToken(token);
            await FetchProfileAsync(CancellationToken.None);

            _isAuthenticated = true;
            _userSettings.SetTrackingProviderConnection(ServiceId, true);
            AuthenticationChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public Task LogoutAsync()
        {
            var wasAuthenticated = _isAuthenticated;

            _isAuthenticated = false;
            _accessToken = null;
            _refreshToken = null;
            _tokenExpiry = null;
            _userName = null;
            _httpClient.DefaultRequestHeaders.Authorization = null;

            _userSettings.ClearMyAnimeListAuthentication();
            _userSettings.SetTrackingProviderConnection(ServiceId, false);

            if (wasAuthenticated)
            {
                AuthenticationChanged?.Invoke(this, EventArgs.Empty);
            }

            return Task.CompletedTask;
        }

        public async Task<IReadOnlyList<MyAnimeListMedia>> SearchSeriesAsync(string query, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Array.Empty<MyAnimeListMedia>();
            }

            try
            {
                var requestUri = $"manga?q={Uri.EscapeDataString(query)}&limit=20&nsfw=true&fields=title,main_picture,synopsis,num_chapters,mean";
                using var response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return Array.Empty<MyAnimeListMedia>();
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!document.RootElement.TryGetProperty("data", out var dataArray) || dataArray.ValueKind != JsonValueKind.Array)
                {
                    return Array.Empty<MyAnimeListMedia>();
                }

                var results = new List<MyAnimeListMedia>();
                foreach (var element in dataArray.EnumerateArray())
                {
                    var media = ParseMedia(element);
                    if (media != null)
                    {
                        results.Add(media);
                    }
                }

                return new ReadOnlyCollection<MyAnimeListMedia>(results);
            }
            catch
            {
                return Array.Empty<MyAnimeListMedia>();
            }
        }

        public async Task<IReadOnlyDictionary<MyAnimeListStatus, IReadOnlyList<MyAnimeListMedia>>> GetUserListsAsync(CancellationToken cancellationToken = default)
        {
            var groups = CreateStatusBuckets();

            if (!await EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false))
            {
                return BuildGroupsFromLocal(groups);
            }

            var requestUri = "users/@me/mangalist?limit=100&fields=list_status{status,score,num_chapters_read,updated_at},num_chapters,mean,main_picture";

            try
            {
                while (!string.IsNullOrEmpty(requestUri))
                {
                    using var response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        return BuildGroupsFromLocal(groups);
                    }

                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

                    if (document.RootElement.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in dataElement.EnumerateArray())
                        {
                            if (!item.TryGetProperty("list_status", out var statusElement) || statusElement.ValueKind != JsonValueKind.Object)
                            {
                                continue;
                            }

                            var status = ParseStatus(statusElement.TryGetProperty("status", out var statusValue) ? statusValue.GetString() : null);
                            if (status == null)
                            {
                                continue;
                            }

                            var media = ParseMedia(item);
                            if (media == null)
                            {
                                continue;
                            }

                            groups[status.Value].Add(media);
                            UpdateLocalTrackingFromRemote(media, statusElement);
                        }
                    }

                    if (document.RootElement.TryGetProperty("paging", out var pagingElement) &&
                        pagingElement.ValueKind == JsonValueKind.Object &&
                        pagingElement.TryGetProperty("next", out var nextElement) &&
                        nextElement.ValueKind == JsonValueKind.String)
                    {
                        var next = nextElement.GetString();
                        if (!string.IsNullOrEmpty(next) && next.StartsWith(ApiBaseUrl, StringComparison.OrdinalIgnoreCase))
                        {
                            requestUri = next.Substring(ApiBaseUrl.Length);
                        }
                        else
                        {
                            requestUri = next;
                        }
                    }
                    else
                    {
                        requestUri = null;
                    }
                }
            }
            catch
            {
                return BuildGroupsFromLocal(groups);
            }

            var existingIds = new HashSet<int>();
            foreach (var bucket in groups.Values)
            {
                foreach (var media in bucket)
                {
                    if (media.Id > 0)
                    {
                        existingIds.Add(media.Id);
                    }
                }
            }

            AddLocalEntries(groups, existingIds);
            return CreateReadOnlyGroups(groups);
        }

        public async Task UpdateProgressAsync(string seriesTitle, int progress, CancellationToken cancellationToken = default)
        {
            await UpdateTrackingInternalAsync(seriesTitle, progress: progress, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task<MyAnimeListTrackingInfo> TrackSeriesAsync(
            string seriesTitle,
            MyAnimeListMedia media,
            MyAnimeListStatus? status = null,
            int? progress = null,
            double? score = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(seriesTitle))
            {
                throw new ArgumentException("Series title cannot be empty.", nameof(seriesTitle));
            }

            if (media == null)
            {
                throw new ArgumentNullException(nameof(media));
            }

            var selectedStatus = status ?? MyAnimeListStatus.Reading;
            var sanitizedScore = ClampScore(score ?? media.Score);
            var normalizedProgress = NormalizeProgress(progress);

            if (media.Id > 0 && await EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false))
            {
                var payload = new Dictionary<string, string>
                {
                    ["status"] = FormatStatus(selectedStatus)
                };

                if (normalizedProgress.HasValue)
                {
                    payload["num_chapters_read"] = normalizedProgress.Value.ToString(CultureInfo.InvariantCulture);
                }

                if (sanitizedScore.HasValue)
                {
                    payload["score"] = Math.Round(sanitizedScore.Value).ToString(CultureInfo.InvariantCulture);
                }

                var request = new HttpRequestMessage(new HttpMethod("PATCH"), $"manga/{media.Id}/my_list_status")
                {
                    Content = new FormUrlEncodedContent(payload)
                };

                try
                {
                    using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                        var trackingInfo = CreateTrackingInfo(media, document.RootElement);
                        SaveTracking(seriesTitle, trackingInfo);
                        RaiseTrackingChanged(seriesTitle, trackingInfo);
                        return trackingInfo;
                    }
                }
                catch
                {
                    // Fall back to local storage when the API call fails.
                }
            }

            var info = new MyAnimeListTrackingInfo(
                media.Id,
                media.Title,
                media.CoverImageUrl,
                selectedStatus,
                normalizedProgress,
                sanitizedScore,
                media.Chapters,
                media.SiteUrl,
                DateTimeOffset.UtcNow);

            SaveTracking(seriesTitle, info);
            RaiseTrackingChanged(seriesTitle, info);
            return info;
        }

        public Task<MyAnimeListTrackingInfo?> UpdateTrackingAsync(
            string seriesTitle,
            MyAnimeListStatus? status = null,
            int? progress = null,
            double? score = null,
            CancellationToken cancellationToken = default)
        {
            return UpdateTrackingInternalAsync(seriesTitle, status, progress, score, cancellationToken);
        }

        public async Task<MyAnimeListTrackingInfo?> RefreshTrackingAsync(string seriesTitle, CancellationToken cancellationToken = default)
        {
            if (!_trackedSeries.TryGetValue(seriesTitle, out var existing))
            {
                return null;
            }

            if (existing.MediaId <= 0 || !await EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false))
            {
                return existing;
            }

            var fields = "title,main_picture,synopsis,num_chapters,mean,my_list_status{status,score,num_chapters_read,updated_at}";

            try
            {
                using var response = await _httpClient.GetAsync($"manga/{existing.MediaId}?fields={Uri.EscapeDataString(fields)}", cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return existing;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

                var media = ParseMedia(document.RootElement);
                if (media == null)
                {
                    return existing;
                }

                MyAnimeListTrackingInfo updated;
                if (document.RootElement.TryGetProperty("my_list_status", out var statusElement) && statusElement.ValueKind == JsonValueKind.Object)
                {
                    updated = CreateTrackingInfo(media, statusElement);
                }
                else
                {
                    updated = existing.With(
                        totalChapters: media.Chapters ?? existing.TotalChapters,
                        coverImageUrl: string.IsNullOrWhiteSpace(media.CoverImageUrl) ? existing.CoverImageUrl : media.CoverImageUrl,
                        siteUrl: string.IsNullOrWhiteSpace(media.SiteUrl) ? existing.SiteUrl : media.SiteUrl,
                        updatedAt: DateTimeOffset.UtcNow);
                }

                SaveTracking(seriesTitle, updated);
                RaiseTrackingChanged(seriesTitle, updated);
                return updated;
            }
            catch
            {
                return existing;
            }
        }

        public async Task<bool> UntrackSeriesAsync(string seriesTitle, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(seriesTitle))
            {
                return false;
            }

            if (_trackedSeries.TryGetValue(seriesTitle, out var existing) && existing.MediaId > 0)
            {
                if (await EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false))
                {
                    try
                    {
                        using var response = await _httpClient.DeleteAsync($"manga/{existing.MediaId}/my_list_status", cancellationToken).ConfigureAwait(false);
                        // Ignore unsuccessful responses – we still remove local state.
                    }
                    catch
                    {
                        // Ignore network issues during untracking.
                    }
                }
            }

            if (_trackedSeries.Remove(seriesTitle))
            {
                _userSettings.RemoveExternalTracking(ServiceId, seriesTitle);
                RaiseTrackingChanged(seriesTitle, null);
                return true;
            }

            return false;
        }

        public bool TryGetTracking(string seriesTitle, out MyAnimeListTrackingInfo? trackingInfo)
        {
            return _trackedSeries.TryGetValue(seriesTitle, out trackingInfo);
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient
            {
                BaseAddress = new Uri(ApiBaseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MSCS", "1.0"));
            return client;
        }

        private void LoadExistingAuthentication()
        {
            _accessToken = _userSettings.MyAnimeListAccessToken;
            _refreshToken = _userSettings.MyAnimeListRefreshToken;
            _tokenExpiry = _userSettings.MyAnimeListAccessTokenExpiry;
            _userName = _userSettings.MyAnimeListUserName;

            if (!string.IsNullOrWhiteSpace(_accessToken))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            }

            _isAuthenticated = !string.IsNullOrWhiteSpace(_accessToken) &&
                (!_tokenExpiry.HasValue || _tokenExpiry > DateTimeOffset.UtcNow + TokenRefreshSkew);

            if (_isAuthenticated)
            {
                _userSettings.SetTrackingProviderConnection(ServiceId, true);
            }
        }

        private async Task<bool> EnsureAuthenticatedAsync(CancellationToken cancellationToken)
        {
            if (!_isAuthenticated || string.IsNullOrWhiteSpace(_accessToken))
            {
                return false;
            }

            if (!_tokenExpiry.HasValue || _tokenExpiry > DateTimeOffset.UtcNow + TokenRefreshSkew)
            {
                return true;
            }

            await _authLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_tokenExpiry.HasValue || _tokenExpiry > DateTimeOffset.UtcNow + TokenRefreshSkew)
                {
                    return true;
                }

                return await RefreshAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _authLock.Release();
            }
        }

        private async Task<bool> RefreshAccessTokenAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_refreshToken) || string.IsNullOrWhiteSpace(_clientId))
            {
                return false;
            }

            var token = await RequestTokenAsync(new Dictionary<string, string>
            {
                ["client_id"] = _clientId,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = _refreshToken
            }, cancellationToken).ConfigureAwait(false);

            if (token == null)
            {
                var wasAuthenticated = _isAuthenticated;
                InvalidateAuthentication();
                if (wasAuthenticated)
                {
                    AuthenticationChanged?.Invoke(this, EventArgs.Empty);
                }

                return false;
            }

            ApplyToken(token);
            return true;
        }

        private void ApplyToken(TokenResponse token)
        {
            _accessToken = token.AccessToken;
            if (!string.IsNullOrWhiteSpace(token.RefreshToken))
            {
                _refreshToken = token.RefreshToken;
            }

            var expiresIn = token.ExpiresIn > 0 ? TimeSpan.FromSeconds(token.ExpiresIn) : TimeSpan.FromHours(1);
            _tokenExpiry = DateTimeOffset.UtcNow.Add(expiresIn);

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            _isAuthenticated = true;
            _userSettings.MyAnimeListAccessToken = _accessToken;
            _userSettings.MyAnimeListRefreshToken = _refreshToken;
            _userSettings.MyAnimeListAccessTokenExpiry = _tokenExpiry;
            _userSettings.SetTrackingProviderConnection(ServiceId, true);
        }

        private void InvalidateAuthentication()
        {
            _isAuthenticated = false;
            _accessToken = null;
            _refreshToken = null;
            _tokenExpiry = null;
            _userName = null;
            _httpClient.DefaultRequestHeaders.Authorization = null;

            _userSettings.ClearMyAnimeListAuthentication();
            _userSettings.SetTrackingProviderConnection(ServiceId, false);
        }

        private async Task FetchProfileAsync(CancellationToken cancellationToken)
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            try
            {
                using var response = await _httpClient.GetAsync("users/@me", cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

                if (document.RootElement.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
                {
                    _userName = nameElement.GetString();
                    _userSettings.MyAnimeListUserName = _userName;
                }
            }
            catch
            {
                // Ignore failures when fetching the profile – authentication already succeeded.
            }
        }

        private async Task<TokenResponse?> RequestTokenAsync(Dictionary<string, string> parameters, CancellationToken cancellationToken)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
                {
                    Content = new FormUrlEncodedContent(parameters)
                };

                using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

                if (!document.RootElement.TryGetProperty("access_token", out var accessTokenElement) || accessTokenElement.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                var accessToken = accessTokenElement.GetString();
                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    return null;
                }

                string? refreshToken = null;
                if (document.RootElement.TryGetProperty("refresh_token", out var refreshTokenElement) && refreshTokenElement.ValueKind == JsonValueKind.String)
                {
                    refreshToken = refreshTokenElement.GetString();
                }

                var expiresIn = 0;
                if (document.RootElement.TryGetProperty("expires_in", out var expiresElement) && expiresElement.ValueKind == JsonValueKind.Number)
                {
                    expiresIn = expiresElement.GetInt32();
                }

                return new TokenResponse(accessToken!, refreshToken, expiresIn);
            }
            catch
            {
                return null;
            }
        }

        private Uri BuildAuthorizationUri(string codeChallenge, string state)
        {
            var builder = new StringBuilder();
            builder.Append(AuthorizationEndpoint)
                .Append("?response_type=code")
                .Append("&client_id=").Append(Uri.EscapeDataString(_clientId))
                .Append("&redirect_uri=").Append(Uri.EscapeDataString(RedirectUri))
                .Append("&code_challenge=").Append(Uri.EscapeDataString(codeChallenge))
                .Append("&code_challenge_method=S256")
                .Append("&state=").Append(Uri.EscapeDataString(state))
                .Append("&scope=").Append(Uri.EscapeDataString("read write"));

            return new Uri(builder.ToString());
        }

        private static string GenerateCodeVerifier()
        {
            Span<byte> buffer = stackalloc byte[32];
            RandomNumberGenerator.Fill(buffer);
            return Base64UrlEncode(buffer);
        }

        private static string GenerateState()
        {
            Span<byte> buffer = stackalloc byte[16];
            RandomNumberGenerator.Fill(buffer);
            return Base64UrlEncode(buffer);
        }

        private static string CreateCodeChallenge(string verifier)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.ASCII.GetBytes(verifier);
            var hash = sha256.ComputeHash(bytes);
            return Base64UrlEncode(hash);
        }

        private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
        {
            var base64 = Convert.ToBase64String(bytes);
            return base64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        private async Task<MyAnimeListTrackingInfo?> UpdateTrackingInternalAsync(
            string seriesTitle,
            MyAnimeListStatus? status = null,
            int? progress = null,
            double? score = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(seriesTitle) || !_trackedSeries.TryGetValue(seriesTitle, out var existing))
            {
                return null;
            }

            var normalizedProgress = progress.HasValue ? NormalizeProgress(progress) : existing.Progress;
            var sanitizedScore = ClampScore(score ?? existing.Score);

            if (existing.MediaId > 0 && await EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false))
            {
                var payload = new Dictionary<string, string>();

                if (status.HasValue)
                {
                    payload["status"] = FormatStatus(status.Value);
                }

                if (normalizedProgress.HasValue)
                {
                    payload["num_chapters_read"] = normalizedProgress.Value.ToString(CultureInfo.InvariantCulture);
                }

                if (sanitizedScore.HasValue)
                {
                    payload["score"] = Math.Round(sanitizedScore.Value).ToString(CultureInfo.InvariantCulture);
                }

                if (payload.Count > 0)
                {
                    var request = new HttpRequestMessage(new HttpMethod("PATCH"), $"manga/{existing.MediaId}/my_list_status")
                    {
                        Content = new FormUrlEncodedContent(payload)
                    };

                    try
                    {
                        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                        if (response.IsSuccessStatusCode)
                        {
                            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                            var updated = CreateTrackingInfoFromStatus(existing, document.RootElement);
                            SaveTracking(seriesTitle, updated);
                            RaiseTrackingChanged(seriesTitle, updated);
                            return updated;
                        }
                    }
                    catch
                    {
                        // Ignore network failures and fall back to local updates.
                    }
                }
            }

            var localUpdate = existing.With(
                status: status ?? existing.Status,
                progress: normalizedProgress ?? existing.Progress,
                score: sanitizedScore,
                updatedAt: DateTimeOffset.UtcNow);

            SaveTracking(seriesTitle, localUpdate);
            RaiseTrackingChanged(seriesTitle, localUpdate);
            return localUpdate;
        }

        private void SaveTracking(string seriesTitle, MyAnimeListTrackingInfo info)
        {
            _trackedSeries[seriesTitle] = info;
            var entry = new MediaTrackingEntry(
                SerializeMediaId(info.MediaId),
                info.Title,
                info.CoverImageUrl,
                FormatStatus(info.Status),
                info.Progress,
                info.Score,
                info.TotalChapters,
                info.SiteUrl,
                info.UpdatedAt);
            _userSettings.SetExternalTracking(ServiceId, seriesTitle, entry);
        }

        private static string FormatStatus(MyAnimeListStatus status)
        {
            return status switch
            {
                MyAnimeListStatus.Reading => "reading",
                MyAnimeListStatus.Completed => "completed",
                MyAnimeListStatus.OnHold => "on_hold",
                MyAnimeListStatus.Dropped => "dropped",
                MyAnimeListStatus.PlanToRead => "plan_to_read",
                _ => "reading"
            };
        }

        private static string? SerializeMediaId(int mediaId)
        {
            return mediaId > 0
                ? mediaId.ToString(CultureInfo.InvariantCulture)
                : null;
        }

        private static int ParseMediaId(string? mediaId)
        {
            if (int.TryParse(mediaId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }

            return 0;
        }

        private static MyAnimeListTrackingInfo? ConvertFromEntry(string seriesTitle, MediaTrackingEntry entry)
        {
            var status = ParseStatus(entry.Status);
            if (status == null)
            {
                return null;
            }

            return new MyAnimeListTrackingInfo(
                ParseMediaId(entry.MediaId),
                entry.Title ?? seriesTitle,
                entry.CoverImageUrl,
                status.Value,
                entry.Progress,
                entry.Score,
                entry.TotalChapters,
                entry.SiteUrl,
                entry.UpdatedAt);
        }

        private static MyAnimeListStatus? ParseStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        return status.Trim().ToLowerInvariant() switch
        {
            "reading" => MyAnimeListStatus.Reading,
            "completed" => MyAnimeListStatus.Completed,
            "onhold" => MyAnimeListStatus.OnHold,
            "on_hold" => MyAnimeListStatus.OnHold,
            "dropped" => MyAnimeListStatus.Dropped,
            "plantoread" => MyAnimeListStatus.PlanToRead,
            "plan_to_read" => MyAnimeListStatus.PlanToRead,
            _ => null
        };
    }

    private static MyAnimeListMedia? ParseMedia(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (element.TryGetProperty("node", out var nodeElement) && nodeElement.ValueKind == JsonValueKind.Object)
        {
            element = nodeElement;
        }

        if (!element.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        var id = idElement.GetInt32();
        var title = element.TryGetProperty("title", out var titleElement) ? titleElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        string? coverImage = null;
        if (element.TryGetProperty("main_picture", out var pictureElement) && pictureElement.ValueKind == JsonValueKind.Object)
        {
            if (pictureElement.TryGetProperty("large", out var largeElement) && largeElement.ValueKind == JsonValueKind.String)
            {
                coverImage = largeElement.GetString();
            }
            else if (pictureElement.TryGetProperty("medium", out var mediumElement) && mediumElement.ValueKind == JsonValueKind.String)
            {
                coverImage = mediumElement.GetString();
            }
        }

        var synopsis = element.TryGetProperty("synopsis", out var synopsisElement) ? synopsisElement.GetString() : null;
        int? chapters = element.TryGetProperty("num_chapters", out var chaptersElement) && chaptersElement.ValueKind == JsonValueKind.Number
            ? chaptersElement.GetInt32()
            : null;
        double? score = element.TryGetProperty("mean", out var scoreElement) && scoreElement.ValueKind == JsonValueKind.Number
            ? scoreElement.GetDouble()
            : null;
        var siteUrl = $"https://myanimelist.net/manga/{id}";

        return new MyAnimeListMedia(
            id,
            title!,
            synopsis,
            coverImage,
            chapters,
            score,
            siteUrl);
    }

    private static MyAnimeListTrackingInfo CreateTrackingInfo(MyAnimeListMedia media, JsonElement statusElement)
    {
        var status = ParseStatus(statusElement.TryGetProperty("status", out var statusValue) ? statusValue.GetString() : null) ?? MyAnimeListStatus.Reading;
        int? progress = statusElement.TryGetProperty("num_chapters_read", out var progressElement) && progressElement.ValueKind == JsonValueKind.Number
            ? progressElement.GetInt32()
            : null;
        double? score = statusElement.TryGetProperty("score", out var scoreElement) && scoreElement.ValueKind == JsonValueKind.Number
            ? scoreElement.GetDouble()
            : null;
        DateTimeOffset? updatedAt = null;
        if (statusElement.TryGetProperty("updated_at", out var updatedElement) && updatedElement.ValueKind == JsonValueKind.String)
        {
            var value = updatedElement.GetString();
            if (!string.IsNullOrWhiteSpace(value) && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                updatedAt = parsed;
            }
        }

        return new MyAnimeListTrackingInfo(
            media.Id,
            media.Title,
            media.CoverImageUrl,
            status,
            progress,
            score,
            media.Chapters,
            media.SiteUrl,
            updatedAt);
    }

    private static MyAnimeListTrackingInfo CreateTrackingInfoFromStatus(MyAnimeListTrackingInfo existing, JsonElement statusElement)
    {
        var status = ParseStatus(statusElement.TryGetProperty("status", out var statusValue) ? statusValue.GetString() : null) ?? existing.Status;
        int? progress = statusElement.TryGetProperty("num_chapters_read", out var progressElement) && progressElement.ValueKind == JsonValueKind.Number
            ? progressElement.GetInt32()
            : existing.Progress;
        double? score = statusElement.TryGetProperty("score", out var scoreElement) && scoreElement.ValueKind == JsonValueKind.Number
            ? scoreElement.GetDouble()
            : existing.Score;
        DateTimeOffset? updatedAt = existing.UpdatedAt;
        if (statusElement.TryGetProperty("updated_at", out var updatedElement) && updatedElement.ValueKind == JsonValueKind.String)
        {
            var value = updatedElement.GetString();
            if (!string.IsNullOrWhiteSpace(value) && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                updatedAt = parsed;
            }
        }

        return new MyAnimeListTrackingInfo(
            existing.MediaId,
            existing.Title,
            existing.CoverImageUrl,
            status,
            progress,
            score,
            existing.TotalChapters,
            existing.SiteUrl,
            updatedAt);
    }

    private void UpdateLocalTrackingFromRemote(MyAnimeListMedia media, JsonElement statusElement)
    {
        var updated = CreateTrackingInfo(media, statusElement);

        foreach (var kvp in _trackedSeries.ToArray())
        {
            if (kvp.Value.MediaId == media.Id && HasDifferences(kvp.Value, updated))
            {
                SaveTracking(kvp.Key, updated);
                RaiseTrackingChanged(kvp.Key, updated);
            }
        }
    }

    private static bool HasDifferences(MyAnimeListTrackingInfo existing, MyAnimeListTrackingInfo updated)
    {
        if (existing.Status != updated.Status)
        {
            return true;
        }

        if (existing.Progress != updated.Progress)
        {
            return true;
        }

        if (!Nullable.Equals(existing.Score, updated.Score))
        {
            return true;
        }

        if (existing.TotalChapters != updated.TotalChapters)
        {
            return true;
        }

        if (!string.Equals(existing.CoverImageUrl, updated.CoverImageUrl, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.Equals(existing.SiteUrl, updated.SiteUrl, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!Nullable.Equals(existing.UpdatedAt, updated.UpdatedAt))
        {
            return true;
        }

        return false;
    }

    private static Dictionary<MyAnimeListStatus, List<MyAnimeListMedia>> CreateStatusBuckets()
    {
        var groups = new Dictionary<MyAnimeListStatus, List<MyAnimeListMedia>>();
        foreach (var status in Enum.GetValues<MyAnimeListStatus>())
        {
            groups[status] = new List<MyAnimeListMedia>();
        }

        return groups;
    }

    private IReadOnlyDictionary<MyAnimeListStatus, IReadOnlyList<MyAnimeListMedia>> BuildGroupsFromLocal(Dictionary<MyAnimeListStatus, List<MyAnimeListMedia>> groups)
    {
        foreach (var tracking in _trackedSeries.Values)
        {
            if (!groups.TryGetValue(tracking.Status, out var bucket))
            {
                bucket = new List<MyAnimeListMedia>();
                groups[tracking.Status] = bucket;
            }

            bucket.Add(new MyAnimeListMedia(
                tracking.MediaId,
                tracking.Title,
                null,
                tracking.CoverImageUrl,
                tracking.TotalChapters,
                tracking.Score,
                tracking.SiteUrl));
        }

        return CreateReadOnlyGroups(groups);
    }

    private void AddLocalEntries(Dictionary<MyAnimeListStatus, List<MyAnimeListMedia>> groups, HashSet<int> remoteIds)
    {
        foreach (var tracking in _trackedSeries.Values)
        {
            if (tracking.MediaId > 0 && remoteIds.Contains(tracking.MediaId))
            {
                continue;
            }

            if (!groups.TryGetValue(tracking.Status, out var bucket))
            {
                bucket = new List<MyAnimeListMedia>();
                groups[tracking.Status] = bucket;
            }

            bucket.Add(new MyAnimeListMedia(
                tracking.MediaId,
                tracking.Title,
                null,
                tracking.CoverImageUrl,
                tracking.TotalChapters,
                tracking.Score,
                tracking.SiteUrl));
        }
    }

    private static IReadOnlyDictionary<MyAnimeListStatus, IReadOnlyList<MyAnimeListMedia>> CreateReadOnlyGroups(Dictionary<MyAnimeListStatus, List<MyAnimeListMedia>> groups)
    {
        var result = new Dictionary<MyAnimeListStatus, IReadOnlyList<MyAnimeListMedia>>(groups.Count);
        foreach (var kvp in groups)
        {
            result[kvp.Key] = new ReadOnlyCollection<MyAnimeListMedia>(kvp.Value);
        }

        return result;
    }

    private static double? ClampScore(double? score)
    {
        if (!score.HasValue)
        {
            return null;
        }

        return Math.Clamp(score.Value, 0, 10);
    }

    private static int? NormalizeProgress(int? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return Math.Max(0, value.Value);
    }

    private void RaiseTrackingChanged(string? seriesTitle, MyAnimeListTrackingInfo? trackingInfo)
    {
        MediaTrackingChanged?.Invoke(this, new MediaTrackingChangedEventArgs<MyAnimeListTrackingInfo>(seriesTitle, trackingInfo));
    }

    private sealed record TokenResponse(string AccessToken, string? RefreshToken, int ExpiresIn);
}
}