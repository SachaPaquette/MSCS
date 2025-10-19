using MSCS.Interfaces;
using MSCS.Services;
using MSCS.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace MSCS.Views
{
    public partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; }

        public MainWindow() : this(App.GetRequiredService<MainViewModel>(), App.GetRequiredService<INavigationService>())
        {
        }

        public MainWindow(MainViewModel viewModel, INavigationService? navigationService = null)
        {
            InitializeComponent();

            ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = ViewModel;

            var resolvedNavigation = navigationService ?? App.GetRequiredService<INavigationService>();
            resolvedNavigation.ApplyViewModel = vm => ViewModel.NavigateToViewModel(vm);
        }
    }
}