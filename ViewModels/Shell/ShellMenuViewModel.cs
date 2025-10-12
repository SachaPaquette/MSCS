using System.Collections.ObjectModel;
using System.Windows.Input;
using MSCS.Commands;

namespace MSCS.ViewModels
{
    public class ShellMenuViewModel : BaseViewModel
    {
        private ShellMenuItem _selectedItem;

        public ObservableCollection<ShellMenuItem> Items { get; } = new();
        public ShellMenuItem SelectedItem
        {
            get => _selectedItem;
            set => SetProperty(ref _selectedItem, value);
        }

        public ICommand NavigateCommand { get; }

        // Owner (MainViewModel) supplies how to navigate
        private readonly Action<ShellRoute> _navigate;

        public ShellMenuViewModel(Action<ShellRoute> navigate)
        {
            _navigate = navigate;

            // Populate menu
            Items.Add(new ShellMenuItem("Home", ShellRoute.Home, "\uE80F"));
            Items.Add(new ShellMenuItem("Settings", ShellRoute.Settings, "\uE713"));
            Items.Add(new ShellMenuItem("About", ShellRoute.About, "\uE946"));

            NavigateCommand = new RelayCommand(
                p => _navigate(((ShellMenuItem)p).Route),
                p => p is ShellMenuItem
            );

            SelectedItem = Items[0];
        }
    }
}
