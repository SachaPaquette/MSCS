using MSCS.Models;
using MSCS.ViewModels;
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

            if (!TryPrepareContextMenu(button))
            {
                return;
            }

            button.ContextMenu!.IsOpen = true;
            e.Handled = true;
        }

        private void OnMenuButtonContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button button)
            {
                return;
            }

            if (!TryPrepareContextMenu(button))
            {
                e.Handled = true;
            }
        }

        private bool TryPrepareContextMenu(System.Windows.Controls.Button button)
        {
            if (button.ContextMenu == null)
            {
                return false;
            }

            if (button.DataContext is not AniListMedia media)
            {
                return false;
            }

            if (DataContext is not AniListRecommendationsViewModel viewModel)
            {
                return false;
            }

            button.ContextMenu.DataContext = new MenuContext(viewModel, media);
            button.ContextMenu.PlacementTarget = button;
            return true;
        }

        private sealed record MenuContext(AniListRecommendationsViewModel ViewModel, AniListMedia Media);
    }
}