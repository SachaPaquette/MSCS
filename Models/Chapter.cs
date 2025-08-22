using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSCS.Models
{
    public class Chapter
    {
        public string Title { get; set; }
        public string Url { get; set; }
        public double Number { get; set; } // Double since some chapters may have decimal numbers (e.g., 1.5)
    }
}
