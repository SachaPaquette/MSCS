using MSCS.Enums;
using MSCS.Helpers;
using MSCS.Interfaces;
using MSCS.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
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
        private const string SaveMediaListEntryMutation = @"mutation($mediaId: Int!, $status: MediaListStatus, $progress: Int, $score: Float) {
  SaveMediaListEntry(mediaId: $mediaId, status: $status, progress: $progress, score: $score) {
    id
    status
    progress
    score
    updatedAt
  }
}";
        private const string DeleteMediaListEntryMutation = @"mutation($id: Int) {
  DeleteMediaListEntry(id: $id) {
    deleted
  }
}";
        private const string ServiceUnavailableMessage = "AniList is currently unavailable. Please check your internet connection and try again.";
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
      format
      chapters
      siteUrl
      meanScore
      averageScore
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
      mediaListEntry {
        id
        status
        progress
        score
        updatedAt
      }
    }
  }
}";

            var variables = new
            {
                search = query
            };

            using var document = await TrySendGraphQlRequestAsync(gqlQuery, variables, cancellationToken).ConfigureAwait(false);
            if (document == null)
            {
                throw new InvalidOperationException(ServiceUnavailableMessage);
            }
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
                    var parsed = ParseMedia(media);
                    if (parsed != null)
                    {
                        results.Add(parsed);
                    }
                }
            }

            return results;
        }

        public async Task<IReadOnlyList<AniListMedia>> GetTopSeriesAsync(
                 AniListRecommendationCategory category,
                 int perPage = 12,
                 CancellationToken cancellationToken = default)
        {
            if (perPage <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(perPage));
            }

            var sort = category switch
            {
                AniListRecommendationCategory.Trending => new[] { "TRENDING_DESC" },
                AniListRecommendationCategory.NewReleases => new[] { "START_DATE_DESC", "POPULARITY_DESC" },
                AniListRecommendationCategory.StaffPicks => new[] { "SCORE_DESC", "POPULARITY_DESC" },
                _ => new[] { "POPULARITY_DESC" }
            };

            var country = category switch
            {
                AniListRecommendationCategory.Manga => "JP",
                AniListRecommendationCategory.Manhwa => "KR",
                _ => null
            };

            string[]? statusIn = category switch
            {
                AniListRecommendationCategory.NewReleases => new[] { "RELEASING", "NOT_YET_RELEASED" },
                AniListRecommendationCategory.StaffPicks => new[] { "RELEASING", "FINISHED" },
                _ => null
            };

            int? minimumScore = category switch
            {
                AniListRecommendationCategory.StaffPicks => 80,
                _ => null
            };

            var queryBuilder = new StringBuilder();
            queryBuilder.AppendLine("query ($perPage: Int!) {");
            queryBuilder.AppendLine("  Page(perPage: $perPage) {");
            queryBuilder.Append("    media(");

            var argumentParts = new List<string>
            {
                "type: MANGA",
                $"sort: [{string.Join(", ", sort)}]"
            };

            if (!string.IsNullOrEmpty(country))
            {
                argumentParts.Add($"countryOfOrigin: {country}");
            }

            if (statusIn is { Length: > 0 })
            {
                argumentParts.Add($"status_in: [{string.Join(", ", statusIn)}]");
            }

            if (minimumScore.HasValue)
            {
                argumentParts.Add($"averageScore_greater: {minimumScore.Value}");
            }

            argumentParts.Add("isAdult: false");

            queryBuilder.Append(string.Join(", ", argumentParts));
            queryBuilder.AppendLine(") {");
            queryBuilder.AppendLine("      id");
            queryBuilder.AppendLine("      status");
            queryBuilder.AppendLine("      format");
            queryBuilder.AppendLine("      chapters");
            queryBuilder.AppendLine("      siteUrl");
            queryBuilder.AppendLine("      meanScore");
            queryBuilder.AppendLine("      averageScore");
            queryBuilder.AppendLine("      title {");
            queryBuilder.AppendLine("        romaji");
            queryBuilder.AppendLine("        english");
            queryBuilder.AppendLine("        native");
            queryBuilder.AppendLine("      }");
            queryBuilder.AppendLine("      coverImage {");
            queryBuilder.AppendLine("        large");
            queryBuilder.AppendLine("      }");
            queryBuilder.AppendLine("      bannerImage");
            queryBuilder.AppendLine("      startDate {");
            queryBuilder.AppendLine("        year");
            queryBuilder.AppendLine("        month");
            queryBuilder.AppendLine("        day");
            queryBuilder.AppendLine("      }");
            queryBuilder.AppendLine("      mediaListEntry {");
            queryBuilder.AppendLine("        id");
            queryBuilder.AppendLine("        status");
            queryBuilder.AppendLine("        progress");
            queryBuilder.AppendLine("        score");
            queryBuilder.AppendLine("        updatedAt");
            queryBuilder.AppendLine("      }");
            queryBuilder.AppendLine("    }");
            queryBuilder.AppendLine("  }");
            queryBuilder.AppendLine("}");

            var gqlQuery = queryBuilder.ToString();

            var variables = new
            {
                perPage = Math.Min(perPage, 50)
            };


            using var document = await TrySendGraphQlRequestAsync(gqlQuery, variables, cancellationToken).ConfigureAwait(false);
            if (document == null)
            {
                throw new InvalidOperationException(ServiceUnavailableMessage);
            }
            if (!document.RootElement.TryGetProperty("data", out var dataElement))
            {
                return Array.Empty<AniListMedia>();
            }

            if (!dataElement.TryGetProperty("Page", out var pageElement) ||
                !pageElement.TryGetProperty("media", out var mediaArray) ||
                mediaArray.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<AniListMedia>();
            }

            var results = new List<AniListMedia>();
            foreach (var media in mediaArray.EnumerateArray())
            {
                var parsed = ParseMedia(media);
                if (parsed != null)
                {
                    results.Add(parsed);
                }
            }

            return results;
        }

        public async Task<AniListTrackingInfo> TrackSeriesAsync(
            string mangaTitle,
            AniListMedia media,
            AniListMediaListStatus? status = null,
            int? progress = null,
            double? score = null,
            CancellationToken cancellationToken = default)
        {
            if (media == null) throw new ArgumentNullException(nameof(media));
            EnsureAuthenticated();

            var desiredStatus = status ?? AniListMediaListStatus.Current;
            var info = await SaveMediaListEntryAsync(
                mangaTitle,
                media.Id,
                media.DisplayTitle,
                media.CoverImageUrl,
                desiredStatus,
                progress,
                score,
                cancellationToken).ConfigureAwait(false);

            if (info != null)
            {
                return info;
            }

            var fallback = new AniListTrackingInfo(
                media.Id,
                media.DisplayTitle,
                media.CoverImageUrl,
                desiredStatus,
                progress,
                score,
                media.Chapters,
                media.SiteUrl,
                DateTimeOffset.UtcNow,
                null);
            _userSettings.SetAniListTracking(mangaTitle, fallback);
            TrackingChanged?.Invoke(this, EventArgs.Empty);
            return fallback;
        }


        public async Task UpdateProgressAsync(string mangaTitle, int progress, CancellationToken cancellationToken = default)
        {
            if (progress <= 0)
            {
                return;
            }

            if (!_userSettings.TryGetAniListTracking(mangaTitle, out var trackingInfo) || trackingInfo == null)
            {
                return;
            }

            var updated = await SaveMediaListEntryAsync(
                mangaTitle,
                trackingInfo.MediaId,
                trackingInfo.Title,
                trackingInfo.CoverImageUrl,
                null,
                progress,
                null,
                cancellationToken).ConfigureAwait(false);

            if (updated == null)
            {
                var fallback = new AniListTrackingInfo(
                    trackingInfo.MediaId,
                    trackingInfo.Title,
                    trackingInfo.CoverImageUrl,
                    trackingInfo.Status,
                    progress,
                    trackingInfo.Score,
                    trackingInfo.TotalChapters,
                    trackingInfo.SiteUrl,
                    DateTimeOffset.UtcNow,
                    trackingInfo.MediaListEntryId);
                _userSettings.SetAniListTracking(mangaTitle, fallback);
                TrackingChanged?.Invoke(this, EventArgs.Empty);
            }
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


        public async Task<AniListTrackingInfo?> UpdateTrackingAsync(
            string mangaTitle,
            AniListMediaListStatus? status = null,
            int? progress = null,
            double? score = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(mangaTitle))
            {
                return null;
            }

            if (!_userSettings.TryGetAniListTracking(mangaTitle, out var trackingInfo) || trackingInfo == null)
            {
                return null;
            }


            var updated = await SaveMediaListEntryAsync(
                mangaTitle,
                trackingInfo.MediaId,
                trackingInfo.Title,
                trackingInfo.CoverImageUrl,
                status,
                progress,
                score,
                cancellationToken).ConfigureAwait(false);

            if (updated != null)
            {
                return updated;
            }

            var fallback = new AniListTrackingInfo(
                trackingInfo.MediaId,
                trackingInfo.Title,
                trackingInfo.CoverImageUrl,
                status ?? trackingInfo.Status,
                progress ?? trackingInfo.Progress,
                score ?? trackingInfo.Score,
                trackingInfo.TotalChapters,
                trackingInfo.SiteUrl,
                DateTimeOffset.UtcNow,
                trackingInfo.MediaListEntryId);
            _userSettings.SetAniListTracking(mangaTitle, fallback);
            TrackingChanged?.Invoke(this, EventArgs.Empty);
            return fallback;
        }

        public async Task<bool> UntrackSeriesAsync(string mangaTitle, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(mangaTitle))
            {
                return false;
            }

            if (!_userSettings.TryGetAniListTracking(mangaTitle, out var trackingInfo) || trackingInfo == null)
            {
                return false;
            }

            EnsureAuthenticated();
            var entryId = trackingInfo.MediaListEntryId;
            if (entryId == null)
            {
                var refreshed = await RefreshTrackingAsync(mangaTitle, cancellationToken).ConfigureAwait(false);
                entryId = refreshed?.MediaListEntryId;
                if (entryId == null)
                {
                    _userSettings.RemoveAniListTracking(mangaTitle);
                    TrackingChanged?.Invoke(this, EventArgs.Empty);
                    return true;
                }
            }

            var variables = new
            {
                id = entryId
            };

            using var document = await TrySendGraphQlRequestAsync(DeleteMediaListEntryMutation, variables, cancellationToken).ConfigureAwait(false);
            if (document == null)
            {
                Debug.WriteLine("AniList: Unable to remove tracking entry due to missing connection. Removing local entry only.");
            }
            _userSettings.RemoveAniListTracking(mangaTitle);
            TrackingChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public async Task<IReadOnlyDictionary<AniListMediaListStatus, IReadOnlyList<AniListMedia>>> GetUserListsAsync(
    CancellationToken cancellationToken = default)
        {
            if (!IsAuthenticated)
            {
                return new Dictionary<AniListMediaListStatus, IReadOnlyList<AniListMedia>>();
            }

            if (string.IsNullOrWhiteSpace(_userName))
            {
                await FetchViewerAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(_userName))
                {
                    return new Dictionary<AniListMediaListStatus, IReadOnlyList<AniListMedia>>();
                }
            }

            const string query = @"query ($userName: String) {
  MediaListCollection(type: MANGA, userName: $userName) {
    lists {
      status
      isCustomList
      entries {
        status
        progress
        score
        updatedAt
        media {
          id
          status
          format
          chapters
          siteUrl
          meanScore
          averageScore
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
          mediaListEntry {
            id
            status
            progress
            score
            updatedAt
          }
        }
      }
    }
  }
}";

            var variables = new
            {
                userName = _userName
            };

            using var document = await TrySendGraphQlRequestAsync(query, variables, cancellationToken).ConfigureAwait(false);
            if (document == null)
            {
                throw new InvalidOperationException(ServiceUnavailableMessage);
            }
            if (!document.RootElement.TryGetProperty("data", out var dataElement) ||
                !dataElement.TryGetProperty("MediaListCollection", out var collectionElement) ||
                collectionElement.ValueKind != JsonValueKind.Object)
            {
                return new Dictionary<AniListMediaListStatus, IReadOnlyList<AniListMedia>>();
            }

            var groups = Enum.GetValues<AniListMediaListStatus>()
                .ToDictionary(status => status, _ => new List<AniListMedia>());

            if (collectionElement.TryGetProperty("lists", out var listsElement) &&
                listsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var list in listsElement.EnumerateArray())
                {
                    if (list.TryGetProperty("isCustomList", out var isCustomList) &&
                        isCustomList.ValueKind == JsonValueKind.True)
                    {
                        continue;
                    }

                    var statusValue = list.TryGetProperty("status", out var statusElement) &&
                                       statusElement.ValueKind == JsonValueKind.String
                        ? AniListFormatting.FromApiValue(statusElement.GetString())
                        : null;

                    if (statusValue == null)
                    {
                        continue;
                    }

                    if (!groups.TryGetValue(statusValue.Value, out var targetGroup))
                    {
                        targetGroup = new List<AniListMedia>();
                        groups[statusValue.Value] = targetGroup;
                    }

                    if (!list.TryGetProperty("entries", out var entriesElement) ||
                        entriesElement.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var entry in entriesElement.EnumerateArray())
                    {
                        if (!entry.TryGetProperty("media", out var mediaElement) ||
                            mediaElement.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        var media = ParseMedia(mediaElement);
                        if (media == null)
                        {
                            continue;
                        }

                        var entryStatus = entry.TryGetProperty("status", out var entryStatusElement) &&
                                          entryStatusElement.ValueKind == JsonValueKind.String
                            ? AniListFormatting.FromApiValue(entryStatusElement.GetString()) ?? statusValue
                            : statusValue;

                        int? progress = null;
                        if (entry.TryGetProperty("progress", out var progressElement) &&
                            progressElement.ValueKind == JsonValueKind.Number)
                        {
                            var progressValue = progressElement.GetInt32();
                            if (progressValue > 0)
                            {
                                progress = progressValue;
                            }
                        }

                        double? score = null;
                        if (entry.TryGetProperty("score", out var scoreElement) &&
                            scoreElement.ValueKind == JsonValueKind.Number)
                        {
                            var scoreValue = scoreElement.GetDouble();
                            if (scoreValue > 0)
                            {
                                score = scoreValue;
                            }
                        }

                        DateTimeOffset? updatedAt = null;
                        if (entry.TryGetProperty("updatedAt", out var updatedElement) &&
                            updatedElement.ValueKind == JsonValueKind.Number)
                        {
                            var seconds = updatedElement.GetInt64();
                            if (seconds > 0)
                            {
                                updatedAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
                            }
                        }

                        var merged = new AniListMedia
                        {
                            Id = media.Id,
                            RomajiTitle = media.RomajiTitle,
                            EnglishTitle = media.EnglishTitle,
                            NativeTitle = media.NativeTitle,
                            Status = media.Status,
                            CoverImageUrl = media.CoverImageUrl,
                            BannerImageUrl = media.BannerImageUrl,
                            StartDateText = media.StartDateText,
                            Format = media.Format,
                            Chapters = media.Chapters,
                            SiteUrl = media.SiteUrl,
                            AverageScore = media.AverageScore,
                            MeanScore = media.MeanScore,
                            UserStatus = entryStatus ?? media.UserStatus,
                            UserProgress = progress ?? media.UserProgress,
                            UserScore = score ?? media.UserScore,
                            UserUpdatedAt = updatedAt ?? media.UserUpdatedAt
                        };

                        targetGroup.Add(merged);
                    }
                }
            }

            var result = new Dictionary<AniListMediaListStatus, IReadOnlyList<AniListMedia>>();
            foreach (var kvp in groups)
            {
                var items = kvp.Value;
                items.Sort((a, b) =>
                {
                    var left = a.UserUpdatedAt ?? DateTimeOffset.MinValue;
                    var right = b.UserUpdatedAt ?? DateTimeOffset.MinValue;
                    return right.CompareTo(left);
                });
                result[kvp.Key] = new ReadOnlyCollection<AniListMedia>(items);
            }

            return result;
        }

        public async Task<AniListTrackingInfo?> RefreshTrackingAsync(string mangaTitle, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(mangaTitle))
            {
                return null;
            }

            if (!_userSettings.TryGetAniListTracking(mangaTitle, out var trackingInfo) || trackingInfo == null)
            {
                return null;
            }

            EnsureAuthenticated();
            var refreshed = await FetchTrackingInfoByMediaIdAsync(
                trackingInfo.MediaId,
                trackingInfo.Title,
                trackingInfo.CoverImageUrl,
                cancellationToken).ConfigureAwait(false);

            if (refreshed != null)
            {
                _userSettings.SetAniListTracking(mangaTitle, refreshed);
                TrackingChanged?.Invoke(this, EventArgs.Empty);
            }

            return refreshed;
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
            TrackingChanged?.Invoke(this, EventArgs.Empty);
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

        private static AniListMedia? ParseMedia(JsonElement media)
        {
            if (!media.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.Number)
            {
                return null;
            }

            var id = idElement.GetInt32();
            if (id == 0)
            {
                return null;
            }

            var titleElement = media.TryGetProperty("title", out var title) ? title : default;
            var romaji = titleElement.ValueKind == JsonValueKind.Object && titleElement.TryGetProperty("romaji", out var romajiElement)
                ? romajiElement.GetString()
                : null;
            var english = titleElement.ValueKind == JsonValueKind.Object && titleElement.TryGetProperty("english", out var englishElement)
                ? englishElement.GetString()
                : null;
            var nativeTitle = titleElement.ValueKind == JsonValueKind.Object && titleElement.TryGetProperty("native", out var nativeElement)
                ? nativeElement.GetString()
                : null;

            var status = media.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null;
            var cover = media.TryGetProperty("coverImage", out var coverElement) &&
                        coverElement.TryGetProperty("large", out var coverUrl)
                ? coverUrl.GetString()
                : null;
            var banner = media.TryGetProperty("bannerImage", out var bannerElement) ? bannerElement.GetString() : null;
            var startDate = media.TryGetProperty("startDate", out var startDateElement)
                ? FormatDate(startDateElement)
                : null;
            var format = media.TryGetProperty("format", out var formatElement) ? formatElement.GetString() : null;
            var chapters = media.TryGetProperty("chapters", out var chaptersElement) && chaptersElement.ValueKind == JsonValueKind.Number
                ? chaptersElement.GetInt32()
                : (int?)null;
            var siteUrl = media.TryGetProperty("siteUrl", out var siteUrlElement) ? siteUrlElement.GetString() : null;
            var meanScore = media.TryGetProperty("meanScore", out var meanScoreElement) && meanScoreElement.ValueKind == JsonValueKind.Number
                ? meanScoreElement.GetDouble()
                : (double?)null;
            var averageScore = media.TryGetProperty("averageScore", out var averageScoreElement) && averageScoreElement.ValueKind == JsonValueKind.Number
                ? averageScoreElement.GetDouble()
                : (double?)null;

            AniListMediaListStatus? userStatus = null;
            int? userProgress = null;
            double? userScore = null;
            DateTimeOffset? userUpdatedAt = null;
            if (media.TryGetProperty("mediaListEntry", out var entryElement) && entryElement.ValueKind == JsonValueKind.Object)
            {
                userStatus = AniListFormatting.FromApiValue(entryElement.TryGetProperty("status", out var entryStatus) ? entryStatus.GetString() : null);

                if (entryElement.TryGetProperty("progress", out var entryProgress) && entryProgress.ValueKind == JsonValueKind.Number)
                {
                    var progressValue = entryProgress.GetInt32();
                    if (progressValue > 0)
                    {
                        userProgress = progressValue;
                    }
                }

                if (entryElement.TryGetProperty("score", out var entryScore) && entryScore.ValueKind == JsonValueKind.Number)
                {
                    var scoreValue = entryScore.GetDouble();
                    if (scoreValue > 0)
                    {
                        userScore = scoreValue;
                    }
                }

                if (entryElement.TryGetProperty("updatedAt", out var entryUpdatedAt) && entryUpdatedAt.ValueKind == JsonValueKind.Number)
                {
                    var seconds = entryUpdatedAt.GetInt64();
                    if (seconds > 0)
                    {
                        userUpdatedAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
                    }
                }
            }

            return new AniListMedia
            {
                Id = id,
                RomajiTitle = romaji,
                EnglishTitle = english,
                NativeTitle = nativeTitle,
                Status = status,
                CoverImageUrl = cover,
                BannerImageUrl = banner,
                StartDateText = startDate,
                Format = format,
                Chapters = chapters,
                SiteUrl = siteUrl,
                MeanScore = meanScore,
                AverageScore = averageScore,
                UserStatus = userStatus,
                UserProgress = userProgress,
                UserScore = userScore,
                UserUpdatedAt = userUpdatedAt
            };
        }

        private static string? FormatDate(JsonElement startDateElement)
        {
            var year = GetOptionalInt32(startDateElement, "year");
            var month = GetOptionalInt32(startDateElement, "month");
            var day = GetOptionalInt32(startDateElement, "day");

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

        private static int? GetOptionalInt32(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property))
            {
                return null;
            }

            return property.ValueKind == JsonValueKind.Number
                ? property.GetInt32()
                : (int?)null;
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

        private async Task<JsonDocument?> TrySendGraphQlRequestAsync(string query, object variables, CancellationToken cancellationToken)
        {
            try
            {
                return await SendGraphQlRequestAsync(query, variables, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                Debug.WriteLine($"AniList request timed out: {ex.Message}");
                return null;
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"AniList request failed: {ex.Message}");
                return null;
            }
        }

        private void EnsureAuthenticated()
        {
            if (!IsAuthenticated)
            {
                throw new InvalidOperationException("AniList authentication is required for this operation.");
            }
        }
        private async Task<AniListTrackingInfo?> SaveMediaListEntryAsync(
            string mangaTitle,
            int mediaId,
            string fallbackTitle,
            string? fallbackCoverImage,
            AniListMediaListStatus? status,
            int? progress,
            double? score,
            CancellationToken cancellationToken)
        {
            EnsureAuthenticated();

            var variables = new Dictionary<string, object?>
            {
                ["mediaId"] = mediaId
            };

            if (status.HasValue)
            {
                variables["status"] = status.Value.ToApiValue();
            }

            if (progress.HasValue)
            {
                variables["progress"] = progress.Value;
            }

            if (score.HasValue)
            {
                variables["score"] = score.Value;
            }

            using var document = await TrySendGraphQlRequestAsync(SaveMediaListEntryMutation, variables, cancellationToken).ConfigureAwait(false);
            if (document == null)
            {
                return null;
            }
            var refreshed = await FetchTrackingInfoByMediaIdAsync(mediaId, fallbackTitle, fallbackCoverImage, cancellationToken).ConfigureAwait(false);
            if (refreshed != null)
            {
                _userSettings.SetAniListTracking(mangaTitle, refreshed);
                TrackingChanged?.Invoke(this, EventArgs.Empty);
            }

            return refreshed;
        }

        private async Task<AniListTrackingInfo?> FetchTrackingInfoByMediaIdAsync(int mediaId, string fallbackTitle, string? fallbackCover, CancellationToken cancellationToken)
        {
            const string query = @"query($mediaId: Int!) {
  Media(id: $mediaId, type: MANGA) {
    id
    title {
      romaji
      english
      native
    }
    coverImage {
      large
    }
    siteUrl
    chapters
    mediaListEntry {
      id
      status
      progress
      score
      updatedAt
    }
  }
}";

            var variables = new
            {
                mediaId
            };

            using var document = await TrySendGraphQlRequestAsync(query, variables, cancellationToken).ConfigureAwait(false);
            if (document == null)
            {
                return null;
            }
            if (!document.RootElement.TryGetProperty("data", out var dataElement) ||
                !dataElement.TryGetProperty("Media", out var mediaElement) ||
                mediaElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var titleElement = mediaElement.TryGetProperty("title", out var titleJson) ? titleJson : default;
            var title = ResolveTitle(titleElement, fallbackTitle);
            var cover = mediaElement.TryGetProperty("coverImage", out var coverElement) &&
                        coverElement.TryGetProperty("large", out var coverUrl)
                ? coverUrl.GetString()
                : fallbackCover;
            var siteUrl = mediaElement.TryGetProperty("siteUrl", out var siteUrlElement) ? siteUrlElement.GetString() : null;
            var chapters = mediaElement.TryGetProperty("chapters", out var chaptersElement) && chaptersElement.ValueKind == JsonValueKind.Number
                ? chaptersElement.GetInt32()
                : (int?)null;

            AniListMediaListStatus? status = null;
            int? progress = null;
            double? score = null;
            DateTimeOffset? updatedAt = null;
            int? entryId = null;
            if (mediaElement.TryGetProperty("mediaListEntry", out var entryElement) && entryElement.ValueKind == JsonValueKind.Object)
            {
                entryId = entryElement.TryGetProperty("id", out var idElement) ? idElement.GetInt32() : (int?)null;
                status = AniListFormatting.FromApiValue(entryElement.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null);
                if (entryElement.TryGetProperty("progress", out var progressElement) && progressElement.ValueKind == JsonValueKind.Number)
                {
                    var progressValue = progressElement.GetInt32();
                    if (progressValue > 0)
                    {
                        progress = progressValue;
                    }
                }

                if (entryElement.TryGetProperty("score", out var scoreElement) && scoreElement.ValueKind == JsonValueKind.Number)
                {
                    var scoreValue = scoreElement.GetDouble();
                    if (scoreValue > 0)
                    {
                        score = scoreValue;
                    }
                }

                if (entryElement.TryGetProperty("updatedAt", out var updatedElement) && updatedElement.ValueKind == JsonValueKind.Number)
                {
                    var seconds = updatedElement.GetInt64();
                    if (seconds > 0)
                    {
                        updatedAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
                    }
                }
            }

            return new AniListTrackingInfo(
                mediaId,
                title,
                cover,
                status,
                progress,
                score,
                chapters,
                siteUrl,
                updatedAt,
                entryId);
        }

        private static string ResolveTitle(JsonElement titleElement, string fallbackTitle)
        {
            if (titleElement.ValueKind != JsonValueKind.Object)
            {
                return fallbackTitle;
            }

            var english = titleElement.TryGetProperty("english", out var englishElement) ? englishElement.GetString() : null;
            if (!string.IsNullOrWhiteSpace(english))
            {
                return english!;
            }

            var romaji = titleElement.TryGetProperty("romaji", out var romajiElement) ? romajiElement.GetString() : null;
            if (!string.IsNullOrWhiteSpace(romaji))
            {
                return romaji!;
            }

            var nativeTitle = titleElement.TryGetProperty("native", out var nativeElement) ? nativeElement.GetString() : null;
            if (!string.IsNullOrWhiteSpace(nativeTitle))
            {
                return nativeTitle!;
            }

            return fallbackTitle;
        }


        private sealed record GraphQlRequest(string query, object variables);
    }
}