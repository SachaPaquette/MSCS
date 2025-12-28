using MSCS.Enums;
using MSCS.Helpers;
using MSCS.Models;
using MSCS.ViewModels;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Point = System.Windows.Point;
namespace MSCS.Views
{
    public partial class ReaderView : System.Windows.Controls.UserControl
    {
        private double? _pendingScrollProgress;
        private double? _pendingScrollOffset;
        private string? _pendingAnchorImageUrl;
        private bool _pendingScrollRetryScheduled;
        private double? _pendingAnchorImageProgress;
        private int? _pendingAnchorIndex;
        private bool _anchorBringIntoViewRequested;
        private bool _isApplyingPendingScroll;
        private bool _layoutValidationPending;
        private double? _lastRequestedScrollOffset;
        private bool _suppressAutoAdvance;
        private bool _isFullscreen;
        private WindowState _restoreWindowState;
        private WindowStyle _restoreWindowStyle;
        private ResizeMode _restoreResizeMode;
        private Window? _hostWindow;
        private double? _restoreWidthFactor;
        private int _suppressViewportRestoreCount;
        private DateTime _lastAnchorUpdate = DateTime.MinValue;
        private bool _loadMoreScheduled;

        public static readonly DependencyProperty IsFullscreenModeProperty =
            DependencyProperty.Register(nameof(IsFullscreenMode), typeof(bool), typeof(ReaderView), new PropertyMetadata(false));

        public bool IsFullscreenMode
        {
            get => (bool)GetValue(IsFullscreenModeProperty);
            private set => SetValue(IsFullscreenModeProperty, value);
        }




        private ScrollViewer? _scrollViewer;
        private ReaderViewModel? _viewModel;
        private ReaderViewModel? ViewModel => _viewModel;

        private ScrollViewer? ScrollView => _scrollViewer;

        public ReaderView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            UpdateFullscreenButtonVisuals();
        }

        private void ImageList_Loaded(object sender, RoutedEventArgs e)
        {
            AttachScrollViewer();
            if (ImageList != null)
            {
                ImageList.ItemContainerGenerator.StatusChanged -= OnItemContainerGeneratorStatusChanged;
                ImageList.ItemContainerGenerator.StatusChanged += OnItemContainerGeneratorStatusChanged;
            }
        }

        private void AttachScrollViewer()
        {
            if (_scrollViewer != null)
            {
                return;
            }

            if (ImageList == null)
            {
                return;
            }

            _scrollViewer = FindDescendant<ScrollViewer>(ImageList);
            if (_scrollViewer != null)
            {
                _scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;

                if (_pendingScrollOffset.HasValue || _pendingScrollProgress.HasValue)
                {
                    _scrollViewer.Dispatcher.InvokeAsync(
                        TryApplyPendingScroll,
                        System.Windows.Threading.DispatcherPriority.Background);
                }
            }
        }

        private void DetachScrollViewer()
        {
            if (_scrollViewer != null)
            {
                CancelLayoutValidation();
                _scrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;
                _scrollViewer = null;
            }
        }

