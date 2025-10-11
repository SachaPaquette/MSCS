using HtmlAgilityPack;
using MSCS.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;

namespace MSCS.Sources
{
    public sealed class AsuraScansSource : MadaraSourceBase
    {
        private static readonly MadaraSourceSettings Settings = new(new Uri("https://www.asurascans.com/"))
        {
            ReaderImageXPath = "//div[contains(@class,'reading-content')]//img | //div[contains(@class,'image-container')]//img",
            ChapterListXPath = "//li[contains(@class,'wp-manga-chapter')]/a",
            SearchTemplate = "series?name={0}&page=1",
            UpdateTimestampFormat = string.Empty,
            AdditionalRequestHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Accept-Language"] = "en-US,en;q=0.9",
                ["Cache-Control"] = "no-cache",
                ["Pragma"] = "no-cache",
                ["Sec-Fetch-Site"] = "same-origin",
                ["Sec-Fetch-Mode"] = "navigate",
                ["Sec-Fetch-Dest"] = "document"
            }
        };

        public AsuraScansSource() : base(Settings) { }

        /// <summary>
        /// Asura’s search takes only the first letter for 'name'.
        /// If the user types a longer query, we fall back to its first character.
        /// </summary>
        protected override Uri BuildSearchUri(string query)
        {
            var key = (query ?? string.Empty).Trim();
            var letter = key.Length > 0 ? char.ToLowerInvariant(key[0]).ToString() : string.Empty;

            var formatted = string.Format(CultureInfo.InvariantCulture, Settings.SearchTemplate,
                                          Uri.EscapeDataString(letter));
            return new Uri(Settings.BaseUri, formatted);
        }

        /// <summary>
        /// Asura paginates with GET at /series?name=<letter>&page=<n>, not Madara Ajax POST.
        /// Return the full HTML so the existing HTML fragment parser can be reused.
        /// </summary>
        public override async Task<string> LoadMoreSeriesHtmlAsync(string query, int page, CancellationToken ct = default)
        {
            var key = (query ?? string.Empty).Trim();
            var letter = key.Length > 0 ? char.ToLowerInvariant(key[0]).ToString() : string.Empty;

            var url = new Uri(Settings.BaseUri,
                $"series?name={Uri.EscapeDataString(letter)}&page={page.ToString(CultureInfo.InvariantCulture)}");

            using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Asura exposes relative timestamps ("x days ago"). We keep the raw string elsewhere;
        /// nothing to parse here.
        /// </summary>
        protected override void PopulateUpdateInfo(HtmlNode node, Manga manga)
        {
            // Intentionally no-op
        }
    }
}