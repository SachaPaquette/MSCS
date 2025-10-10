using MSCS.Helpers;
using MSCS.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
namespace MSCS.Views
{
    public partial class ReaderView : System.Windows.Controls.UserControl
    {
        private double? _pendingScrollProgress;
        private double? _pendingScrollOffset;

        public ReaderView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }
        private async void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer == null)
                return;
            if (DataContext is ReaderViewModel readerViewModel)
            {
                readerViewModel.UpdateScrollPosition(
                    scrollViewer.VerticalOffset,
                    scrollViewer.ExtentHeight,
                    scrollViewer.ViewportHeight);
            }

            TryApplyPendingScroll();

            if (e.VerticalChange > 0 &&
                scrollViewer.VerticalOffset > 0 &&
                scrollViewer.VerticalOffset + scrollViewer.ViewportHeight >= scrollViewer.ExtentHeight - 100)
            {
                if (DataContext is ReaderViewModel viewModel)
                {
                    await viewModel.LoadMoreImagesAsync();
                    if (viewModel.RemainingImages == 0)
                    {
                        await GoToNextChapter(viewModel);
                    }
                }

            }
        }
        private void ScrollEventHandler(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.Image image && ScrollView != null)
            {
                // Get mouse position relative to the ScrollView
                var pos = e.GetPosition(ScrollView);

                // Use the visible area of the ScrollView, not the full image
                double displayedHeight = ScrollView.ViewportHeight;
                double clickFraction = pos.Y / displayedHeight;

                double scrollAmount = ScrollView.ViewportHeight * Constants.DefaultSmoothScrollPageFraction;
                TimeSpan duration = TimeSpan.FromMilliseconds(Constants.DefaultSmoothScrollDuration);

                if (clickFraction < 0.33)
                {
                    SmoothScroll.By(ScrollView, -scrollAmount, duration);
                }
                else
                {
                    SmoothScroll.By(ScrollView, scrollAmount, duration);
                }
            }
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
            if (ScrollView == null)
            {
                return;
            }

            ScrollView.Dispatcher.InvokeAsync(() =>
            {
                ScrollView.ScrollToTop();
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
            var extent = ScrollView.ExtentHeight;
            var viewport = ScrollView.ViewportHeight;
            if (extent <= 0 || viewport <= 0)
            {
                return;
            }

            var scrollableHeight = Math.Max(extent - viewport, 0);
            if (scrollableHeight <= 0 && ((_pendingScrollProgress.HasValue && _pendingScrollProgress.Value > 0) || (_pendingScrollOffset.HasValue && _pendingScrollOffset.Value > 0)))
            {
                return;
            }

            double targetOffset;
            if (_pendingScrollOffset.HasValue)
            {
                targetOffset = Math.Clamp(_pendingScrollOffset.Value, 0, scrollableHeight);
            }
            else
            {
                targetOffset = Math.Clamp(_pendingScrollProgress!.Value * scrollableHeight, 0, scrollableHeight);
            }
            ScrollView.ScrollToVerticalOffset(targetOffset);

            var currentOffset = ScrollView.VerticalOffset;
            var currentProgress = scrollableHeight > 0 ? currentOffset / scrollableHeight : 0;
            var offsetMatch = _pendingScrollOffset.HasValue
                ? Math.Abs(currentOffset - _pendingScrollOffset.Value) <= Math.Max(1.0, ScrollView.ViewportHeight * 0.01)
                : false;
            var progressMatch = _pendingScrollProgress.HasValue
                ? Math.Abs(currentProgress - _pendingScrollProgress.Value) <= 0.01
                : false;
            if (offsetMatch || progressMatch)
            {
                _pendingScrollProgress = null;
                _pendingScrollOffset = null;
                if (DataContext is ReaderViewModel readerViewModel)
                {
                    readerViewModel.NotifyScrollRestoreCompleted();
                }
            }
            else
            {
                // Keep the pending progress so subsequent scroll/extent changes can retry.
            }
        }
    }
}