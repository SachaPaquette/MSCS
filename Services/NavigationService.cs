using MSCS.Interfaces;
using MSCS.ViewModels;
using System;
using System.Collections.Concurrent;

namespace MSCS.Services
{
    public class NavigationService : INavigationService
    {
        private readonly Func<Type, BaseViewModel> _factory;
        private readonly ConcurrentDictionary<Type, BaseViewModel> _singletons = new();

        // Host (MainWindow/MainViewModel) sets this
        public Action<BaseViewModel> ApplyViewModel { get; set; } = _ => { };

        public NavigationService(Func<Type, BaseViewModel> factory)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public void RegisterSingleton<TViewModel>(TViewModel instance) where TViewModel : BaseViewModel
        {
            if (instance is null) throw new ArgumentNullException(nameof(instance));
            _singletons[typeof(TViewModel)] = instance;
        }

        public void NavigateToSingleton<TViewModel>() where TViewModel : BaseViewModel
        {
            if (_singletons.TryGetValue(typeof(TViewModel), out var vm))
            {
                ApplyViewModel(vm);
                return;
            }
            // Fallback: create if not registered
            NavigateTo<TViewModel>();
        }

        public void NavigateTo<TViewModel>() where TViewModel : BaseViewModel
        {
            var vm = _factory(typeof(TViewModel));
            ApplyViewModel(vm);
        }

        public void NavigateTo<TViewModel>(object parameter) where TViewModel : BaseViewModel
        {
            // NOTE: only works for single-parameter ctors. Prefer building the VM yourself and calling NavigateToViewModel.
            var vm = (BaseViewModel)Activator.CreateInstance(typeof(TViewModel), parameter);
            ApplyViewModel(vm);
        }

        public void NavigateToViewModel(BaseViewModel vm)
        {
            if (vm == null) throw new ArgumentNullException(nameof(vm));
            ApplyViewModel(vm);
        }
    }
}
