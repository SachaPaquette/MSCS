using System;

namespace MSCS.ViewModels
{
    public sealed class ScrollRestoreRequest : EventArgs
    {
        public ScrollRestoreRequest(double? normalizedProgress, double? scrollOffset, string? anchorImageUrl, double? anchorImageProgress)
        {
            NormalizedProgress = normalizedProgress;
            ScrollOffset = scrollOffset;
            AnchorImageUrl = anchorImageUrl;
            AnchorImageProgress = anchorImageProgress;
        }

        public double? NormalizedProgress { get; }

        public double? ScrollOffset { get; }

        public string? AnchorImageUrl { get; }

        public double? AnchorImageProgress { get; }
    }
}