using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
﻿using System;
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

        public static readonly DependencyProperty HorizontalOffsetProperty =
            DependencyProperty.RegisterAttached(
                "HorizontalOffset",
                typeof(double),
                typeof(SmoothScroll),
                new PropertyMetadata(0.0, OnHorizontalOffsetChanged));

        private static void OnVerticalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer sv)
            {
                sv.ScrollToVerticalOffset((double)e.NewValue);
            }
        }

        private static void OnHorizontalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer sv)
            {
                sv.ScrollToHorizontalOffset((double)e.NewValue);
            }
        }

        public static double GetVerticalOffset(DependencyObject d) =>
            (double)d.GetValue(VerticalOffsetProperty);
        public static void SetVerticalOffset(DependencyObject d, double value) =>
            d.SetValue(VerticalOffsetProperty, value);

        public static double GetHorizontalOffset(DependencyObject d) =>
            (double)d.GetValue(HorizontalOffsetProperty);
        public static void SetHorizontalOffset(DependencyObject d, double value) =>
            d.SetValue(HorizontalOffsetProperty, value);

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

        public static void By(ScrollViewer sv, double delta, TimeSpan duration, IEasingFunction? easing = null)
            => To(sv, sv.VerticalOffset + delta, duration, easing);

        public static void ToHorizontal(ScrollViewer sv, double targetOffset, TimeSpan duration, IEasingFunction? easing = null)
        {
            if (sv == null) return;

            sv.BeginAnimation(HorizontalOffsetProperty, null);

            var from = sv.HorizontalOffset;
            var anim = new DoubleAnimation
            {
                From = from,
                To = Math.Max(0, targetOffset),
                Duration = duration,
                EasingFunction = easing ?? new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            sv.BeginAnimation(HorizontalOffsetProperty, anim);
        }

        public static void ByHorizontal(ScrollViewer sv, double delta, TimeSpan duration, IEasingFunction? easing = null)
            => ToHorizontal(sv, sv.HorizontalOffset + delta, duration, easing);
    }
}