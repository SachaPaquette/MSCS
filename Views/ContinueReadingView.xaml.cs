using MSCS.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace MSCS.Views
{
    public partial class ContinueReadingView : System.Windows.Controls.UserControl
    {
        public ContinueReadingView()
        {
            InitializeComponent();
        }

        private void OnEntryClicked(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source)
            {
                while (source != null)
                {
                    if (source is System.Windows.Controls.Primitives.ButtonBase)
                    {
                        return;
                    }

                    source = VisualTreeHelper.GetParent(source);
                }
            }

            if (sender is System.Windows.Controls.ListViewItem item && item.DataContext is ContinueReadingEntryViewModel entry)
            {
                var command = entry.ContinueCommand;
                if (command?.CanExecute(null) == true)
                {
                    command.Execute(null);
                    e.Handled = true;
                }
            }
        }
    }
}