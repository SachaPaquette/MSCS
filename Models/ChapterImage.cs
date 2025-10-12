using System.IO;
using System;
using System.Collections.Generic;

namespace MSCS.Models
{
    public class ChapterImage
    {
        public string ImageUrl { get; set; } = string.Empty;
        public IDictionary<string, string>? Headers { get; set; }
        public Func<Stream>? StreamFactory { get; set; }
    }
}