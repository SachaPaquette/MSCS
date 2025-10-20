using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ListBox = System.Windows.Controls.ListBox;

namespace MSCS.Views
{
    public partial class ReaderSidebar : System.Windows.Controls.UserControl
    {
        public ReaderSidebar()
        {
            InitializeComponent();
        }

        // Bind the chapters (IEnumerable<Chapter>)
        public IEnumerable ItemsSource
        {
            get => (IEnumerable)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(ReaderSidebar),
                new PropertyMetadata(null, (_, __) => { /* CollectionViewSource picks it up */ }));

        // Selected chapter
        public object SelectedItem
        {
            get => GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }
        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(ReaderSidebar),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        // Home command
        public System.Windows.Input.ICommand HomeCommand
        {
            get => (System.Windows.Input.ICommand)GetValue(HomeCommandProperty);
            set => SetValue(HomeCommandProperty, value);
        }
        public static readonly DependencyProperty HomeCommandProperty =
            DependencyProperty.Register(nameof(HomeCommand), typeof(System.Windows.Input.ICommand), typeof(ReaderSidebar));

        // Filter text
        public string SearchText
        {
            get => (string)GetValue(SearchTextProperty);
            set => SetValue(SearchTextProperty, value);
        }
        public static readonly DependencyProperty SearchTextProperty =
            DependencyProperty.Register(nameof(SearchText), typeof(string), typeof(ReaderSidebar),
                new PropertyMetadata(string.Empty, OnSearchChanged));

        private static void OnSearchChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ReaderSidebar s &&
                s.Resources["ChaptersView"] is CollectionViewSource cvs &&
                cvs.View != null)
            {
                cvs.View.Refresh();
            }
        }

        private void ChaptersView_Filter(object sender, FilterEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                e.Accepted = true;
                return;
            }
            var text = SearchText.Trim().ToLowerInvariant();

            // Expecting an object with Title property; adjust if needed.
            var title = e.Item?.GetType().GetProperty("Title")?.GetValue(e.Item)?.ToString() ?? "";
            e.Accepted = title.ToLowerInvariant().Contains(text);
        }

        private void ChaptersList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ListBox listBox && listBox.SelectedItem != null)
            {
                listBox.ScrollIntoView(listBox.SelectedItem);
            }
        }
    }
}
