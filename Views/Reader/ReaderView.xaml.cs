using MSCS.Enums;
using MSCS.Helpers;
using MSCS.ViewModels;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace MSCS.Views
{
    public partial class ReaderView : System.Windows.Controls.UserControl
    {
        private double? _pendingScrollProgress;
        private double? _pendingScrollOffset;
        private bool _pendingScrollRetryScheduled;
        private int _scrollRestoreAttemptCount;
        private bool _suppressAutoAdvance;

        private ReaderViewModel? ViewModel => DataContext as ReaderViewModel;

        public ReaderView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private async void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (sender is not ScrollViewer sv) return;

            var vm = ViewModel;
            vm?.UpdateScrollPosition(sv.VerticalOffset, sv.ExtentHeight, sv.ViewportHeight);

            TryApplyPendingScroll();

            if (vm?.IsRestoringProgress == true || vm?.IsInRestoreCooldown == true)
                return;

            double viewport = sv.ViewportHeight > 0 ? sv.ViewportHeight : sv.ActualHeight;
            double threshold = Math.Max(400, viewport * 1.25);
            double distanceToBottom = Math.Max(0, sv.ScrollableHeight - sv.VerticalOffset);

            if (distanceToBottom <= threshold)
            {
                try
                {
                    await vm!.LoadMoreImagesAsync();
                    if (vm.RemainingImages == 0)
                        await GoToNextChapter(vm);
                }
                catch (OperationCanceledException) { Debug.WriteLine("Image loading cancelled during scroll."); }
            }
        }


        private void OnScrollRestoreRequested(object? sender, ScrollRestoreRequest request)
        {
            if (ScrollView == null)
            {
                return;
            }

            _pendingScrollProgress = request.NormalizedProgress.HasValue
                ? Math.Clamp(request.NormalizedProgress.Value, 0.0, 1.0)
                : null;
            _pendingScrollOffset = request.ScrollOffset;
            _scrollRestoreAttemptCount = 0;
            _suppressAutoAdvance = true;
            ScrollView.Dispatcher.InvokeAsync(() =>
            {
                TryApplyPendingScroll();
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void TryApplyPendingScroll()
        {
            if (ScrollView == null || (!_pendingScrollProgress.HasValue && !_pendingScrollOffset.HasValue))
            {
                return;
            }

            ScrollView.UpdateLayout();
            var vm = ViewModel;
            var extent = ScrollView.ExtentHeight;
            var viewport = ScrollView.ViewportHeight;
            if (extent <= 0 || viewport <= 0)
            {
                SchedulePendingScrollRetry();
                return;
            }

            var scrollableLength = Math.Max(extent - viewport, 0);
            if (scrollableLength <= 0)
            {
                if (ShouldAbortRestore())
                {
                    FinalizeScrollRestore(vm);
                }
                else
                {
                    SchedulePendingScrollRetry();
                }
                return;
            }

            double targetOffset;
            if (_pendingScrollOffset.HasValue)
            {
                targetOffset = Math.Clamp(_pendingScrollOffset.Value, 0, scrollableLength);
            }
            else
            {
                targetOffset = Math.Clamp(_pendingScrollProgress!.Value * scrollableLength, 0, scrollableLength);
            }

            ScrollView.ScrollToVerticalOffset(targetOffset);

            var currentOffset = ScrollView.VerticalOffset;
            var currentProgress = scrollableLength > 0 ? currentOffset / scrollableLength : 0;

            var tolerance = Math.Max(1.0, ScrollView.ViewportHeight * 0.01);

            bool offsetMatch = Math.Abs(currentOffset - targetOffset) <= tolerance;

            bool progressMatch = _pendingScrollProgress.HasValue
                ? Math.Abs(currentProgress - _pendingScrollProgress.Value) <= 0.01
                : false;

            if (offsetMatch || progressMatch)
            {
                _scrollRestoreAttemptCount = 0;
                _pendingScrollProgress = null;
                _pendingScrollOffset = null;
                ViewModel?.NotifyScrollRestoreCompleted();
            }
            else
            {
                if (ShouldAbortRestore()) FinalizeScrollRestore(ViewModel);
                else SchedulePendingScrollRetry();
            }

        }

        private void SchedulePendingScrollRetry()
        {
            if (_pendingScrollRetryScheduled || ScrollView == null)
            {
                return;
            }

            _pendingScrollRetryScheduled = true;
            ScrollView.Dispatcher.InvokeAsync(() =>
            {
                _pendingScrollRetryScheduled = false;
                TryApplyPendingScroll();
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private bool ShouldAbortRestore()
        {
            if (!_pendingScrollProgress.HasValue && !_pendingScrollOffset.HasValue)
            {
                return true;
            }

            _scrollRestoreAttemptCount++;
            const int maxAttempts = 10;
            return _scrollRestoreAttemptCount >= maxAttempts;
        }

        private void FinalizeScrollRestore(ReaderViewModel? vm)
        {
            _pendingScrollProgress = null;
            _pendingScrollOffset = null;
            _scrollRestoreAttemptCount = 0;
            vm?.NotifyScrollRestoreCompleted();
        }

        private void ScrollEventHandler(object sender, MouseButtonEventArgs e)
        {
            if (ScrollView == null || ViewModel == null || sender is not System.Windows.Controls.Image)
            {
                return;
            }

            var pos = e.GetPosition(ScrollView);
            var vm = ViewModel;
            var duration = vm?.ScrollDuration ?? TimeSpan.FromMilliseconds(Constants.DefaultSmoothScrollDuration);
            double displayedHeight = ScrollView.ViewportHeight > 0 ? ScrollView.ViewportHeight : ScrollView.ActualHeight;
            if (displayedHeight <= 0)
            {
                displayedHeight = 1;
            }

            double clickFraction = pos.Y / displayedHeight;
            var fraction = vm?.ScrollPageFraction ?? Constants.DefaultSmoothScrollPageFraction;
            double scrollAmount = ScrollView.ViewportHeight * fraction;
            if (scrollAmount <= 0)
            {
                scrollAmount = ScrollView.ActualHeight * fraction;
            }

            if (clickFraction < 0.33)
            {
                SmoothScroll.By(ScrollView, -scrollAmount, duration);
            }
            else
            {
                SmoothScroll.By(ScrollView, scrollAmount, duration);
            }

            e.Handled = true;
        }

        private void Image_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ScrollEventHandler(sender, e);
        }


        private async Task GoToNextChapter(ReaderViewModel vm)
        {
            await vm.GoToNextChapterAsync();
            ScrollView.ScrollToTop();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is ReaderViewModel oldViewModel)
            {
                oldViewModel.ChapterChanged -= OnChapterChanged;
                oldViewModel.ScrollRestoreRequested -= OnScrollRestoreRequested;
            }

            if (e.NewValue is ReaderViewModel newViewModel)
            {
                newViewModel.ChapterChanged += OnChapterChanged;
                newViewModel.ScrollRestoreRequested += OnScrollRestoreRequested;
            }
        }

        private void OnChapterChanged(object? sender, EventArgs e)
        {
            if (ScrollView == null) return;
            var vm = ViewModel;
            if (vm?.IsRestoringProgress == true) return; 

            ScrollView.Dispatcher.InvokeAsync(() => ScrollView.ScrollToTop(),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (ScrollView != null)
            {
                ScrollView.Focus();
                Keyboard.Focus(ScrollView);
            }
            else
            {
                Focus();
            }
        }

        private void UserControl_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (ScrollView == null || ViewModel == null)
            {
                return;
            }

            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != ModifierKeys.None)
            {
                return;
            }

            double viewport = ScrollView.ViewportHeight;
            if (viewport <= 0)
            {
                viewport = ScrollView.ActualHeight;
            }

            var fraction = ViewModel.ScrollPageFraction;
            if (fraction <= 0)
            {
                fraction = Constants.DefaultSmoothScrollPageFraction;
            }

            double amount = viewport * fraction;
            var duration = ViewModel.ScrollDuration;

            void ScrollForward()
            {
                SmoothScroll.By(ScrollView, amount, duration);
            }

            void ScrollBackward()
            {
                SmoothScroll.By(ScrollView, -amount, duration);
            }

            switch (e.Key)
            {
                case Key.Space:
                    if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                    {
                        ScrollBackward();
                    }
                    else
                    {
                        ScrollForward();
                    }
                    e.Handled = true;
                    break;
                case Key.PageDown:
                case Key.Down:
                    ScrollForward();
                    e.Handled = true;
                    break;
                case Key.PageUp:
                case Key.Up:
                    ScrollBackward();
                    e.Handled = true;
                    break;
                case Key.Right:
                    if (ViewModel.NextChapterCommand?.CanExecute(null) == true)
                    {
                        ViewModel.NextChapterCommand.Execute(null);
                        e.Handled = true;
                    }
                    else
                    {
                        ScrollForward();
                        e.Handled = true;
                    }
                    break;
                case Key.Left:
                    if (ViewModel.PreviousChapterCommand?.CanExecute(null) == true)
                    {
                        ViewModel.PreviousChapterCommand.Execute(null);
                        e.Handled = true;
                    }
                    else
                    {
                        ScrollBackward();
                        e.Handled = true;
                    }
                    break;
            }
        }
    }
}