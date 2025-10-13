using MSCS.Commands;
using MSCS.Enums;
using MSCS.Interfaces;
using MSCS.Services;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Forms = System.Windows.Forms;

namespace MSCS.ViewModels
{
    public class SettingsViewModel : BaseViewModel, IDisposable
    {
        private readonly LocalLibraryService _libraryService;
        private readonly UserSettings _userSettings;
        private readonly ThemeService _themeService;
        private readonly List<TrackingProviderViewModel> _trackingProviders;
        private bool _disposed;
        private string? _libraryPath;
        private bool _suppressUpdate;
        private bool _isAniListConnected;
        private string? _aniListUserName;
        private bool _suppressSettingsUpdate;
        private AppTheme _selectedTheme;

        public SettingsViewModel(
            LocalLibraryService libraryService,
            UserSettings userSettings,
            ThemeService themeService,
            MediaTrackingServiceRegistry trackingRegistry)
        {
            _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
            _userSettings = userSettings ?? throw new ArgumentNullException(nameof(userSettings));
            _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));

            if (trackingRegistry == null)
            {
                throw new ArgumentNullException(nameof(trackingRegistry));
            }

            _trackingProviders = trackingRegistry.Services
                .OrderBy(service => service.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(service => new TrackingProviderViewModel(service))
                .ToList();

            _libraryService.LibraryPathChanged += OnLibraryPathChanged;
            _userSettings.SettingsChanged += OnUserSettingsChanged;

            _libraryPath = _libraryService.LibraryPath;

            ThemeOptions = new List<ThemeOption>
            {
                new(AppTheme.Dark, "Dark"),
                new(AppTheme.Light, "Light")
            };

            _suppressSettingsUpdate = true;
            _selectedTheme = _userSettings.AppTheme;
            _suppressSettingsUpdate = false;
            OnPropertyChanged(nameof(SelectedTheme));


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
        public IReadOnlyList<ThemeOption> ThemeOptions { get; }
        public IReadOnlyList<TrackingProviderViewModel> TrackingProviders => _trackingProviders;

        public AppTheme SelectedTheme
        {
            get => _selectedTheme;
            set
            {
                if (SetProperty(ref _selectedTheme, value, nameof(SelectedTheme)))
                {
                    if (!_suppressSettingsUpdate)
                    {
                        _themeService.ApplyTheme(value);
                        _userSettings.AppTheme = value;
                    }
                }
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

            foreach (var provider in _trackingProviders)
            {
                provider.Dispose();
            }
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

        private void OnUserSettingsChanged(object? sender, EventArgs e)
        {
            try
            {
                _suppressSettingsUpdate = true;
                SelectedTheme = _userSettings.AppTheme;
            }
            finally
            {
                _suppressSettingsUpdate = false;
            }
        }

        public record ThemeOption(AppTheme Value, string DisplayName);
    }
}