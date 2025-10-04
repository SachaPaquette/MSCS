using MSCS.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSCS.Interfaces
{
    public interface INavigationService
    {
        void NavigateTo<TViewModel>() where TViewModel : BaseViewModel;
        void NavigateTo<TViewModel>(object parameter) where TViewModel : BaseViewModel;
        void NavigateToViewModel(BaseViewModel viewModel);
        void RegisterSingleton<TViewModel>(TViewModel instance) where TViewModel : BaseViewModel;
        void NavigateToSingleton<TViewModel>() where TViewModel : BaseViewModel;
        void GoBack();
        bool CanGoBack { get; }
        event EventHandler CanGoBackChanged;
        void SetRootViewModel(BaseViewModel viewModel);
    }
}