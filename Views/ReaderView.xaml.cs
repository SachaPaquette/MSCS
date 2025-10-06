using MSCS.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Media.Animation;
using System.Reflection;
using MSCS.Helpers;
namespace MSCS.Views
{
    public partial class ReaderView : System.Windows.Controls.UserControl
    {
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
                double progress = 0;
                if (scrollViewer.ExtentHeight > 0)
                {
                    progress = Math.Clamp(
                        (scrollViewer.VerticalOffset + scrollViewer.ViewportHeight) / scrollViewer.ExtentHeight,
                        0.0,
                        1.0);
                }

                readerViewModel.ScrollProgress = progress;
            }
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
            }

            if (e.NewValue is ReaderViewModel newViewModel)
            {
                newViewModel.ChapterChanged += OnChapterChanged;
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




    }
}
