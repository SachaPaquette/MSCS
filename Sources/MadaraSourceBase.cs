using HtmlAgilityPack;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;
using MSCS.Interfaces;
using MSCS.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace MSCS.Sources
{
    /// <summary>
    /// Provides a reusable implementation of <see cref="IMangaSource"/> for sites powered by the
    /// Madara theme. The majority of popular aggregators share the same markup and AJAX contracts,
    /// so new sources can simply provide a <see cref="MadaraSourceSettings"/> instance instead of
    /// duplicating parsing logic.
    /// </summary>
    public abstract class MadaraSourceBase : IMangaSource
    {
        private static readonly Regex ChapterNumberRegex = new("\\d+(?:\\.\\d+)?", RegexOptions.Compiled);

        protected static readonly SocketsHttpHandler Handler = new()
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 8
        };

        private readonly HttpClient _http;

        protected MadaraSourceBase(MadaraSourceSettings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _http = new HttpClient(Handler, disposeHandler: false)
            {
                Timeout = settings.RequestTimeout
            };

            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/118.0.0.0 Safari/537.36");
            _http.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        }

        protected MadaraSourceSettings Settings { get; }

        protected HttpClient Http => _http;

        public virtual async Task<List<Manga>> SearchMangaAsync(string query, CancellationToken ct = default)
        {
            var url = BuildSearchUri(query);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            ConfigureRequest(request);

            try
            {
                using var resp = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    return new List<Manga>();
                }

                var html = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return ParseMangaHtml(html);
            }
            catch (HttpRequestException)
            {

                return new List<Manga>();
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                return new List<Manga>();
            }
        }

        protected List<Manga> ParseMangaHtmlCore(string html)
        {
            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);

            var results = new List<Manga>();
            ExtractMangaFromDocument(doc, results);
            return results;
        }

        protected virtual List<Manga> ParseMangaHtml(string html)
        {
            return ParseMangaHtmlCore(html);
        }

        public virtual List<Manga> ParseMangaFromHtmlFragment(string htmlFragment)
        {
            return ParseMangaHtmlCore(htmlFragment);
        }

        public virtual async Task<List<Chapter>> GetChaptersAsync(string mangaUrl, CancellationToken ct = default)
        {
            var uri = ToAbsolute(mangaUrl);

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            ConfigureRequest(request);

            using var resp = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var html = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var nodes = doc.DocumentNode.SelectNodes(Settings.ChapterListXPath);
            if (nodes == null)
            {
                return new List<Chapter>();
            }

            var chapters = nodes.Select(a => new Chapter
            {
                Title = WebUtility.HtmlDecode(a.InnerText.Trim()),
                Url = a.GetAttributeValue("href", string.Empty)
            }).ToList();

            chapters = chapters
                .OrderBy(ch =>
                {
                    var match = ChapterNumberRegex.Match(ch.Title);
                    return match.Success ? double.Parse(match.Value, CultureInfo.InvariantCulture) : double.NaN;
                })
                .ThenBy(ch => ch.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return chapters;
        }

        public virtual async Task<string> LoadMoreSeriesHtmlAsync(string query, int page, CancellationToken ct = default)
        {
            var url = new Uri(Settings.BaseUri, Settings.LoadMorePath);

            using var content = new FormUrlEncodedContent(BuildLoadMorePayload(query, page));
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = content
            };
            ConfigureRequest(request);

            using var resp = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }

        public virtual async Task<IReadOnlyList<ChapterImage>> FetchChapterImages(string chapterUrl, CancellationToken ct = default)
        {
            var uri = ToAbsolute(chapterUrl);

            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            ConfigureRequest(req);
            req.Headers.Referrer = uri;

            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var html = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);

            var imageNodes = doc.DocumentNode.SelectNodes(Settings.ReaderImageXPath);
            if (imageNodes == null || imageNodes.Count == 0)
                return Array.Empty<ChapterImage>();

            var items = imageNodes
                .Select((img, idx) =>
                {
                    var picked = this.PickBestImage(img);
                    return new
                    {
                        Node = img,
                        Picked = picked,
                        OriginalIndex = idx,
                        Key = ComputePageKey(img, idx, picked) 
                    };
                })
                .GroupBy(x => NormalizeUrlForDedup(x.Picked ?? string.Empty), StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var best = g.OrderByDescending(x => x.Key.HasValue)
                                .ThenBy(x => x.Key ?? int.MaxValue)
                                .ThenBy(x => x.OriginalIndex)
                                .First();
                    return best;
                })
                .OrderBy(x => x.Key ?? int.MaxValue)
                .ThenBy(x => x.OriginalIndex)
                .ToList();

            if (items.Count == 0)
                return Array.Empty<ChapterImage>();

            var list = new List<ChapterImage>(items.Count);
            var referer = uri.ToString();
            var cookieHeader = Handler.CookieContainer.GetCookieHeader(uri);

            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Picked))
                    continue;

                var abs = ToAbsolute(item.Picked.Trim()).ToString();

                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Referer"] = referer
                };
                if (!string.IsNullOrWhiteSpace(cookieHeader))
                    headers["Cookie"] = cookieHeader;

                list.Add(new ChapterImage
                {
                    ImageUrl = abs,
                    Headers = headers
                });
            }

            return list;
        }

        private static int? ComputePageKey(HtmlNode img, int fallbackIndex, string? pickedUrl)
        {
            if (TryGetIntAttr(img, "data-page", out var v)) return v;
            if (TryGetIntAttr(img, "data-index", out v)) return v;
            if (TryGetIntAttr(img, "data-id", out v)) return v;
            if (TryGetIntAttr(img, "data-pageindex", out v)) return v;
            if (TryGetIntAttr(img, "data-number", out v)) return v;

            var alt = img.GetAttributeValue("alt", null);
            if (TryExtractTrailingNumber(alt, out v)) return v;

            var id = img.GetAttributeValue("id", null);
            if (TryExtractTrailingNumber(id, out v)) return v;

            if (TryExtractTrailingNumber(pickedUrl, out v)) return v;

            return null;
        }

        private static bool TryGetIntAttr(HtmlNode n, string name, out int value)
        {
            value = default;
            var s = n.GetAttributeValue(name, null);
            return !string.IsNullOrWhiteSpace(s) &&
                   int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryExtractTrailingNumber(string? s, out int value)
        {
            value = default;
            if (string.IsNullOrWhiteSpace(s)) return false;

            for (int i = s.Length - 1; i >= 0; i--)
            {
                if (char.IsDigit(s[i]))
                {
                    int end = i;
                    while (i >= 0 && char.IsDigit(s[i])) i--;
                    var span = s[(i + 1)..(end + 1)];
                    return int.TryParse(span, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
                }
            }
            return false;
        }

        private static string NormalizeUrlForDedup(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;
            try
            {
                var u = new Uri(url, UriKind.RelativeOrAbsolute);
                if (!u.IsAbsoluteUri) return url;
                var b = new UriBuilder(u) { Query = string.Empty, Fragment = string.Empty };
                b.Path = (b.Path ?? string.Empty).ToLowerInvariant();
                return b.Uri.ToString();
            }
            catch
            {
                return url;
            }
        }

        protected virtual void ConfigureRequest(HttpRequestMessage request)
        {
            if (Settings.AdditionalRequestHeaders.Count == 0)
            {
                return;
            }

            foreach (var pair in Settings.AdditionalRequestHeaders)
            {
                request.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
            }
        }

        protected virtual Uri BuildSearchUri(string query)
        {
            var formatted = string.Format(CultureInfo.InvariantCulture, Settings.SearchTemplate, Uri.EscapeDataString(query));
            return new Uri(Settings.BaseUri, formatted);
        }

        protected virtual Dictionary<string, string> BuildLoadMorePayload(string query, int page)
        {
            return new Dictionary<string, string>
            {
                { "action", Settings.LoadMoreAction },
                { "page", page.ToString(CultureInfo.InvariantCulture) },
                { "template", Settings.LoadMoreTemplate },
                { "vars[s]", query },
                { "vars[orderby]", string.Empty },
                { "vars[paged]", page.ToString(CultureInfo.InvariantCulture) },
                { "vars[template]", Settings.LoadMoreTemplateName },
                { "vars[post_type]", Settings.PostType },
                { "vars[post_status]", "publish" },
                { "vars[manga_archives_item_layout]", Settings.LoadMoreItemLayout }
            };
        }

        private void ExtractMangaFromDocument(HtmlDocument doc, List<Manga> results)
       {
            var mangaNodes = doc.DocumentNode.SelectNodes(Settings.MangaContainerXPath);
            if (mangaNodes == null)
          {
                return;
            }

            foreach (var node in mangaNodes)
            {
                var titleAnchor = node.SelectSingleNode(Settings.TitleXPath);
                if (titleAnchor == null)
                {
                    continue;
                }

                var imageNode = node.Descendants("img")
                    .FirstOrDefault(img => img.Ancestors("div").Any(div => div.GetAttributeValue("class", string.Empty).Contains(Settings.CoverContainerClassFragment)));

                var coverRaw = imageNode != null ? PickBestImage(imageNode) : null;

                var manga = new Manga
                {
                    Title = WebUtility.HtmlDecode(titleAnchor.InnerText.Trim()),
                    Url = titleAnchor.GetAttributeValue("href", string.Empty),
                    CoverImageUrl = string.IsNullOrWhiteSpace(coverRaw) ? null : ToAbsolute(coverRaw).ToString()
                };

                PopulateMetadata(node, manga);
                PopulateLatestChapter(node, manga);
                PopulateUpdateInfo(node, manga);
                PopulateRating(node, manga);

                results.Add(manga);
            }
        }

        protected virtual void PopulateMetadata(HtmlNode node, Manga manga)
        {
            if (string.IsNullOrWhiteSpace(Settings.MetadataSectionXPath))
            {
                return;
            }

            var summaryNodes = node.SelectNodes(Settings.MetadataSectionXPath);
            if (summaryNodes == null)
            {
                return;
            }

            foreach (var summaryNode in summaryNodes)
            {
                var parent = summaryNode.ParentNode?.ParentNode;
                if (parent == null)
                {
                    continue;
                }

                var cls = parent.GetAttributeValue("class", string.Empty);
                var text = WebUtility.HtmlDecode(summaryNode.InnerText?.Trim() ?? string.Empty);

                if (cls.Contains("mg_alternative", StringComparison.OrdinalIgnoreCase))
                {
                    manga.AlternativeTitles = text.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).ToList();
                }
                else if (cls.Contains("mg_author", StringComparison.OrdinalIgnoreCase))
                {
                    manga.Authors = summaryNode.Descendants("a").Select(a => WebUtility.HtmlDecode(a.InnerText.Trim())).ToList();
                }
                else if (cls.Contains("mg_artists", StringComparison.OrdinalIgnoreCase))
                {
                    manga.Artists = summaryNode.Descendants("a").Select(a => WebUtility.HtmlDecode(a.InnerText.Trim())).ToList();
                }
                else if (cls.Contains("mg_genres", StringComparison.OrdinalIgnoreCase))
                {
                    manga.Genres = summaryNode.Descendants("a").Select(a => WebUtility.HtmlDecode(a.InnerText.Trim())).ToList();
                }
                else if (cls.Contains("mg_status", StringComparison.OrdinalIgnoreCase))
                {
                    manga.Status = text;
                }
                else if (cls.Contains("mg_release", StringComparison.OrdinalIgnoreCase) && int.TryParse(text, out var year))
                {
                    manga.ReleaseYear = year;
                }
            }
        }

        protected virtual void PopulateLatestChapter(HtmlNode node, Manga manga)
        {
            if (string.IsNullOrWhiteSpace(Settings.LatestChapterClassFragment))
            {
                return;
            }

            var latestChapterNode = node.Descendants("a")
                .FirstOrDefault(a => a.ParentNode?.GetAttributeValue("class", string.Empty).Contains(Settings.LatestChapterClassFragment) == true);
            manga.LatestChapter = latestChapterNode?.InnerText.Trim() ?? string.Empty;
        }

        protected virtual void PopulateUpdateInfo(HtmlNode node, Manga manga)
        {
            if (string.IsNullOrWhiteSpace(Settings.UpdateTimestampFormat))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(Settings.UpdateTimestampClassFragment))
            {
                return;
            }

            var updateNode = node.Descendants("span")
                .FirstOrDefault(s => s.GetAttributeValue("class", string.Empty).Contains(Settings.UpdateTimestampClassFragment));
            if (updateNode != null &&
                DateTime.TryParseExact(updateNode.InnerText.Trim(), Settings.UpdateTimestampFormat,
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var lastUpdated))
            {
                manga.LastUpdated = lastUpdated;
            }
        }

        protected virtual void PopulateRating(HtmlNode node, Manga manga)
        {
            if (string.IsNullOrWhiteSpace(Settings.RatingClassFragment))
            {
                return;
            }

            var ratingNode = node.Descendants("span")
                .FirstOrDefault(s => s.GetAttributeValue("class", string.Empty).Contains(Settings.RatingClassFragment));
            if (ratingNode != null &&
                double.TryParse(ratingNode.InnerText.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var rating))
            {
                manga.Rating = rating;
            }
        }

        protected Uri ToAbsolute(string maybeUrl)
        {
            if (string.IsNullOrWhiteSpace(maybeUrl))
            {
                return Settings.BaseUri;
            }

            if (Uri.TryCreate(maybeUrl, UriKind.Absolute, out var abs))
            {
                return abs;
            }

            if (maybeUrl.StartsWith("/", StringComparison.Ordinal))
            {
                return new Uri(Settings.BaseUri, maybeUrl);
            }

            return new Uri(Settings.BaseUri, "/" + maybeUrl);
        }

        protected string? PickBestImage(HtmlNode img)
        {
            var candidates = new List<(string Url, int Weight, int Order)>();
            var order = 0;

            void AddCandidate(string? value, int weight)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return;
                }

                candidates.Add((value.Trim(), weight, order++));
            }

            void AddSrcSetCandidates(string? srcset)
            {
                if (string.IsNullOrWhiteSpace(srcset))
                {
                    return;
                }

                foreach (var entry in srcset.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var parts = entry.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 0)
                    {
                        continue;
                    }

                    var url = parts[0];
                    var weight = 0;
                    if (parts.Length > 1)
                    {
                        var descriptor = parts[1];
                        if (descriptor.EndsWith("w", StringComparison.OrdinalIgnoreCase) &&
                            int.TryParse(descriptor[..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width))
                        {
                            weight = width;
                        }
                        else if (descriptor.EndsWith("x", StringComparison.OrdinalIgnoreCase) &&
                                 double.TryParse(descriptor[..^1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var scale))
                        {
                            weight = (int)Math.Round(scale * 1000);
                        }
                    }

                    AddCandidate(url, weight);
                }
            }

            AddSrcSetCandidates(img.GetAttributeValue("srcset", null));
            AddSrcSetCandidates(img.GetAttributeValue("data-srcset", null));

            var pictureNode = FindPictureAncestor(img);
            if (pictureNode != null)
            {
                foreach (var source in pictureNode.Elements("source"))
                {
                    AddSrcSetCandidates(source.GetAttributeValue("srcset", null));
                    AddSrcSetCandidates(source.GetAttributeValue("data-srcset", null));
                    AddCandidate(source.GetAttributeValue("src", null), 0);
                    AddCandidate(source.GetAttributeValue("data-src", null), 0);
                    AddCandidate(source.GetAttributeValue("data-original", null), 0);
                }
            }

            AddCandidate(img.GetAttributeValue("src", null), 0);
            AddCandidate(img.GetAttributeValue("data-src", null), -1);
            AddCandidate(img.GetAttributeValue("data-cfsrc", null), -1);
            AddCandidate(img.GetAttributeValue("data-lazy-src", null), -1);
            AddCandidate(img.GetAttributeValue("data-original", null), -1);

            if (candidates.Count == 0)
            {
                return null;
            }

            return candidates
                .OrderByDescending(c => c.Weight)
                .ThenBy(c => c.Order)
                .Select(c => c.Url)
                .FirstOrDefault();
        }

        private static HtmlNode? FindPictureAncestor(HtmlNode img)
        {
            var node = img.ParentNode;
            while (node != null)
            {
                if (string.Equals(node.Name, "picture", StringComparison.OrdinalIgnoreCase))
                {
                    return node;
                }

                node = node.ParentNode;
            }

            return null;
        }
    }
}