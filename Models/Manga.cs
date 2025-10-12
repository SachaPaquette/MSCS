using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSCS.Models
{
    public class Manga
    {
        public string Title { get; set; }
        public string Url { get; set; }
        public string CoverImageUrl { get; set; }
        public string Description { get; set; }
        public List<string> Genres { get; set; } = new List<string>();
        public DateTime? LastUpdated { get; set; }
        public int? TotalChapters { get; set; }
        public override string ToString()
        {
            return $"{Title} - {Url}";
        }
        public List<string> AlternativeTitles { get; set; } = new List<string>();
        public List<string> Authors { get; set; } = new List<string>();
        public List<string> Artists { get; set; } = new List<string>();
        public string Status { get; set; }
        public int? ReleaseYear { get; set; }
        public string LatestChapter { get; set; }
        public double? Rating { get; set; } = 0.0;
    }
}
