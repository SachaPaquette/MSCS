using System.Windows;
using System.Windows.Controls;
using MSCS.Services;
using MSCS.ViewModels;

namespace MSCS.Views
{
    public partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; }

        public MainWindow()
        {
            InitializeComponent();

            var navigationService = new NavigationService(
                type => (BaseViewModel)Activator.CreateInstance(type));

            var viewModel = new MainViewModel(navigationService);

            // Wire the callback to push VMs into MainViewModel
            navigationService.ApplyViewModel = vm => viewModel.NavigateToViewModel(vm);

            ViewModel = viewModel;
            DataContext = ViewModel;
        }


        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            ViewModel = viewModel;
            DataContext = ViewModel;
        }


    }
}