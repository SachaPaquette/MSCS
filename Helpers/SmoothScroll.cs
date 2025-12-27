using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace MSCS.Helpers
{
    public static class SmoothScroll
    {
        public static readonly DependencyProperty VerticalOffsetProperty =
          DependencyProperty.RegisterAttached(
            "VerticalOffset",
            typeof(double),
            typeof(SmoothScroll),
            new PropertyMetadata(0.0, OnVerticalOffsetChanged));

        public static readonly DependencyProperty IsAnimatingProperty =
          DependencyProperty.RegisterAttached(
            "IsAnimating",
            typeof(bool),
            typeof(SmoothScroll),
            new PropertyMetadata(false));

        private static void OnVerticalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer sv)
                sv.ScrollToVerticalOffset((double)e.NewValue);
        }

        public static bool GetIsAnimating(DependencyObject d) =>
          (bool)d.GetValue(IsAnimatingProperty);

        private static void SetIsAnimating(DependencyObject d, bool value) =>
          d.SetValue(IsAnimatingProperty, value);

        public static void Cancel(ScrollViewer sv)
        {
            if (sv == null) return;
            sv.BeginAnimation(VerticalOffsetProperty, null);
            SetIsAnimating(sv, false);
        }

        public static void To(ScrollViewer sv, double targetOffset, TimeSpan duration, IEasingFunction? easing = null)
        {
            if (sv == null) return;

            Cancel(sv);

            var from = sv.VerticalOffset;
            var target = Math.Clamp(targetOffset, 0, sv.ScrollableHeight);

            var anim = new DoubleAnimation
            {
                From = from,
                To = target,
                Duration = duration,
                EasingFunction = easing ?? new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            SetIsAnimating(sv, true);
            anim.Completed += (_, __) => SetIsAnimating(sv, false);

            sv.BeginAnimation(VerticalOffsetProperty, anim, HandoffBehavior.SnapshotAndReplace);
        }

        public static void By(ScrollViewer sv, double delta, TimeSpan duration, IEasingFunction? easing = null) =>
          To(sv, sv.VerticalOffset + delta, duration, easing);
    }
}