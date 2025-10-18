using MSCS.ViewModels;
using System.Windows.Controls;
using System.Windows.Input;
using UserControl = System.Windows.Controls.UserControl;

namespace MSCS.Views
{
    public partial class AniListTrackingView : UserControl
    {
        public AniListTrackingView()
        {
            InitializeComponent();
        }

        private void ListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is AniListTrackingViewModel viewModel && viewModel.ConfirmCommand.CanExecute(null))
            {
                viewModel.ConfirmCommand.Execute(null);
            }
        }
    }
}