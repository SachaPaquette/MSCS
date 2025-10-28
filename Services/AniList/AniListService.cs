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
    public partial class AniListService : IAniListService
    {
        private const string GraphQlEndpoint = "https://graphql.anilist.co";
        private const string SaveMediaListEntryMutation = @"mutation($mediaId: Int!, $status: MediaListStatus, $progress: Int, $score: Float) {
  SaveMediaListEntry(mediaId: $mediaId, status: $status, progress: $progress, score: $score) {
    id
    status
    progress
    score
    updatedAt
    media {
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
    }
  }
}";
        private const string DeleteMediaListEntryMutation = @"mutation($id: Int) {
  DeleteMediaListEntry(id: $id) {
    deleted
  }
}";
        private const string ServiceUnavailableMessage = "AniList is currently unavailable. Please check your internet connection and try again.";
        private static readonly HttpClient SharedHttpClient = new();
        private readonly HttpClient _httpClient;
        private readonly UserSettings _userSettings;
        private string? _accessToken;
        private DateTimeOffset? _tokenExpiry;
        private string? _userName;

        public AniListService(UserSettings userSettings)
        {
            _userSettings = userSettings ?? throw new ArgumentNullException(nameof(userSettings));
            _httpClient = SharedHttpClient;

            LoadExistingAuthentication();
        }

        public string ServiceId => "AniList";

        public string DisplayName => "AniList";

        public bool IsAuthenticated => !string.IsNullOrWhiteSpace(_accessToken) && (!_tokenExpiry.HasValue || _tokenExpiry > DateTimeOffset.UtcNow);

        public string? UserName => _userName;

        public event EventHandler? AuthenticationChanged;
        public event EventHandler<AniListTrackingChangedEventArgs>? TrackingChanged;
        public event EventHandler<MediaTrackingChangedEventArgs<AniListTrackingInfo>>? MediaTrackingChanged;

        private void RaiseTrackingChanged(AniListTrackingChangedEventArgs args)
        {
            TrackingChanged?.Invoke(this, args);
            MediaTrackingChanged?.Invoke(this, args);
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
    }
}