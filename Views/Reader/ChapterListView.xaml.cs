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

namespace MSCS.Views
{
    /// <summary>
    /// Interaction logic for ChapterListView.xaml
    /// </summary>
    public partial class ChapterListView : System.Windows.Controls.UserControl
    {
        public ChapterListView()
        {
            InitializeComponent();
        }

        private void OnChaptersListPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not DependencyObject dependencyObject)
            {
                return;
            }

            var scrollViewer = FindParentScrollViewer(dependencyObject);
            if (scrollViewer == null)
            {
                return;
            }

            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
            e.Handled = true;
        }

        private static ScrollViewer? FindParentScrollViewer(DependencyObject current)
        {
            while (current != null)
            {
                if (current is ScrollViewer scrollViewer)
                {
                    return scrollViewer;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }
    }
}