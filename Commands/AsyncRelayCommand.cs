using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace MSCS.Commands
{
    /// <summary>
    /// An <see cref="ICommand"/> implementation that supports asynchronous execution while
    /// preventing concurrent re-entrancy.
    /// </summary>
    public sealed class AsyncRelayCommand : ICommand
    {
        private readonly Func<object?, Task> _executeAsync;
        private readonly Predicate<object?>? _canExecute;
        private bool _isExecuting;

        public AsyncRelayCommand(Func<Task> executeAsync)
            : this(_ => executeAsync())
        {
        }

        public AsyncRelayCommand(Func<object?, Task> executeAsync)
            : this(executeAsync, null)
        {
        }

        public AsyncRelayCommand(Func<Task> executeAsync, Func<bool> canExecute)
            : this(_ => executeAsync(), canExecute == null ? null : new Predicate<object?>(_ => canExecute()))
        {
        }

        public AsyncRelayCommand(Func<object?, Task> executeAsync, Predicate<object?>? canExecute)
        {
            _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter)
        {
            if (_isExecuting)
            {
                return false;
            }

            return _canExecute?.Invoke(parameter) ?? true;
        }

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter))
            {
                return;
            }

            try
            {
                _isExecuting = true;
                RaiseCanExecuteChanged();
                await _executeAsync(parameter).ConfigureAwait(true);
            }
            finally
            {
                _isExecuting = false;
                RaiseCanExecuteChanged();
            }
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        private static void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }
}