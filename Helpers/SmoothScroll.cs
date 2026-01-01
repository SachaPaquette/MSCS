using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MSCS.Helpers
{
    public static class SmoothScroll
    {
        private sealed class AnimationState
        {
            public required ScrollViewer ScrollViewer { get; init; }
            public required double From { get; set; }
            public required double To { get; set; }
            public required DateTime StartTime { get; init; }
            public required TimeSpan Duration { get; init; }
            public IEasingFunction? Easing { get; init; }
        }

        private static readonly Dictionary<ScrollViewer, AnimationState> ActiveAnimations = new();
        private static bool _isRenderingHooked;

        public static readonly DependencyProperty IsAnimatingProperty =
            DependencyProperty.RegisterAttached(
                "IsAnimating",
                typeof(bool),
                typeof(SmoothScroll),
                new PropertyMetadata(false));

        public static bool GetIsAnimating(DependencyObject d) =>
            (bool)d.GetValue(IsAnimatingProperty);

        private static void SetIsAnimating(DependencyObject d, bool value) =>
            d.SetValue(IsAnimatingProperty, value);

        public static void Cancel(ScrollViewer sv)
        {
            if (sv == null) return;

            ActiveAnimations.Remove(sv);
            SetIsAnimating(sv, false);

            UnhookRenderingIfIdle();
        }

        public static void To(ScrollViewer sv, double targetOffset, TimeSpan duration, IEasingFunction? easing = null)
        {
            if (sv == null) return;

            Cancel(sv);

            var from = sv.VerticalOffset;
            var target = Math.Clamp(targetOffset, 0, sv.ScrollableHeight);
            if (duration <= TimeSpan.Zero || Math.Abs(target - from) < 0.5)
            {
                sv.ScrollToVerticalOffset(target);
                return;
            }

            var state = new AnimationState
            {
                ScrollViewer = sv,
                From = from,
                To = target,
                StartTime = DateTime.UtcNow,
                Duration = duration,
                Easing = easing ?? new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            ActiveAnimations[sv] = state;
            SetIsAnimating(sv, true);
            HookRendering();
        }

        public static void By(ScrollViewer sv, double delta, TimeSpan duration, IEasingFunction? easing = null) =>
            To(sv, sv.VerticalOffset + delta, duration, easing);

        private static void HookRendering()
        {
            if (_isRenderingHooked) return;
            CompositionTarget.Rendering += OnRendering;
            _isRenderingHooked = true;
        }

        private static void UnhookRenderingIfIdle()
        {
            if (!_isRenderingHooked || ActiveAnimations.Count > 0) return;
            CompositionTarget.Rendering -= OnRendering;
            _isRenderingHooked = false;
        }

        private static void OnRendering(object? sender, EventArgs e)
        {
            if (ActiveAnimations.Count == 0)
            {
                UnhookRenderingIfIdle();
                return;
            }

            var completed = new List<ScrollViewer>();

            foreach (var entry in ActiveAnimations)
            {
                var sv = entry.Key;
                var state = entry.Value;

                if (!sv.IsVisible)
                {
                    completed.Add(sv);
                    continue;
                }

                var elapsed = DateTime.UtcNow - state.StartTime;
                var progress = state.Duration.TotalMilliseconds <= 0
                    ? 1.0
                    : Math.Clamp(elapsed.TotalMilliseconds / state.Duration.TotalMilliseconds, 0.0, 1.0);

                var eased = state.Easing?.Ease(progress) ?? progress;
                var offset = state.From + (state.To - state.From) * eased;
                sv.ScrollToVerticalOffset(offset);

                if (progress >= 1.0)
                {
                    completed.Add(sv);
                }
            }

            if (completed.Count > 0)
            {
                foreach (var sv in completed)
                {
                    if (ActiveAnimations.TryGetValue(sv, out var state))
                    {
                        sv.ScrollToVerticalOffset(state.To);
                    }

                    ActiveAnimations.Remove(sv);
                    SetIsAnimating(sv, false);
                }
            }

            UnhookRenderingIfIdle();
        }
    }
}