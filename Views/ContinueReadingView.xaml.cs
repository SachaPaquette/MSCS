using System.Windows.Controls;
using System.Windows.Input;
using MSCS.ViewModels;

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