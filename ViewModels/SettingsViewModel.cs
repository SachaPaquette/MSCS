using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using MSCS.Commands;
using MSCS.Interfaces;
using MSCS.Services;
using Forms = System.Windows.Forms;

namespace MSCS.ViewModels
{
    public class SettingsViewModel : BaseViewModel, IDisposable
    {
        private readonly LocalLibraryService _libraryService;
        private readonly UserSettings _userSettings;
        private readonly IAniListService _aniListService;
        private bool _disposed;
        private string? _libraryPath;
        private bool _suppressUpdate;
        private bool _isAniListConnected;
        private string? _aniListUserName;
        private bool _suppressSettingsUpdate;

        public SettingsViewModel(LocalLibraryService libraryService, UserSettings userSettings, IAniListService aniListService)
        {
            _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
            _userSettings = userSettings ?? throw new ArgumentNullException(nameof(userSettings));
            _aniListService = aniListService ?? throw new ArgumentNullException(nameof(aniListService));

            _libraryService.LibraryPathChanged += OnLibraryPathChanged;
            _userSettings.SettingsChanged += OnUserSettingsChanged;
            _aniListService.AuthenticationChanged += OnAniListAuthenticationChanged;

            _libraryPath = _libraryService.LibraryPath;
            _isAniListConnected = _aniListService.IsAuthenticated;
            _aniListUserName = _aniListService.UserName;

            BrowseCommand = new RelayCommand(_ => BrowseForFolder());
            ClearCommand = new RelayCommand(_ => LibraryPath = null, _ => !string.IsNullOrWhiteSpace(LibraryPath));
            AniListAuthenticateCommand = new AsyncRelayCommand(_ => AuthenticateAniListAsync());
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
        public ICommand AniListAuthenticateCommand { get; }

        public bool IsAniListConnected
        {
            get => _isAniListConnected;
            private set
            {
                if (SetProperty(ref _isAniListConnected, value, nameof(IsAniListConnected)))
                {
                    OnPropertyChanged(nameof(AniListStatusText));
                }
            }
        }

        public string? AniListUserName
        {
            get => _aniListUserName;
            private set
            {
                if (SetProperty(ref _aniListUserName, value, nameof(AniListUserName)))
                {
                    OnPropertyChanged(nameof(AniListStatusText));
                }
            }
        }

        public string AniListStatusText
        {
            get
            {
                if (IsAniListConnected)
                {
                    return !string.IsNullOrWhiteSpace(AniListUserName)
                        ? $"Connected as {AniListUserName}"
                        : "Connected";
                }

                return "Not connected";
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _libraryService.LibraryPathChanged -= OnLibraryPathChanged;
            _userSettings.SettingsChanged -= OnUserSettingsChanged;
            _aniListService.AuthenticationChanged -= OnAniListAuthenticationChanged;
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

        private async Task AuthenticateAniListAsync()
        {
            try
            {
                var owner = System.Windows.Application.Current?.MainWindow;
                var success = await _aniListService.AuthenticateAsync(owner).ConfigureAwait(true);
                if (success)
                {
                    UpdateAniListState();
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "AniList", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private void OnUserSettingsChanged(object? sender, EventArgs e)
        {
            try
            {
                _suppressSettingsUpdate = true;
            }
            finally
            {
                _suppressSettingsUpdate = false;
            }
        }

        private void OnAniListAuthenticationChanged(object? sender, EventArgs e)
        {
            UpdateAniListState();
        }

        private void UpdateAniListState()
        {
            IsAniListConnected = _aniListService.IsAuthenticated;
            AniListUserName = _aniListService.UserName;
        }
    }
}