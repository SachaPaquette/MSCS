using HtmlAgilityPack;
using MSCS.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace MSCS.Sources
{
    public sealed class AsuraScansSource : MadaraSourceBase
    {
        private static readonly MadaraSourceSettings Settings = new(new Uri("https://asuracomic.net/"))
        {

            ReaderImageXPath = "//div[contains(@class,'reading-content')]//img | //div[contains(@class,'image-container')]//img",
            UpdateTimestampFormat = string.Empty,

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

        protected override List<Manga> ParseMangaHtml(string html)
        {
            var fromFlight = ParseSeriesFromNextFlight(html);
            return fromFlight.Count > 0 ? fromFlight : base.ParseMangaHtml(html);
        }

        private static string? TryMatch(Regex rx, string text)
        {
            var m = rx.Match(text);
            return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value) : null;
        }

        private static string SlugToTitle(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug)) return slug ?? "";
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

        protected override void PopulateUpdateInfo(HtmlNode node, Manga manga)
        {
        }
    }
}
