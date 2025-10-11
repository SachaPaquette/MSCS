using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace MSCS.Sources
{
    /// <summary>
    /// Declarative configuration for <see cref="MadaraSourceBase"/> implementations. Most Madara
    /// sites reuse the same selectors and AJAX endpoints, so these defaults aim to work out of the
    /// box for common deployments while still allowing per-source overrides when necessary.
    /// </summary>
    public sealed record MadaraSourceSettings
    {
        public MadaraSourceSettings(Uri baseUri)
        {
            BaseUri = baseUri ?? throw new ArgumentNullException(nameof(baseUri));
        }

        /// <summary>Root URL of the site, e.g. https://www.mangaread.org/.</summary>
        public Uri BaseUri { get; init; }

        /// <summary>Timeout applied to HTTP requests issued for this source.</summary>
        public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Extra headers appended to every outbound request. Useful for sites that expect browser-like
        /// hints (e.g. Accept-Language or Sec-Fetch-* values) to bypass basic anti-bot challenges.
        /// </summary>
        [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Settings object")]
        public Dictionary<string, string> AdditionalRequestHeaders { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Format used when building search URLs. The escaped query string is injected as {0}.
        /// Defaults to "?s={0}&post_type=wp-manga" which mirrors Madara's search form.
        /// </summary>
        public string SearchTemplate { get; init; } = "?s={0}&post_type=wp-manga";

        /// <summary>AJAX endpoint path for infinite scroll / load more requests.</summary>
        public string LoadMorePath { get; init; } = "wp-admin/admin-ajax.php";

        /// <summary>Action field used in load-more POST bodies.</summary>
        public string LoadMoreAction { get; init; } = "madara_load_more";

        public string LoadMoreTemplate { get; init; } = "madara-core/content/content-search";

        public string LoadMoreTemplateName { get; init; } = "search";

        public string LoadMoreItemLayout { get; init; } = "default";

        public string PostType { get; init; } = "wp-manga";

        public string ChapterListXPath { get; init; } = "//ul[contains(@class,'main')]/li[contains(@class,'wp-manga-chapter')]/a";

        public string MangaContainerXPath { get; init; } = "//div[contains(@class,'c-tabs-item__content')]";

        public string TitleXPath { get; init; } = ".//div[contains(@class,'post-title')]//a";

        public string MetadataSectionXPath { get; init; } = ".//div[contains(@class,'summary-content')]";

        public string CoverContainerClassFragment { get; init; } = "tab-thumb";

        public string LatestChapterClassFragment { get; init; } = "latest-chap";

        public string UpdateTimestampClassFragment { get; init; } = "font-meta";

        public string UpdateTimestampFormat { get; init; } = "yyyy-MM-dd HH:mm:ss";

        public string RatingClassFragment { get; init; } = "total_votes";

        public string ReaderImageXPath { get; init; } = "//div[contains(@class,'reading-content')]//img";
    }
}