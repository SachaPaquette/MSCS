using MSCS.Interfaces;
using MSCS.Services;
using MSCS.ViewModels;
using System;
using System.ComponentModel;
using System.Windows;

namespace MSCS.Views
{
    public partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; }
        public MainWindow()
            : this(App.GetRequiredService<MainViewModel>(), App.GetRequiredService<INavigationService>())
        {
        }

        public MainWindow(MainViewModel viewModel, INavigationService? navigationService = null)
        {
            InitializeComponent();

            ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = ViewModel;

            var resolvedNavigation = navigationService ?? App.GetRequiredService<INavigationService>();
            resolvedNavigation.ApplyViewModel = vm => ViewModel.NavigateToViewModel(vm);

            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentViewModel))
            {
                if (ViewModel.CurrentViewModel is not ReaderViewModel && ViewModel.IsReaderFullscreen)
                {
                    ViewModel.IsReaderFullscreen = false;
                }
            }
        }
    }
}