using MSCS.Interfaces;
using MSCS.ViewModels;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Windows.Input;
namespace MSCS.Services
{
    public class NavigationService : INavigationService
    {
        private readonly Func<Type, BaseViewModel> _factory;
        private readonly ConcurrentDictionary<Type, BaseViewModel> _singletons = new();
        private readonly Stack<BaseViewModel> _backStack = new();
        private BaseViewModel _currentViewModel;
        private BaseViewModel _rootViewModel;
        private bool _canGoBack;
        // Host (MainWindow/MainViewModel) sets this
        public Action<BaseViewModel> ApplyViewModel { get; set; } = _ => { };

        public NavigationService(Func<Type, BaseViewModel> factory)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public bool CanGoBack => _canGoBack;
        public event EventHandler CanGoBackChanged;
        public void RegisterSingleton<TViewModel>(TViewModel instance) where TViewModel : BaseViewModel
        {
            if (instance is null) throw new ArgumentNullException(nameof(instance));
            _singletons[typeof(TViewModel)] = instance;
        }

        public void NavigateToSingleton<TViewModel>() where TViewModel : BaseViewModel
        {
            if (_singletons.TryGetValue(typeof(TViewModel), out var vm))
            {
                if (ReferenceEquals(vm, _rootViewModel))
                {
                    ShowViewModel(vm, addToHistory: false);
                    return;
                }

                if (!ReferenceEquals(vm, _currentViewModel))
                {
                    ShowViewModel(vm, addToHistory: true);
                }
                return;
            }
            // Fallback: create if not registered
            NavigateTo<TViewModel>();
        }

        public void NavigateTo<TViewModel>() where TViewModel : BaseViewModel
        {
            var vm = _factory(typeof(TViewModel));
            ShowViewModel(vm, addToHistory: true);
        }

        public void NavigateTo<TViewModel>(object parameter) where TViewModel : BaseViewModel
        {
            // NOTE: only works for single-parameter ctors. Prefer building the VM yourself and calling NavigateToViewModel.
            var vm = (BaseViewModel)Activator.CreateInstance(typeof(TViewModel), parameter);
            ShowViewModel(vm, addToHistory: true);
        }

        public void NavigateToViewModel(BaseViewModel vm)
        {
            if (vm == null) throw new ArgumentNullException(nameof(vm));
            ShowViewModel(vm, addToHistory: true);
        }

        public void GoBack()
        {
            if (!CanGoBack)
            {
                return;
            }

            var target = _backStack.Pop();
            ShowViewModel(target, addToHistory: false);
        }

        public void SetRootViewModel(BaseViewModel viewModel)
        {
            _currentViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _rootViewModel = _currentViewModel;
            _backStack.Clear();
            UpdateCanGoBack();
        }

        private void ShowViewModel(BaseViewModel vm, bool addToHistory)
        {
            if (vm == null) throw new ArgumentNullException(nameof(vm));

            var previous = _currentViewModel;
            bool isDifferent = previous != null && !ReferenceEquals(previous, vm);

            if (addToHistory && previous != null && isDifferent)
            {
                _backStack.Push(previous);
            }
            else if (isDifferent && previous is IDisposable disposable)
            {
                disposable.Dispose();
            }

            _currentViewModel = vm;
            ApplyViewModel(vm);
            UpdateCanGoBack();
        }

        private void UpdateCanGoBack()
        {
            bool newValue = _backStack.Count > 0;
            if (newValue != _canGoBack)
            {
                _canGoBack = newValue;
                CanGoBackChanged?.Invoke(this, EventArgs.Empty);
            }
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
