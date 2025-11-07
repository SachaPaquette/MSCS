using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using UserControl = System.Windows.Controls.UserControl;

namespace MSCS.Views
{
    public partial class TrackingLibrariesView : UserControl
    {
        public TrackingLibrariesView()
        {
            InitializeComponent();
        }

        private void OnEntryMenuButtonClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
            {
                return;
            }

            if (button.ContextMenu == null)
            {
                return;
            }

            PrepareEntryContextMenu(button);
            button.ContextMenu.IsOpen = true;
            e.Handled = true;
        }

        private void OnEntryMenuButtonContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (sender is not Button button)
            {
                return;
            }

            if (button.ContextMenu == null)
            {
                return;
            }

            PrepareEntryContextMenu(button);
        }

        private static void PrepareEntryContextMenu(Button button)
        {
            if (button.ContextMenu == null)
            {
                return;
            }

            button.ContextMenu.PlacementTarget = button;
        }
    }
}