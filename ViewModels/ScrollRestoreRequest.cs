using System;

namespace MSCS.ViewModels
{
    public sealed class ScrollRestoreRequest : EventArgs
    {
        public ScrollRestoreRequest(double? normalizedProgress, double? scrollOffset)
        {
            NormalizedProgress = normalizedProgress;
            ScrollOffset = scrollOffset;
        }

        public double? NormalizedProgress { get; }

        public double? ScrollOffset { get; }
    }
}