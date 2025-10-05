using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSCS.Helpers
{
    public static partial class Constants
    {
        public const int DefaultLoadedBatchSize = 2; // number of images to load at once
        public const int DefaultMaxPageWidth = 900; // max width for a page
        public const double DefaultWidthFactor = 0.40; // default width factor for images
        public const double DefaultClickScrollValue = 150; // default scroll value on image click
        public const int DefaultSmoothScrollDuration = 250; // default duration for smooth scroll in ms
        public const double DefaultSmoothScrollPageFraction = 0.85; // factor for smooth scroll
        public const string ClientID = "30966";
    }
}
