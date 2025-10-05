using System;
using System.IO;
using System.Windows.Input;
using MSCS.Commands;
using MSCS.Services;
using Forms = System.Windows.Forms;

namespace MSCS.ViewModels
{
    public class SettingsViewModel : BaseViewModel, IDisposable
    {
        private readonly LocalLibraryService _libraryService;
        private bool _disposed;
        private string? _libraryPath;
        private bool _suppressUpdate;

        public SettingsViewModel(LocalLibraryService libraryService)
        {
            _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
            _libraryService.LibraryPathChanged += OnLibraryPathChanged;

            _libraryPath = _libraryService.LibraryPath;

            BrowseCommand = new RelayCommand(_ => BrowseForFolder());
            ClearCommand = new RelayCommand(_ => LibraryPath = null, _ => !string.IsNullOrWhiteSpace(LibraryPath));
        }

        public string? LibraryPath
        {
            get => _libraryPath;
            set
            {
                if (_suppressUpdate)
                {
                    SetProperty(ref _libraryPath, value, nameof(LibraryPath));
                    OnPropertyChanged(nameof(LibraryPathExists));
                    CommandManager.InvalidateRequerySuggested();
                    return;
                }

                if (SetProperty(ref _libraryPath, value, nameof(LibraryPath)))
                {
                    _libraryService.SetLibraryPath(value);
                    OnPropertyChanged(nameof(LibraryPathExists));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool LibraryPathExists => !string.IsNullOrWhiteSpace(LibraryPath) && Directory.Exists(LibraryPath);

        public ICommand BrowseCommand { get; }
        public ICommand ClearCommand { get; }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _libraryService.LibraryPathChanged -= OnLibraryPathChanged;
        }

        private void BrowseForFolder()
        {
            using var dialog = new Forms.FolderBrowserDialog
            {
                Description = "Select the folder that contains your manga library.",
                SelectedPath = LibraryPathExists ? LibraryPath : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog() == Forms.DialogResult.OK)
            {
                LibraryPath = dialog.SelectedPath;
            }
        }

        private void OnLibraryPathChanged(object? sender, EventArgs e)
        {
            try
            {
                _suppressUpdate = true;
                LibraryPath = _libraryService.LibraryPath;
            }
            finally
            {
                _suppressUpdate = false;
            }
        }
    }
}