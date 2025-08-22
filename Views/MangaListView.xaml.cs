using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MSCS.Sources;
using MSCS.ViewModels;
using System.Diagnostics;
using MSCS.Models;

namespace MSCS.Views
{
    public partial class MangaListView : UserControl
    {
        public MangaListView()
        {
            InitializeComponent();
            ResultList.Loaded += ResultList_Loaded;
        }

        private async void Search_Click(object sender, RoutedEventArgs e)
        {
            string selectedSource = (SourceSelector.SelectedItem as ComboBoxItem)?.Content?.ToString();
            string query = QueryBox.Text;

            if (string.IsNullOrWhiteSpace(selectedSource) || string.IsNullOrWhiteSpace(query))
            {
                MessageBox.Show("Please select a source and enter a search query.");
                return;
            }

            var source = SourceRegistry.Resolve(selectedSource);
            if (source == null)
            {
                MessageBox.Show("Invalid source selected.");
                return;
            }

            var vm = (MangaListViewModel)DataContext;
            vm.SetSource(source); 
            await vm.SearchAsync(query);
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
            var scrollViewer = (ScrollViewer)sender;
            double threshold = scrollViewer.ExtentHeight * 0.95;
            if (scrollViewer.VerticalOffset + scrollViewer.ViewportHeight >= threshold)
            {
                await LoadMoreMangaAsync();
            }
        }

        private void ResultList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ResultList.SelectedItem is Manga selectedManga)
            {
                var vm = (MangaListViewModel)DataContext;
                vm.SelectedManga = selectedManga;
                Debug.WriteLine($"Selected Manga: {selectedManga.Title}");
            }
        }

        private async Task LoadMoreMangaAsync()
        {
            var vm = (MangaListViewModel)DataContext;
            await vm.LoadMoreAsync();
        }
    }
}