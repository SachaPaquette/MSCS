﻿using MSCS.ViewModels;
using System.Windows.Controls;
using System.Windows.Input;
using UserControl = System.Windows.Controls.UserControl;

namespace MSCS.Views
{
    public partial class MyAnimeListTrackingView : UserControl
    {
        public MyAnimeListTrackingView()
        {
            InitializeComponent();
        }

        private void ListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is MyAnimeListTrackingViewModel viewModel && viewModel.ConfirmCommand.CanExecute(null))
            {
                viewModel.ConfirmCommand.Execute(null);
            }
        }
    }
}