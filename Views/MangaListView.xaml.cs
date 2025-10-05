using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MSCS.Models;
using MSCS.ViewModels;

namespace MSCS.Views
{
    public partial class MangaListView : System.Windows.Controls.UserControl
    {
        public MangaListView()
        {
            InitializeComponent();
            ResultList.Loaded += ResultList_Loaded;
        }

        private void ResultList_Loaded(object sender, RoutedEventArgs e)
        {
            if (VisualTreeHelper.GetChild(ResultList, 0) is Border border)
            {
                if (VisualTreeHelper.GetChild(border, 0) is ScrollViewer scrollViewer)
                {
                    scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
                }
            }
        }

        private async void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (sender is not ScrollViewer scrollViewer)
            {
                return;
            }

            double threshold = scrollViewer.ExtentHeight * 0.95;
            if (scrollViewer.VerticalOffset + scrollViewer.ViewportHeight >= threshold)
            {
                await LoadMoreMangaAsync();
            }
        }

        private void ResultList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ResultList.SelectedItem is Manga selectedManga && DataContext is MangaListViewModel vm)
            {
                vm.SelectedManga = selectedManga;
                Debug.WriteLine($"Selected Manga: {selectedManga.Title}");
            }
        }

        private async Task LoadMoreMangaAsync()
        {
            if (DataContext is MangaListViewModel vm)
            {
                await vm.LoadMoreAsync();
            }
        }
    }
}