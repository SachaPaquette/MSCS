using MSCS.Enums;
using MSCS.Helpers;
using MSCS.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MSCS.Services
{
    public partial class AniListService
    {
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
    }
}