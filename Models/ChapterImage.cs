using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSCS.Models
{
    public class ChapterImage
    {
        public string ImageUrl { get; set; } = string.Empty;
        public IDictionary<string, string>? Headers { get; set; }
    }
}
