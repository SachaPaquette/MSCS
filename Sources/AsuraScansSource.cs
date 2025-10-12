using HtmlAgilityPack;
using MSCS.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MSCS.Sources
{
    public sealed class AsuraScansSource : MadaraSourceBase
    {
        private static readonly MadaraSourceSettings Settings = new(new Uri("https://asuracomic.net/"))
        {

            ReaderImageXPath = "//div[contains(@class,'reading-content')]//img | //div[contains(@class,'image-container')]//img",
            UpdateTimestampFormat = string.Empty,
            ChapterListXPath = "//a[contains(@href,'chapter/')]",

            SearchTemplate = "series?page=1&name={0}"
        };
        protected override Uri BuildSearchUri(string query) => new Uri(Settings.BaseUri, $"/series?name={NormalizeLetter(query)}&page=1");

        public AsuraScansSource() : base(Settings) { }

        // href:"/series/<slug>" or "series/<slug>"
        private static readonly Regex SlugRx = new(
            @"href\\\""\s*:\s*\\\""\/*series/([a-z0-9-]+)\\?\""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Next to the slug, title sits in a span with font-bold
        private static readonly Regex TitleRx = new(
            @"className\\\""\s*:\s*\\\""block[^""]*font-bold\\\"",\s*\\\""children\\\""\s*:\s*\\\""([^\""]{2,200})\\\""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Cover image inside Image component: src:"https://...thumb-small.webp"
        private static readonly Regex ImageRx = new(
            "\\\\\"src\\\\\"\\s*:\\s*\\\\\"(https?://[^\\\\\"]+thumb-small\\.webp)\\\\\"",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Chapter label appears like: "children":["Chapter ",180]
        private static readonly Regex ChapterRx = new(
            "\\\\\"children\\\\\"\\s*:\\s*\\[\\s*\\\\\"Chapter\\s*\\\\\",\\s*(\\d+)\\s*\\]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex HashSuffixRx = new("-[0-9a-f]{8}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SeriesSlugRx =
    new(@"/series/(?<slug>[^/]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ChapterNumberRegex = new("\\d+(?:\\.\\d+)?", RegexOptions.Compiled);

        protected override List<Manga> ParseMangaHtml(string html)
        {
            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);

            var anchors = doc.DocumentNode.SelectNodes("//a[contains(@href,'/series/') or starts-with(@href,'series/')]");
            if (anchors == null) return new List<Manga>();

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var results = new List<Manga>();

            var slugRx = new Regex(@"(?:^|/)series/([a-z0-9\-]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

            foreach (var a in anchors)
            {
                var href = a.GetAttributeValue("href", string.Empty);
                if (string.IsNullOrWhiteSpace(href)) continue;

                var m = slugRx.Match(href);
                if (!m.Success) continue;

                var slug = m.Groups[1].Value;
                if (!seen.Add(slug)) continue;

                var title = WebUtility.HtmlDecode(a.InnerText?.Trim() ?? string.Empty);
                if (string.IsNullOrWhiteSpace(title))
                    title = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(slug.Replace('-', ' '));

                string? cover = null;
                var img = a.SelectSingleNode(".//img")
                          ?? a.ParentNode?.SelectSingleNode(".//img");
                if (img != null)
                {
                    var raw = PickBestImage(img);
                    if (!string.IsNullOrWhiteSpace(raw)) cover = ToAbsolute(raw!).ToString();
                }

                results.Add(new Manga
                {
                    Title = title,
                    Url = ToAbsolute(href).ToString(),
                    CoverImageUrl = cover
                });
            }

            return results;
        }

        private static string? TryMatch(Regex rx, string text)
        {
            var m = rx.Match(text);
            return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value) : null;
        }

        private static string SlugToTitle(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug)) return slug ?? "";
            slug = HashSuffixRx.Replace(slug, string.Empty);
            var spaced = slug.Replace('-', ' ');
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(spaced);
        }

        private static string NormalizeLetter(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return "a";
            var c = query.Trim()[0];
            return char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c).ToString() : "a";
        }

        public override async Task<string> LoadMoreSeriesHtmlAsync(string query, int page, CancellationToken ct = default)
        {
            var letter = NormalizeLetter(query);
            var uri = new Uri(Settings.BaseUri, $"/series?name={Uri.EscapeDataString(letter)}&page={page}");
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            ConfigureRequest(req);

            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            if ((int)resp.StatusCode == 404) return string.Empty;
            if (!resp.IsSuccessStatusCode) return string.Empty;

            return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }

        public override List<Manga> ParseMangaFromHtmlFragment(string htmlFragment)
        {
            var list = ParseSeriesFromNextFlight(htmlFragment);
            return list.Count > 0 ? list : base.ParseMangaFromHtmlFragment(htmlFragment);
        }

        private List<Manga> ParseSeriesFromNextFlight(string text)
        {
            var results = new List<Manga>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match m in SlugRx.Matches(text))
            {
                var slug = m.Groups[1].Value;
                if (string.IsNullOrWhiteSpace(slug) || !seen.Add(slug))
                    continue;

                var start = m.Index;
                var windowLen = Math.Min(text.Length - start, 3500);
                if (windowLen <= 0) continue;

                var window = text.Substring(start, windowLen);

                var title = TryMatch(TitleRx, window);
                if (!string.IsNullOrEmpty(title) &&
                    (title.Equals("Ongoing", StringComparison.OrdinalIgnoreCase) ||
                     title.Equals("MANHWA", StringComparison.OrdinalIgnoreCase) ||
                     title.Equals("MANHUA", StringComparison.OrdinalIgnoreCase) ||
                     title.StartsWith("Chapter ", StringComparison.OrdinalIgnoreCase)))
                {
                    title = null;
                }

                var cover = TryMatch(ImageRx, window);
                var latestChapter = TryMatch(ChapterRx, window);

                var url = new Uri(Settings.BaseUri, $"/series/{slug}").ToString();

                results.Add(new Manga
                {
                    Title = string.IsNullOrWhiteSpace(title) ? SlugToTitle(slug) : title,
                    Url = url,
                    CoverImageUrl = cover,
                    LatestChapter = latestChapter
                });
            }

            return results;
        }

        private static string NormalizeChapterHref(string href, Uri pageUri)
        {
            if (string.IsNullOrWhiteSpace(href)) return string.Empty;

            if (Uri.TryCreate(href, UriKind.Absolute, out var abs))
                return abs.ToString();

            var trimmed = href.Trim();
            var pagePath = pageUri.AbsolutePath;
            var m = SeriesSlugRx.Match(pagePath);
            var slug = m.Success ? m.Groups["slug"].Value : null;

            if (trimmed.IndexOf("/series/", StringComparison.OrdinalIgnoreCase) >= 0)
                return new Uri(pageUri, trimmed).ToString();

            var noLead = trimmed.TrimStart('/');

            if (slug != null && noLead.StartsWith("chapter/", StringComparison.OrdinalIgnoreCase))
                return new Uri(pageUri, $"/series/{slug}/{noLead}").ToString();

            if (slug != null && noLead.StartsWith(slug + "/chapter/", StringComparison.OrdinalIgnoreCase))
                return new Uri(pageUri, $"/series/{noLead}").ToString();

            return new Uri(pageUri, trimmed).ToString();
        }

        public override async Task<List<Chapter>> GetChaptersAsync(string mangaUrl, CancellationToken ct = default)
        {
            var uri = ToAbsolute(mangaUrl);

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            ConfigureRequest(request);

            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);

            var anchors = doc.DocumentNode.SelectNodes(Settings.ChapterListXPath)
                        ?? doc.DocumentNode.SelectNodes("//a[contains(@href,'chapter/')]");
            if (anchors == null) return new List<Chapter>();

            var list = new List<Chapter>(anchors.Count);
            foreach (var a in anchors)
            {
                var href = a.GetAttributeValue("href", string.Empty);
                if (string.IsNullOrWhiteSpace(href)) continue;

                var title = WebUtility.HtmlDecode(a.InnerText?.Trim() ?? string.Empty);
                if (string.IsNullOrWhiteSpace(title))
                {
                    var tail = href.Replace('\\', '/');
                    var i = tail.LastIndexOf("/chapter/", StringComparison.OrdinalIgnoreCase);
                    var piece = i >= 0 ? tail[(i + "/chapter/".Length)..] : tail.Trim('/');
                    title = "Chapter " + piece.Replace('-', ' ');
                }

                list.Add(new Chapter
                {
                    Title = title,
                    Url = NormalizeChapterHref(href, uri)   // <— key line
                });
            }

            return SortChapters(list);
        }

        private static readonly Regex FlightScriptRx =
            new(@"self\.__next_f\.push\(\[\d+,\s*""(?<chunk>.*?)""\]\)",
                RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex UrlOrderRx = new(
            @"""order""\s*:\s*(?<o>\d+)[^}]{0,400}?""url""\s*:\s*""(?<u>https?:\/\/gg\.asuracomic\.net\/[^""]+?(?:-optimized\.(?:webp|jpg|png)|\.(?:webp|jpg|png))(?:\?[^""]*)?)""
   |""url""\s*:\s*""(?<u2>https?:\/\/gg\.asuracomic\.net\/[^""]+?(?:-optimized\.(?:webp|jpg|png)|\.(?:webp|jpg|png))(?:\?[^""]*)?)""[^}]{0,400}?""order""\s*:\s*(?<o2>\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex AnyCdnUrlRx = new(
            @"https?:\/\/gg\.asuracomic\.net\/[^""\\]+?(?:-optimized\.(?:webp|jpg|png)|\.(?:webp|jpg|png))(?:\?[^""\\]*)?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static string UnescapeFlightLoose(string s) =>
            s.Replace(@"\\", @"\")
             .Replace(@"\/", @"/")
             .Replace(@"\n", "\n")
             .Replace(@"\t", "\t")
             .Replace("\\\"", "\"");

        private static string? DecodeFlightChunk(string raw) { try { return JsonSerializer.Deserialize<string>("\"" + raw + "\""); } catch (JsonException) { return null; } }

        public override async Task<IReadOnlyList<ChapterImage>> FetchChapterImages(string chapterUrl, CancellationToken ct = default)
        {
            var uri = ToAbsolute(chapterUrl);

            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            ConfigureRequest(req);
            req.Headers.Referrer = uri;

            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var html = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (html.IndexOf("self.__next_f", StringComparison.Ordinal) >= 0)
            {
                var pairs = new List<(int order, string url)>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (Match m in FlightScriptRx.Matches(html))
                {
                    var raw = m.Groups["chunk"].Value;
                    var decoded = DecodeFlightChunk(raw) ?? UnescapeFlightLoose(raw);

                    foreach (Match mu in UrlOrderRx.Matches(decoded))
                    {
                        var url = mu.Groups["u"].Success ? mu.Groups["u"].Value : mu.Groups["u2"].Value;
                        if (string.IsNullOrWhiteSpace(url) || !seen.Add(url)) continue;

                        var oText = mu.Groups["o"].Success ? mu.Groups["o"].Value : mu.Groups["o2"].Value;
                        int order = 0;
                        int.TryParse(oText, NumberStyles.Integer, CultureInfo.InvariantCulture, out order);

                        pairs.Add((order, url));
                    }
                }

                if (pairs.Count == 0)
                {
                    var tmpSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    int idx = 1;
                    foreach (Match u in AnyCdnUrlRx.Matches(html))
                    {
                        var url = u.Value.Replace(@"\/", "/");
                        if (tmpSeen.Add(url)) pairs.Add((idx++, url));
                    }
                }

                if (pairs.Count > 0)
                {
                    var ordered = pairs
                        .OrderBy(p => p.order == 0 ? int.MaxValue : p.order)
                        .ThenBy(p => p.order == 0 ? 1 : 0)
                        .ToList();

                    var cookieHeader = Handler.CookieContainer.GetCookieHeader(uri);
                    var list = ordered.Select(p => new ChapterImage
                    {
                        ImageUrl = p.url,
                        Headers = string.IsNullOrWhiteSpace(cookieHeader)
                            ? new Dictionary<string, string> { ["Referer"] = uri.ToString() }
                            : new Dictionary<string, string> { ["Referer"] = uri.ToString(), ["Cookie"] = cookieHeader }
                    }).ToList();

                    Debug.WriteLine($"[AsuraScans] Flight images: {list.Count}");
                    if (list.Count > 0) return list;
                }
            }

            return await base.FetchChapterImages(chapterUrl, ct).ConfigureAwait(false);
        }


        private void CollectChaptersFromJson(JsonElement element, Dictionary<string, Chapter> accumulator, Uri pageUri, string? currentHref)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    string? hrefCandidate = currentHref;
                    if (element.TryGetProperty("href", out var hrefProperty) && hrefProperty.ValueKind == JsonValueKind.String)
                    {
                        var hrefValue = hrefProperty.GetString();
                        if (!string.IsNullOrWhiteSpace(hrefValue))
                        {
                            hrefCandidate = hrefValue;
                        }
                    }

                    foreach (var property in element.EnumerateObject())
                    {
                        CollectChaptersFromJson(property.Value, accumulator, pageUri, hrefCandidate);
                    }

                    break;

                case JsonValueKind.Array:
                    if (element.GetArrayLength() >= 1)
                    {
                        var first = element[0];
                        if (first.ValueKind == JsonValueKind.String &&
                            first.GetString() is string label &&
                            !string.IsNullOrWhiteSpace(label) &&
                            label.Contains("Chapter", StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(currentHref) &&
                            (currentHref.IndexOf("/read/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             currentHref.IndexOf("/chapter/", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            var title = BuildChapterTitle(label, element);
                            var url = NormalizeChapterHref(currentHref!, pageUri);
                            if (string.IsNullOrWhiteSpace(url))
                            {
                                break;
                            }

                            if (!accumulator.TryGetValue(url, out var chapter))
                            {
                                chapter = new Chapter { Url = url };
                                accumulator[url] = chapter;
                            }

                            if (string.IsNullOrWhiteSpace(chapter.Title))
                            {
                                chapter.Title = string.IsNullOrWhiteSpace(title) ? label.Trim() : title;
                            }
                        }
                    }

                    foreach (var item in element.EnumerateArray())
                    {
                        CollectChaptersFromJson(item, accumulator, pageUri, currentHref);
                    }

                    break;
            }
        }


        private static string BuildChapterTitle(string prefix, JsonElement array)
        {
            var builder = new StringBuilder(prefix);

            for (var i = 1; i < array.GetArrayLength(); i++)
            {
                var element = array[i];
                switch (element.ValueKind)
                {
                    case JsonValueKind.String:
                        builder.Append(element.GetString());
                        break;
                    case JsonValueKind.Number:
                    case JsonValueKind.True:
                    case JsonValueKind.False:
                        builder.Append(element.GetRawText());
                        break;
                }
            }

            return builder.ToString().Trim();
        }

        private static List<Chapter> SortChapters(List<Chapter> chapters)
        {
            if (chapters.Count <= 1)
            {
                return chapters;
            }

            return chapters
                .OrderBy(chapter =>
                {
                    var match = ChapterNumberRegex.Match(chapter.Title);
                    return match.Success ? double.Parse(match.Value, CultureInfo.InvariantCulture) : double.NaN;
                })
                .ThenBy(chapter => chapter.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        protected override void PopulateUpdateInfo(HtmlNode node, Manga manga)
        {
        }
    }
}
