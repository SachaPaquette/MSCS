using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace MSCS.Helpers
{
    public static class SmoothScroll
    {
        // Dummy attached DP we animate; on change we call ScrollToVerticalOffset.
        public static readonly DependencyProperty VerticalOffsetProperty =
            DependencyProperty.RegisterAttached(
                "VerticalOffset",
                typeof(double),
                typeof(SmoothScroll),
                new PropertyMetadata(0.0, OnVerticalOffsetChanged));

        private static void OnVerticalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer sv)
            {
                sv.ScrollToVerticalOffset((double)e.NewValue);
            }
        }

        public static double GetVerticalOffset(DependencyObject d) =>
            (double)d.GetValue(VerticalOffsetProperty);
        public static void SetVerticalOffset(DependencyObject d, double value) =>
            d.SetValue(VerticalOffsetProperty, value);

        /// <summary>Animate to a target vertical offset.</summary>
        public static void To(ScrollViewer sv, double targetOffset, TimeSpan duration, IEasingFunction? easing = null)
        {
            if (sv == null) return;

            // Stop any ongoing animation
            sv.BeginAnimation(VerticalOffsetProperty, null);

            var from = sv.VerticalOffset;
            var anim = new DoubleAnimation
            {
                From = from,
                To = Math.Max(0, targetOffset),
                Duration = duration,
                EasingFunction = easing ?? new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            sv.BeginAnimation(VerticalOffsetProperty, anim);
        }

        /// <summary>Convenience: scroll by a delta smoothly.</summary>
        public static void By(ScrollViewer sv, double delta, TimeSpan duration, IEasingFunction? easing = null)
            => To(sv, sv.VerticalOffset + delta, duration, easing);
    }
}
