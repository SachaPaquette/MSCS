using System.Windows;
using MSCS.Models;
using MSCS.ViewModels.Update;

namespace MSCS.Views.Update;

public partial class UpdateAvailableWindow : Window
{
    public UpdateAvailableWindow(UpdateCheckResult updateInfo)
    {
        InitializeComponent();
        DataContext = new UpdateAvailableViewModel(updateInfo, Close);
    }
}