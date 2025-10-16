using System;

namespace MSCS.ViewModels.Settings
{
    public abstract class SettingsSectionViewModel : BaseViewModel, IDisposable
    {
        private bool _isVisible = true;
        private bool _isDisposed;

        protected SettingsSectionViewModel(string key, string title, string? description)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Title = title ?? throw new ArgumentNullException(nameof(title));
            Description = description;
        }

        public string Key { get; }

        public string Title { get; }

        public string? Description { get; }

        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
        }
    }
}