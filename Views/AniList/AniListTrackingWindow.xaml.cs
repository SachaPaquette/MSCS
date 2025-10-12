using MSCS.ViewModels;
using System;
using System.Windows;
using System.Windows.Input;

namespace MSCS.Views
{
    public partial class AniListTrackingWindow : Window
    {
        private readonly AniListTrackingViewModel _viewModel;

        public AniListTrackingWindow(AniListTrackingViewModel viewModel)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            InitializeComponent();
            DataContext = _viewModel;
            _viewModel.CloseRequested += ViewModelOnCloseRequested;
            Closed += OnClosed;
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _viewModel.CloseRequested -= ViewModelOnCloseRequested;
            _viewModel.Dispose();
        }

        private void ViewModelOnCloseRequested(object? sender, bool e)
        {
            DialogResult = e;
        }

        private void ListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel.ConfirmCommand.CanExecute(null))
            {
                _viewModel.ConfirmCommand.Execute(null);
            }
        }
    }
}