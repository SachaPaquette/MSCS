using MSCS.Helpers;
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
namespace MSCS.Views
{
    public partial class ReaderView : UserControl
    {
        public ReaderView()
        {
            InitializeComponent();
        }
        private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer == null)
                return;

            if (e.VerticalChange > 0 &&
                scrollViewer.VerticalOffset + scrollViewer.ViewportHeight >= scrollViewer.ExtentHeight - 100)
            {
                if (DataContext is ReaderViewModel viewModel)
                {
                    viewModel.LoadMoreImages();
                    if (viewModel.remainingImages == 0)
                    {
                        GoToNextChapter(viewModel);
                    }
                }

            }
        }
        private void ScrollEventHandler(object sender, MouseButtonEventArgs e)
        {
            if (sender is Image image && ScrollView != null)
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





    }
}
