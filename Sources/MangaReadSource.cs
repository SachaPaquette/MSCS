using HtmlAgilityPack;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;
using MSCS.Interfaces;
using MSCS.Models;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Policy;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MSCS.Sources
{
    public class MangaReadSource : IMangaSource
    {
        private static readonly Uri BaseUri = new("https://www.mangaread.org/");
        private static readonly Regex ChapterNum = new(@"\d+(?:\.\d+)?", RegexOptions.Compiled);

        // Tuned, shared HTTP stack (cookies + Brotli/Deflate/GZip + pooling)
        private static readonly SocketsHttpHandler Handler = new()
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 8
        };

        private static readonly HttpClient Http = new(Handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        static MangaReadSource()
        {
            // Sensible defaults; site-specific headers can be added per request
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("MSCS/1.0 (+https://example)");
            Http.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        }

        public async Task<List<Manga>> SearchMangaAsync(string query, CancellationToken ct = default)
        {
            var url = new Uri(BaseUri, $"?s={Uri.EscapeDataString(query)}&post_type=wp-manga");

            using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var html = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var results = new List<Manga>();
            ExtractMangaFromDocument(doc, results);
            return results;
        }

        public List<Manga> ParseMangaFromHtmlFragment(string htmlFragment)
        {
            var results = new List<Manga>();
            var doc = new HtmlDocument();
            doc.LoadHtml(htmlFragment);
            ExtractMangaFromDocument(doc, results);
            return results;
        }

        public async Task<List<Chapter>> GetChaptersAsync(string mangaUrl, CancellationToken ct = default)
        {
            var uri = ToAbsolute(mangaUrl);

            using var resp = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var html = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var nodes = doc.DocumentNode.SelectNodes("//ul[contains(@class,'main')]/li[contains(@class,'wp-manga-chapter')]/a");
            if (nodes == null)
                return new List<Chapter>();

            var chapters = nodes.Select(a => new Chapter
            {
                Title = WebUtility.HtmlDecode(a.InnerText.Trim()),
                Url = a.GetAttributeValue("href", "")
            }).ToList();

            // Robust numeric-first sort, fallback to title
            chapters = chapters
                .OrderBy(ch =>
                {
                    var match = ChapterNum.Match(ch.Title);
                    return match.Success ? double.Parse(match.Value, CultureInfo.InvariantCulture) : double.NaN;
                })
                .ThenBy(ch => ch.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return chapters;
        }

        public async Task<string> LoadMoreSeriesHtmlAsync(string query, int page, CancellationToken ct = default)
        {
            var url = new Uri(BaseUri, "wp-admin/admin-ajax.php");

            // Mirrors madara_load_more params
            var values = new Dictionary<string, string>
            {
                { "action", "madara_load_more" },
                { "page", page.ToString(CultureInfo.InvariantCulture) },
                { "template", "madara-core/content/content-search" },
                { "vars[s]", query },
                { "vars[orderby]", "" },
                { "vars[paged]", page.ToString(CultureInfo.InvariantCulture) },
                { "vars[template]", "search" },
                { "vars[post_type]", "wp-manga" },
                { "vars[post_status]", "publish" },
                { "vars[manga_archives_item_layout]", "default" }
            };

            using var content = new FormUrlEncodedContent(values);
            using var resp = await Http.PostAsync(url, content, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<ChapterImage>> FetchChapterImages(string chapterUrl, CancellationToken ct = default)
        {
            var uri = ToAbsolute(chapterUrl);

            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            // Many readers require referer on subsequent image requests; capture it at least for parity.
            req.Headers.Referrer = uri;

            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var html = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var imageNodes = doc.DocumentNode.SelectNodes("//div[contains(@class,'reading-content')]//img");
            if (imageNodes == null || imageNodes.Count == 0)
                return Array.Empty<ChapterImage>();

            var list = new List<ChapterImage>(imageNodes.Count);
            var referer = uri.ToString();
            var cookieHeader = Handler.CookieContainer.GetCookieHeader(uri);

            foreach (var img in imageNodes)
            {
                var raw = PickBestImage(img);
                if (string.IsNullOrWhiteSpace(raw)) continue;

                var abs = ToAbsolute(raw.Trim()).ToString();

                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Referer"] = referer
                };

                if (!string.IsNullOrWhiteSpace(cookieHeader))
                {
                    headers["Cookie"] = cookieHeader;
                }

                list.Add(new ChapterImage
                {
                    ImageUrl = abs,
                    Headers = headers
                });
            }

            return list;
        }

        // ---------- Helpers ----------

        private static Uri ToAbsolute(string maybeUrl)
        {
            if (string.IsNullOrWhiteSpace(maybeUrl)) return BaseUri;
            if (Uri.TryCreate(maybeUrl, UriKind.Absolute, out var abs)) return abs;
            if (maybeUrl.StartsWith("/", StringComparison.Ordinal)) return new Uri(BaseUri, maybeUrl);
            return new Uri(BaseUri, "/" + maybeUrl);
        }

        private static string? PickBestImage(HtmlNode img)
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

        private static void ExtractMangaFromDocument(HtmlDocument doc, List<Manga> results)
        {
            var mangaNodes = doc.DocumentNode.SelectNodes("//div[contains(@class,'c-tabs-item__content')]");
            if (mangaNodes == null) return;

            foreach (var node in mangaNodes)
            {
                var titleAnchor = node.SelectSingleNode(".//div[contains(@class,'post-title')]//a");
                if (titleAnchor == null) continue;

                var imageNode = node.Descendants("img")
                    .FirstOrDefault(img => img.Ancestors("div")
                                              .Any(div => div.GetAttributeValue("class", "").Contains("tab-thumb")));

                var coverRaw = imageNode != null ? PickBestImage(imageNode) : null;

                var m = new Manga
                {
                    Title = WebUtility.HtmlDecode(titleAnchor.InnerText.Trim()),
                    Url = titleAnchor.GetAttributeValue("href", ""),
                    CoverImageUrl = string.IsNullOrWhiteSpace(coverRaw) ? null : ToAbsolute(coverRaw).ToString()
                };

                var summaryNodes = node.SelectNodes(".//div[contains(@class,'summary-content')]");
                if (summaryNodes != null)
                {
                    foreach (var s in summaryNodes)
                    {
                        var parent = s.ParentNode?.ParentNode;
                        if (parent == null) continue;

                        var cls = parent.GetAttributeValue("class", "");
                        var text = WebUtility.HtmlDecode(s.InnerText?.Trim() ?? "");

                        if (cls.Contains("mg_alternative"))
                            m.AlternativeTitles = text.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).ToList();
                        else if (cls.Contains("mg_author"))
                            m.Authors = s.Descendants("a").Select(a => WebUtility.HtmlDecode(a.InnerText.Trim())).ToList();
                        else if (cls.Contains("mg_artists"))
                            m.Artists = s.Descendants("a").Select(a => WebUtility.HtmlDecode(a.InnerText.Trim())).ToList();
                        else if (cls.Contains("mg_genres"))
                            m.Genres = s.Descendants("a").Select(a => WebUtility.HtmlDecode(a.InnerText.Trim())).ToList();
                        else if (cls.Contains("mg_status"))
                            m.Status = text;
                        else if (cls.Contains("mg_release") && int.TryParse(text, out var year))
                            m.ReleaseYear = year;
                    }
                }

                var latestChapterNode = node.Descendants("a")
                    .FirstOrDefault(a => a.ParentNode?.GetAttributeValue("class", "").Contains("latest-chap") == true);
                m.LatestChapter = latestChapterNode?.InnerText.Trim() ?? "";

                var updateNode = node.Descendants("span")
                    .FirstOrDefault(s => s.GetAttributeValue("class", "").Contains("font-meta"));
                if (updateNode != null &&
                    DateTime.TryParseExact(updateNode.InnerText.Trim(), "yyyy-MM-dd HH:mm:ss",
                                           CultureInfo.InvariantCulture, DateTimeStyles.None, out var lastUpdated))
                {
                    m.LastUpdated = lastUpdated;
                }

                var ratingNode = node.Descendants("span")
                    .FirstOrDefault(s => s.GetAttributeValue("class", "").Contains("total_votes"));
                if (ratingNode != null &&
                    double.TryParse(ratingNode.InnerText.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var rating))
                {
                    m.Rating = rating;
                }

                results.Add(m);
            }
        }
    }
}
