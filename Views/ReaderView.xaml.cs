using MSCS.Enums;
using MSCS.Helpers;
using MSCS.ViewModels;
using System;
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
        private ReaderViewModel? ViewModel => DataContext as ReaderViewModel;

        public ReaderView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }
        private async void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (sender is not ScrollViewer scrollViewer)
            {
                return;
            }

            var readerViewModel = ViewModel;
            if (readerViewModel != null)
            {
                if (readerViewModel.LayoutMode == ReaderLayoutMode.HorizontalScroll)
                {
                    readerViewModel.UpdateScrollPosition(
                        scrollViewer.HorizontalOffset,
                        scrollViewer.ExtentWidth,
                        scrollViewer.ViewportWidth);
                }
                else
                {
                    readerViewModel.UpdateScrollPosition(
                        scrollViewer.VerticalOffset,
                        scrollViewer.ExtentHeight,
                        scrollViewer.ViewportHeight);
                }
            }

            TryApplyPendingScroll();

            if (readerViewModel != null)
            {
                bool shouldLoadMore = false;
                if (readerViewModel.LayoutMode == ReaderLayoutMode.HorizontalScroll)
                {
                    if (e.HorizontalChange > 0 &&
                        scrollViewer.HorizontalOffset > 0 &&
                        scrollViewer.HorizontalOffset + scrollViewer.ViewportWidth >= scrollViewer.ExtentWidth - 100)
                    {
                        shouldLoadMore = true;
                    }
                }
                else
                {
                    if (e.VerticalChange > 0 &&
                        scrollViewer.VerticalOffset > 0 &&
                        scrollViewer.VerticalOffset + scrollViewer.ViewportHeight >= scrollViewer.ExtentHeight - 100)
                    {
                        shouldLoadMore = true;
                    }
                }

                if (shouldLoadMore)
                {
                    await readerViewModel.LoadMoreImagesAsync();
                    if (readerViewModel.RemainingImages == 0)
                    {
                        await GoToNextChapter(readerViewModel);
                    }
                }
            }
        }
        private void ScrollEventHandler(object sender, MouseButtonEventArgs e)
        {
            if (ScrollView == null || ViewModel == null || sender is not System.Windows.Controls.Image)
            {
                return;
            }

            var vm = ViewModel;
            var pos = e.GetPosition(ScrollView);
            var duration = TimeSpan.FromMilliseconds(Constants.DefaultSmoothScrollDuration);

            if (vm.LayoutMode == ReaderLayoutMode.HorizontalScroll)
            {
                double displayedWidth = ScrollView.ViewportWidth > 0 ? ScrollView.ViewportWidth : ScrollView.ActualWidth;
                if (displayedWidth <= 0)
                {
                    displayedWidth = 1;
                }

                double clickFraction = pos.X / displayedWidth;
                double scrollAmount = ScrollView.ViewportWidth * Constants.DefaultSmoothScrollPageFraction;
                if (scrollAmount <= 0)
                {
                    scrollAmount = ScrollView.ActualWidth * Constants.DefaultSmoothScrollPageFraction;
                }

                bool goBackward = clickFraction < 0.33;
                double delta = goBackward
                    ? (vm.IsRightToLeft ? scrollAmount : -scrollAmount)
                    : (vm.IsRightToLeft ? -scrollAmount : scrollAmount);

                SmoothScroll.ByHorizontal(ScrollView, delta, duration);
            }
            else
            {
                double displayedHeight = ScrollView.ViewportHeight > 0 ? ScrollView.ViewportHeight : ScrollView.ActualHeight;
                if (displayedHeight <= 0)
                {
                    displayedHeight = 1;
                }

                double clickFraction = pos.Y / displayedHeight;
                double scrollAmount = ScrollView.ViewportHeight * Constants.DefaultSmoothScrollPageFraction;
                if (scrollAmount <= 0)
                {
                    scrollAmount = ScrollView.ActualHeight * Constants.DefaultSmoothScrollPageFraction;
                }

                if (clickFraction < 0.33)
                {
                    SmoothScroll.By(ScrollView, -scrollAmount, duration);
                }
                else
                {
                    SmoothScroll.By(ScrollView, scrollAmount, duration);
                }
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
            if (ScrollView != null && vm.LayoutMode == ReaderLayoutMode.HorizontalScroll)
            {
                ScrollView.UpdateLayout();
                var target = vm.IsRightToLeft ? ScrollView.ScrollableWidth : 0;
                ScrollView.ScrollToHorizontalOffset(Math.Max(0, target));
            }
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
            if (ScrollView == null)
            {
                return;
            }

            ScrollView.Dispatcher.InvokeAsync(() =>
            {
                ScrollView.ScrollToTop();
                var vm = ViewModel;
                if (vm != null && vm.LayoutMode == ReaderLayoutMode.HorizontalScroll)
                {
                    ScrollView.UpdateLayout();
                    var target = vm.IsRightToLeft ? ScrollView.ScrollableWidth : 0;
                    ScrollView.ScrollToHorizontalOffset(Math.Max(0, target));
                }
            }, System.Windows.Threading.DispatcherPriority.Background);
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
            var isHorizontal = vm?.LayoutMode == ReaderLayoutMode.HorizontalScroll;
            var extent = isHorizontal ? ScrollView.ExtentWidth : ScrollView.ExtentHeight;
            var viewport = isHorizontal ? ScrollView.ViewportWidth : ScrollView.ViewportHeight;
            if (extent <= 0 || viewport <= 0)
            {
                return;
            }

            var scrollableLength = Math.Max(extent - viewport, 0);
            if (scrollableLength <= 0 && ((_pendingScrollProgress.HasValue && _pendingScrollProgress.Value > 0) || (_pendingScrollOffset.HasValue && _pendingScrollOffset.Value > 0)))
            {
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
            if (isHorizontal)
            {
                ScrollView.ScrollToHorizontalOffset(targetOffset);
            }
            else
            {
                ScrollView.ScrollToVerticalOffset(targetOffset);
            }

            var currentOffset = isHorizontal ? ScrollView.HorizontalOffset : ScrollView.VerticalOffset;
            var currentProgress = scrollableLength > 0 ? currentOffset / scrollableLength : 0;
            var offsetMatch = _pendingScrollOffset.HasValue
                ? Math.Abs(currentOffset - _pendingScrollOffset.Value) <= Math.Max(1.0, (isHorizontal ? ScrollView.ViewportWidth : ScrollView.ViewportHeight) * 0.01)
                : false;
            var progressMatch = _pendingScrollProgress.HasValue
                ? Math.Abs(currentProgress - _pendingScrollProgress.Value) <= 0.01
                : false;
            if (offsetMatch || progressMatch)
            {
                _pendingScrollProgress = null;
                _pendingScrollOffset = null;
                vm?.NotifyScrollRestoreCompleted();
            }
            else
            {
                // Keep the pending progress so subsequent scroll/extent changes can retry.
            }
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

            var vm = ViewModel;
            bool isHorizontal = vm.LayoutMode == ReaderLayoutMode.HorizontalScroll;
            double viewport = isHorizontal ? ScrollView.ViewportWidth : ScrollView.ViewportHeight;
            if (viewport <= 0)
            {
                viewport = isHorizontal ? ScrollView.ActualWidth : ScrollView.ActualHeight;
            }

            double amount = viewport * Constants.DefaultSmoothScrollPageFraction;
            var duration = TimeSpan.FromMilliseconds(Constants.DefaultSmoothScrollDuration);

            void ScrollForward()
            {
                if (isHorizontal)
                {
                    var delta = vm.IsRightToLeft ? -amount : amount;
                    SmoothScroll.ByHorizontal(ScrollView, delta, duration);
                }
                else
                {
                    SmoothScroll.By(ScrollView, amount, duration);
                }
            }

            void ScrollBackward()
            {
                if (isHorizontal)
                {
                    var delta = vm.IsRightToLeft ? amount : -amount;
                    SmoothScroll.ByHorizontal(ScrollView, delta, duration);
                }
                else
                {
                    SmoothScroll.By(ScrollView, -amount, duration);
                }
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
                    if (!isHorizontal)
                    {
                        ScrollForward();
                        e.Handled = true;
                    }
                    break;
                case Key.PageUp:
                case Key.Up:
                    if (!isHorizontal)
                    {
                        ScrollBackward();
                        e.Handled = true;
                    }
                    break;
                case Key.Right:
                    if (isHorizontal)
                    {
                        if (vm.IsRightToLeft)
                        {
                            ScrollBackward();
                        }
                        else
                        {
                            ScrollForward();
                        }
                    }
                    else
                    {
                        ScrollForward();
                    }
                    e.Handled = true;
                    break;
                case Key.Left:
                    if (isHorizontal)
                    {
                        if (vm.IsRightToLeft)
                        {
                            ScrollForward();
                        }
                        else
                        {
                            ScrollBackward();
                        }
                    }
                    else
                    {
                        ScrollBackward();
                    }
                    e.Handled = true;
                    break;
            }
        }
    }
}