        private void ImageList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_pendingScrollOffset.HasValue || _pendingScrollProgress.HasValue)
            {
                ClearPendingScrollRestoration();
            }

            if (ScrollView != null)
            {
                SmoothScroll.Cancel(ScrollView);
            }

            _suppressViewportRestoreCount = 1;
        }

        private static T? FindDescendant<T>(DependencyObject? root)
            where T : DependencyObject
        {
            if (root == null)
            {
                return null;
            }

            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T typedChild)
                {
                    return typedChild;
                }

                var descendant = FindDescendant<T>(child);
                if (descendant != null)
                {
                    return descendant;
                }
            }

            return null;
        }

        private void OnItemContainerGeneratorStatusChanged(object? sender, EventArgs e)
        {
            if (ImageList?.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated)
            {
                TryApplyPendingScroll();
            }
        }

        private void ScheduleLoadMoreImages()
        {
            if (_loadMoreScheduled || ViewModel == null) return;
            _loadMoreScheduled = true;

            var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            dispatcher.InvokeAsync(async () =>
            {
                try { await ViewModel.LoadMoreImagesAsync(); }
                catch (OperationCanceledException) { }
                finally { _loadMoreScheduled = false; }
            }, DispatcherPriority.Background);
        }

        private async void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (sender is not ScrollViewer sv) return;

            if (SmoothScroll.GetIsAnimating(sv))
                return;

            var vm = ViewModel;
            double? previousProgress = vm?.ScrollProgress;
            vm?.UpdateScrollPosition(sv.VerticalOffset, sv.ExtentHeight, sv.ViewportHeight);

            if (_pendingScrollOffset.HasValue || _pendingScrollProgress.HasValue)
                TryApplyPendingScroll();

            if (vm != null)
            {
                var now = DateTime.UtcNow;
                if ((now - _lastAnchorUpdate).TotalMilliseconds >= 100)
                {
                    _lastAnchorUpdate = now;

                    var anchorInfo = GetCurrentScrollAnchor();
                    if (anchorInfo.HasValue)
                    {
                        var (anchorUrl, anchorProgress) = anchorInfo.Value;
                        vm.UpdateScrollAnchor(anchorUrl, anchorProgress);
                    }
                    else
                    {
                        vm.UpdateScrollAnchor(null, null);
                    }
                }
            }

            bool viewportChanged = Math.Abs(e.ViewportHeightChange) > 0.1 || Math.Abs(e.ViewportWidthChange) > 0.1;
            if (viewportChanged)
            {
                if (_suppressViewportRestoreCount > 0)
                {
                    _suppressViewportRestoreCount--;
                }
                else if (vm != null && !_pendingScrollOffset.HasValue && !_pendingScrollProgress.HasValue)
                {
                    if (!vm.IsRestoringProgress && !vm.IsInRestoreCooldown)
                    {
                        double? targetProgress = previousProgress;
                        if (!targetProgress.HasValue)
                        {
                            targetProgress = GetCurrentNormalizedScrollProgress();
                        }

                        if (targetProgress.HasValue)
                        {
                            QueueScrollRestore(null, targetProgress.Value);
                        }
                    }
                }
            }

            if (vm?.IsRestoringProgress == true || vm?.IsInRestoreCooldown == true)
                return;

            double viewport = sv.ViewportHeight > 0 ? sv.ViewportHeight : sv.ActualHeight;
            double loadMoreThreshold = Math.Max(400, viewport * 1.25);
            double distanceToBottom = Math.Max(0, sv.ScrollableHeight - sv.VerticalOffset);

            if (distanceToBottom <= loadMoreThreshold)
            {
                ScheduleLoadMoreImages();
                distanceToBottom = Math.Max(0, sv.ScrollableHeight - sv.VerticalOffset);
            }

            if (vm != null)
            {
                double previewThreshold = Math.Max(1.0, viewport * 0.05);
                bool shouldShowPreview = !_suppressAutoAdvance &&
                                         vm.RemainingImages == 0 &&
                                         distanceToBottom <= previewThreshold;

                vm.SetChapterTransitionPreviewVisible(shouldShowPreview);
            }
        }

        private void OnScrollRestoreRequested(object? sender, ScrollRestoreRequest request)
        {
            QueueScrollRestore(
                request.ScrollOffset,
                request.NormalizedProgress,
                request.AnchorImageUrl,
                request.AnchorImageProgress);
        }

        private void OnRestoreTargetReady(object? sender, EventArgs e)
        {
            if (ScrollView == null)
            {
                return;
            }

            ScrollView.Dispatcher.InvokeAsync(TryApplyPendingScroll, DispatcherPriority.Background);
        }


        private void TryApplyPendingScroll()
        {
            if (ScrollView == null || (!_pendingScrollOffset.HasValue && !_pendingScrollProgress.HasValue))
            {
                return;
            }

            if (_isApplyingPendingScroll)
            {
                return;
            }

            if (!EnsureAnchorReady())
            {
                return;
            }

            var vm = ViewModel;
            ScrollView.UpdateLayout();

            var extent = ScrollView.ExtentHeight;
            var viewport = ScrollView.ViewportHeight;
            if (extent <= 0 || viewport <= 0)
            {
                if (vm?.RemainingImages > 0)
                {
                    _ = vm.LoadMoreImagesAsync();
                }

                _lastRequestedScrollOffset = null;
                WaitForLayoutUpdate();
                return;
            }

            var scrollableLength = Math.Max(extent - viewport, 0);
            if (scrollableLength <= 0)
            {
                if (vm?.RemainingImages > 0)
                {
                    _ = vm.LoadMoreImagesAsync();
                    WaitForLayoutUpdate();
                    return;
                }

                FinalizeScrollRestore(vm);
                return;
            }

            var targetOffset = DetermineTargetOffset(scrollableLength);
            if (targetOffset > scrollableLength && (vm?.RemainingImages ?? 0) > 0)
            {
                _ = vm!.LoadMoreImagesAsync();
                _lastRequestedScrollOffset = null;
                WaitForLayoutUpdate();
                return;
            }

            _isApplyingPendingScroll = true;
            _lastRequestedScrollOffset = targetOffset;
            ScrollView.ScrollToVerticalOffset(targetOffset);
            _isApplyingPendingScroll = false;

            WaitForLayoutUpdate();
        }

        private void WaitForLayoutUpdate()
        {
            if (ScrollView == null || _layoutValidationPending)
            {
                return;
            }

            _layoutValidationPending = true;
            ScrollView.LayoutUpdated += OnScrollViewerLayoutUpdated;
        }


        private void CancelLayoutValidation()
        {
            if (ScrollView != null && _layoutValidationPending)
            {
                ScrollView.LayoutUpdated -= OnScrollViewerLayoutUpdated;
            }

            _layoutValidationPending = false;
        }

        private void OnScrollViewerLayoutUpdated(object? sender, EventArgs e)
        {
            if (ScrollView == null)
            {
                _layoutValidationPending = false;
                return;
            }

            ScrollView.LayoutUpdated -= OnScrollViewerLayoutUpdated;
            _layoutValidationPending = false;

            if (!_pendingScrollOffset.HasValue && !_pendingScrollProgress.HasValue)
            {
                _lastRequestedScrollOffset = null;
                return;
            }

            if (!_lastRequestedScrollOffset.HasValue)
            {
                ScrollView.Dispatcher.InvokeAsync(TryApplyPendingScroll, DispatcherPriority.Background);
                return;
            }

            if (!ValidatePendingScrollRestore())
            {
                // Validation will resubscribe if further layout updates are required.
            }
        }

        private bool ValidatePendingScrollRestore()
        {
            if (ScrollView == null)
            {
                return true;
            }

            var vm = ViewModel;
            ScrollView.UpdateLayout();

            var extent = ScrollView.ExtentHeight;
            var viewport = ScrollView.ViewportHeight;
            if (extent <= 0 || viewport <= 0)
            {
                _lastRequestedScrollOffset = null;
                WaitForLayoutUpdate();
                return false;
            }

            var scrollableLength = Math.Max(extent - viewport, 0);
            var targetOffset = DetermineTargetOffset(scrollableLength);
            var currentOffset = ScrollView.VerticalOffset;
            var currentProgress = scrollableLength > 0 ? currentOffset / scrollableLength : 0.0;
            var tolerance = Math.Max(1.0, ScrollView.ViewportHeight * 0.01);

            bool offsetMatch = Math.Abs(currentOffset - targetOffset) <= tolerance;
            bool progressMatch = _pendingScrollProgress.HasValue && scrollableLength > 0
                ? Math.Abs(currentProgress - _pendingScrollProgress.Value) <= 0.01
                : false;

            if (offsetMatch || (!_pendingScrollOffset.HasValue && progressMatch))
            {
                FinalizeScrollRestore(vm);
                return true;
            }

            bool hasMoreImages = vm?.RemainingImages > 0;
            bool generatorBusy = ImageList?.ItemContainerGenerator?.Status != GeneratorStatus.ContainersGenerated;
            bool awaitingAnchor = !AnchorContainerRealized();

            if (hasMoreImages)
            {
                _ = vm!.LoadMoreImagesAsync();
                _lastRequestedScrollOffset = null;
            }

            if (hasMoreImages || generatorBusy || awaitingAnchor)
            {
                WaitForLayoutUpdate();
                return false;
            }

            FinalizeScrollRestore(vm);
            return true;
        }

        private double DetermineTargetOffset(double scrollableLength)
        {
            if (ScrollView == null)
            {
                return 0;
            }

            if (_pendingAnchorImageUrl != null && _pendingAnchorIndex.HasValue && ImageList != null)
            {
                if (ImageList.ItemContainerGenerator.ContainerFromIndex(_pendingAnchorIndex.Value) is FrameworkElement container)
                {
                    try
                    {
                        var relative = container.TransformToVisual(ScrollView).Transform(new Point(0, 0));
                        double anchorProgress = Math.Clamp(_pendingAnchorImageProgress ?? 0.0, 0.0, 1.0);
                        double absoluteTop = ScrollView.VerticalOffset + relative.Y;
                        double desired = absoluteTop + anchorProgress * container.ActualHeight;
                        return Math.Clamp(desired, 0, scrollableLength);
                    }
                    catch (InvalidOperationException)
                    {
                        // fall back to raw offsets
                    }
                }
            }

            if (_pendingScrollOffset.HasValue)
            {
                return Math.Clamp(_pendingScrollOffset.Value, 0, scrollableLength);
            }

            return Math.Clamp(_pendingScrollProgress!.Value * scrollableLength, 0, scrollableLength);
        }

        private bool EnsureAnchorReady()
        {
            if (string.IsNullOrEmpty(_pendingAnchorImageUrl))
            {
                return true;
            }

            if (ImageList == null || ViewModel == null)
            {
                return false;
            }

            if (!_pendingAnchorIndex.HasValue)
            {
                var index = ViewModel.GetImageIndex(_pendingAnchorImageUrl);
                if (index < 0)
                {
                    _pendingAnchorImageUrl = null;
                    _pendingAnchorImageProgress = null;
                    _pendingAnchorIndex = null;
                    return true;
                }

                _pendingAnchorIndex = index;
            }

            if (!ViewModel.IsImageLoaded(_pendingAnchorIndex.Value))
            {
                _ = ViewModel.LoadMoreImagesAsync();
                _lastRequestedScrollOffset = null;
                WaitForLayoutUpdate();
                return false;
            }

            if (_pendingAnchorIndex.Value < 0 || _pendingAnchorIndex.Value >= ImageList.Items.Count)
            {
                WaitForLayoutUpdate();
                return false;
            }

            var container = ImageList.ItemContainerGenerator.ContainerFromIndex(_pendingAnchorIndex.Value) as FrameworkElement;
            if (container == null)
            {
                if (!_anchorBringIntoViewRequested)
                {
                    _anchorBringIntoViewRequested = true;
                    ImageList.ScrollIntoView(ImageList.Items[_pendingAnchorIndex.Value]);
                }

                WaitForLayoutUpdate();
                return false;
            }

            if (!container.IsLoaded || container.ActualHeight <= 0)
            {
                WaitForLayoutUpdate();
                return false;
            }

            _anchorBringIntoViewRequested = false;
            return true;
        }

        private bool AnchorContainerRealized()
        {
            if (string.IsNullOrEmpty(_pendingAnchorImageUrl) || ImageList == null || ViewModel == null)
            {
                return true;
            }

            if (!_pendingAnchorIndex.HasValue)
            {
                var index = ViewModel.GetImageIndex(_pendingAnchorImageUrl);
                if (index < 0)
                {
                    return true;
                }

                _pendingAnchorIndex = index;
            }

            return ImageList.ItemContainerGenerator.ContainerFromIndex(_pendingAnchorIndex.Value) is FrameworkElement container
                && container.IsLoaded
                && container.ActualHeight > 0;
        }

        private void Images_CleanUpVirtualizedItem(object sender, CleanUpVirtualizedItemEventArgs e)
        {
            if (e.Value is ChapterImage image)
            {
                ViewModel?.ChapterCoordinator?.ReleaseImage(image);
            }
        }

        private void FinalizeScrollRestore(ReaderViewModel? vm)
        {
            ClearPendingScrollRestoration();
            vm?.NotifyScrollRestoreCompleted();
        }

        private void ScrollEventHandler(object sender, MouseButtonEventArgs e)
        {
            if (ScrollView == null || ViewModel == null || sender is not System.Windows.Controls.Image)
                return;

            ClearPendingScrollRestoration();
            SmoothScroll.Cancel(ScrollView);
            _suppressViewportRestoreCount = 2;

            var displayedHeight = ScrollView.ViewportHeight > 0
              ? ScrollView.ViewportHeight
              : ScrollView.ActualHeight;
            if (displayedHeight <= 0) displayedHeight = 1;

            var clickFraction = e.GetPosition(ScrollView).Y / displayedHeight;

            var fraction = ViewModel.Preferences.ScrollPageFraction;
            var baseHeight = ScrollView.ViewportHeight > 0
              ? ScrollView.ViewportHeight
              : ScrollView.ActualHeight;

            var scrollAmount = baseHeight * fraction;
            if (scrollAmount <= 0) scrollAmount = 1;

            SmoothScroll.By(
              ScrollView,
              clickFraction < 0.33 ? -scrollAmount : scrollAmount,
              ViewModel.Preferences.ScrollDuration);

            e.Handled = true;
        }

        private void Image_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ScrollEventHandler(sender, e);
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is ReaderViewModel oldViewModel)
            {
                oldViewModel.ChapterChanged -= OnChapterChanged;
                oldViewModel.ScrollRestoreRequested -= OnScrollRestoreRequested;
                oldViewModel.RestoreTargetReady -= OnRestoreTargetReady;
                oldViewModel.Dispose();
            }

            if (e.NewValue is ReaderViewModel newViewModel)
            {
                _viewModel = newViewModel;
                newViewModel.ChapterChanged += OnChapterChanged;
                newViewModel.ScrollRestoreRequested += OnScrollRestoreRequested;
                newViewModel.RestoreTargetReady += OnRestoreTargetReady;
            }
            else
            {
                _viewModel = null;
            }
        }

        private void OnChapterChanged(object? sender, EventArgs e)
        {
            if (ScrollView == null) return;
            var vm = ViewModel;
            if (vm?.IsRestoringProgress == true) return;

            ClearPendingScrollRestoration();
            _suppressViewportRestoreCount = 3;

            ScrollView.Dispatcher.InvokeAsync(() => ScrollView.ScrollToTop(),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            AttachScrollViewer();
            if (ScrollView != null)
            {
                ScrollView.Focus();
                Keyboard.Focus(ScrollView);
            }
            else
            {
                Focus();
            }

            UpdateFullscreenButtonVisuals();
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            if (ImageList != null)
            {
                ImageList.ItemContainerGenerator.StatusChanged -= OnItemContainerGeneratorStatusChanged;
            }
            DetachScrollViewer();
            ExitFullscreen();
            ViewModel?.Dispose();
        }

        private async void UserControl_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
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

            var fraction = ViewModel.Preferences.ScrollPageFraction;
            if (fraction <= 0)
            {
                fraction = Constants.DefaultSmoothScrollPageFraction;
            }

            double amount = viewport * fraction;
            var duration = ViewModel.Preferences.ScrollDuration;

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
                    else if (ViewModel.CanNavigateToNextChapter)
                    {
                        try
                        {
                            await ViewModel.GoToNextChapterAsync();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to navigate to next chapter via keyboard: {ex.Message}");
                        }

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
                    else if (ViewModel.CanNavigateToPreviousChapter)
                    {
                        try
                        {
                            await ViewModel.GoToPreviousChapterAsync();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to navigate to previous chapter via keyboard: {ex.Message}");
                        }

                        e.Handled = true;
                    }
                    else
                    {
                        ScrollBackward();
                        e.Handled = true;
                    }
                    break;
                case Key.F11:
                    ToggleFullscreen();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    if (_isFullscreen)
                    {
                        ExitFullscreen();
                        e.Handled = true;
                    }
                    break;
            }
        }

        private void FullscreenButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleFullscreen();
        }

        private void ToggleFullscreen()
        {
            var (_, progress) = CaptureScrollState();
            if (_isFullscreen)
            {
                ExitFullscreen();
            }
            else
            {
                EnterFullscreen();
            }

            if (progress.HasValue)
            {
                QueueScrollRestore(null, progress.Value);
            }
        }

        private void EnterFullscreen()
        {
            var window = Window.GetWindow(this);
            if (window == null || _isFullscreen)
            {
                return;
            }

            _hostWindow = window;
            _restoreWindowState = window.WindowState;
            _restoreWindowStyle = window.WindowStyle;
            _restoreResizeMode = window.ResizeMode;

            window.WindowStyle = WindowStyle.None;
            window.ResizeMode = ResizeMode.NoResize;
            window.WindowState = WindowState.Maximized;

            if (ViewModel is ReaderViewModel vm)
            {
                if (!_restoreWidthFactor.HasValue)
                {
                    _restoreWidthFactor = vm.Preferences.WidthFactor;
                }

                vm.Preferences.WidthFactor = 1.0;
            }

            _isFullscreen = true;
            IsFullscreenMode = true;
            UpdateFullscreenButtonVisuals();
            UpdateShellFullscreenState(true);
        }

        private void ExitFullscreen()
        {
            if (!_isFullscreen)
            {
                return;
            }

            var window = _hostWindow ?? Window.GetWindow(this);
            if (window != null)
            {
                window.WindowStyle = _restoreWindowStyle;
                window.ResizeMode = _restoreResizeMode;
                window.WindowState = _restoreWindowState;
            }

            if (ViewModel is ReaderViewModel vm && _restoreWidthFactor.HasValue)
            {
                vm.Preferences.WidthFactor = _restoreWidthFactor.Value;
            }

            _restoreWidthFactor = null;
            _hostWindow = null;
            _isFullscreen = false;
            IsFullscreenMode = false;
            UpdateFullscreenButtonVisuals();
            UpdateShellFullscreenState(false);
        }

        private void UpdateFullscreenButtonVisuals()
        {
            if (FullscreenButtonIcon != null)
            {
                FullscreenButtonIcon.Text = _isFullscreen ? "\uE73F" : "\uE740";
            }

            if (FullscreenButton != null)
            {
                FullscreenButton.ToolTip = _isFullscreen ? "Exit fullscreen" : "Enter fullscreen";
            }
        }

        private void UpdateShellFullscreenState(bool isFullscreen)
        {
            if (Application.Current?.MainWindow is MainWindow mainWindow &&
                mainWindow.DataContext is MainViewModel mainViewModel)
            {
                mainViewModel.IsReaderFullscreen = isFullscreen;
            }
        }

        private (double? Offset, double? Progress) CaptureScrollState()
        {
            if (ScrollView == null)
            {
                return (null, null);
            }

            var offset = ScrollView.VerticalOffset;
            if (double.IsNaN(offset))
            {
                offset = 0;
            }

            double? progress = GetCurrentNormalizedScrollProgress();

            return (offset, progress);
        }



        private (string ImageUrl, double Progress)? GetCurrentScrollAnchor()
        {
            if (ScrollView == null || ImageList == null || ImageList.Items.Count == 0)
            {
                return null;
            }

            double viewportHeight = ScrollView.ViewportHeight > 0 ? ScrollView.ViewportHeight : ScrollView.ActualHeight;
            if (viewportHeight <= 0)
            {
                return null;
            }

            var panel = FindDescendant<VirtualizingStackPanel>(ImageList);
            if (panel == null || panel.Children.Count == 0)
            {
                return null;
            }

            var realizedContainers = new List<(FrameworkElement Container, double Top)>();
            foreach (UIElement child in panel.Children)
            {
                if (child is not FrameworkElement container || container.ActualHeight <= 0)
                {
                    continue;
                }

                System.Windows.Point relative;
                try
                {
                    relative = container.TransformToVisual(ScrollView).Transform(new Point(0, 0));
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                realizedContainers.Add((container, relative.Y));
            }

            if (realizedContainers.Count == 0)
            {
                return null;
            }

            foreach (var (container, top) in realizedContainers.OrderBy(entry => entry.Top))
            {
                double bottom = top + container.ActualHeight;

                if (bottom <= 0)
                {
                    continue;
                }

                if (top >= viewportHeight)
                {
                    break;
                }

                if (container.DataContext is ChapterImage image)
                {
                    double progress = top < 0
                        ? Math.Clamp(-top / container.ActualHeight, 0.0, 1.0)
                        : 0.0;
                    return (image.ImageUrl, progress);
                }
            }

            return null;
        }

        private double? GetCurrentNormalizedScrollProgress()
        {
            if (ScrollView == null)
            {
                return null;
            }

            var extent = ScrollView.ExtentHeight;
            var viewport = ScrollView.ViewportHeight;
            if (viewport <= 0)
            {
                viewport = ScrollView.ActualHeight;
            }

            if (extent <= 0 || viewport <= 0)
            {
                return null;
            }

            var scrollable = Math.Max(extent - viewport, 0);
            if (scrollable <= 0)
            {
                return null;
            }

            var offset = ScrollView.VerticalOffset;
            if (double.IsNaN(offset))
            {
                return null;
            }

            return Math.Clamp(offset / scrollable, 0.0, 1.0);
        }



        private void QueueScrollRestore(
            double? offset,
            double? normalizedProgress,
            string? anchorImageUrl = null,
            double? anchorImageProgress = null)
        {
            double? pendingOffset = offset.HasValue ? Math.Max(0, offset.Value) : null;
            double? pendingProgress = (!pendingOffset.HasValue && normalizedProgress.HasValue)
                ? Math.Clamp(normalizedProgress.Value, 0.0, 1.0)
                : null;
            double? pendingAnchorProgress = anchorImageProgress.HasValue
                ? Math.Clamp(anchorImageProgress.Value, 0.0, 1.0)
                : null;
            string? pendingAnchorUrl = string.IsNullOrWhiteSpace(anchorImageUrl) ? null : anchorImageUrl;

            if (!pendingOffset.HasValue && !pendingProgress.HasValue && pendingAnchorUrl == null)
            {
                return;
            }

            _pendingScrollOffset = pendingOffset;
            _pendingScrollProgress = pendingProgress;
            _pendingAnchorImageUrl = pendingAnchorUrl;
            _pendingAnchorImageProgress = pendingAnchorProgress;
            _pendingAnchorIndex = null;
            _anchorBringIntoViewRequested = false;
            _lastRequestedScrollOffset = null;
            _suppressAutoAdvance = true;

            if (ScrollView != null)
            {
                ScrollView.Dispatcher.InvokeAsync(
                    TryApplyPendingScroll,
                    DispatcherPriority.Background);
            }
        }

        private void ClearPendingScrollRestoration()
        {
            _pendingScrollProgress = null;
            _pendingScrollOffset = null;
            _pendingAnchorImageUrl = null;
            _pendingAnchorImageProgress = null;
            _pendingAnchorIndex = null;
            _anchorBringIntoViewRequested = false;
            _lastRequestedScrollOffset = null;
            _isApplyingPendingScroll = false;
            _suppressAutoAdvance = false;
            CancelLayoutValidation();
        }
    }
}