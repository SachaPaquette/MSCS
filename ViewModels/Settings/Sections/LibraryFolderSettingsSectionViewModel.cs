using System;
using System.IO;
using System.Windows.Input;
using MSCS.Commands;
using MSCS.Services;
using Forms = System.Windows.Forms;

namespace MSCS.ViewModels.Settings
{
    public class LibraryFolderSettingsSectionViewModel : SettingsSectionViewModel
    {
        private readonly LocalLibraryService _libraryService;
        private bool _suppressUpdate;
        private string? _libraryPath;

        public LibraryFolderSettingsSectionViewModel(LocalLibraryService libraryService)
            : base("libraryFolder", "Local library folder", "Choose the folder that contains your downloaded manga collection.")
        {
            _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));

            BrowseCommand = new RelayCommand(_ => BrowseForFolder());
            ClearCommand = new RelayCommand(_ => LibraryPath = null, _ => !string.IsNullOrWhiteSpace(LibraryPath));

            _libraryService.LibraryPathChanged += OnLibraryPathChanged;

            _libraryPath = _libraryService.LibraryPath;
        }

        public string? LibraryPath
        {
            get => _libraryPath;
            set
            {
                if (_suppressUpdate)
                {
                    if (SetProperty(ref _libraryPath, value))
                    {
                        OnPropertyChanged(nameof(LibraryPathExists));
                        CommandManager.InvalidateRequerySuggested();
                    }

                    return;
                }

                if (SetProperty(ref _libraryPath, value))
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

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            _libraryService.LibraryPathChanged -= OnLibraryPathChanged;
        }

        private void BrowseForFolder()
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select the folder that contains your manga library.",
                SelectedPath = LibraryPathExists ? LibraryPath : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                LibraryPath = dialog.SelectedPath;
            }
        }

        private void OnLibraryPathChanged(object? sender, LibraryChangedEventArgs e)
        {
            if (e.Kind != LibraryChangeKind.Reset)
            {
                return;
            }

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