using System.Windows;
using System.Windows.Controls;

namespace MSCS.Views
{
    public partial class AniListRecommendationsView : System.Windows.Controls.UserControl
    {
        public AniListRecommendationsView()
        {
            InitializeComponent();
        }

        private void OnMenuButtonClick(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button button)
            {
                return;
            }

            if (button.ContextMenu == null)
            {
                return;
            }

            button.ContextMenu.DataContext = button.DataContext;
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
            e.Handled = true;
        }
    }
}