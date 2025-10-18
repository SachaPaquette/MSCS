using MSCS.ViewModels;
using System;
using System.Windows;

namespace MSCS.Views
{
    public partial class TrackingWindow : Window
    {
        private readonly TrackingWindowViewModel _viewModel;

        public TrackingWindow(TrackingWindowViewModel viewModel)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            InitializeComponent();
            DataContext = _viewModel;
            Title = _viewModel.Title;
            _viewModel.CloseRequested += OnCloseRequested;
            Closed += OnClosed;
        }

        private void OnCloseRequested(object? sender, bool e)
        {
            DialogResult = e;
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _viewModel.CloseRequested -= OnCloseRequested;
            _viewModel.Dispose();
        }
    }
}