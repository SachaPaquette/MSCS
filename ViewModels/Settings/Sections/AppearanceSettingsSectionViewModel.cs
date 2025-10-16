using System;
using System.Collections.Generic;
using MSCS.Enums;
using MSCS.Services;

namespace MSCS.ViewModels.Settings
{
    public class AppearanceSettingsSectionViewModel : SettingsSectionViewModel
    {
        private readonly ThemeService _themeService;
        private readonly UserSettings _userSettings;
        private bool _suppressUpdate;
        private AppTheme _selectedTheme;

        public AppearanceSettingsSectionViewModel(ThemeService themeService, UserSettings userSettings)
            : base("appearance", "Appearance", "Pick a theme for the application.")
        {
            _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
            _userSettings = userSettings ?? throw new ArgumentNullException(nameof(userSettings));

            ThemeOptions = new List<ThemeOption>
            {
                new(AppTheme.Dark, "Dark"),
                new(AppTheme.Light, "Light")
            };

            _userSettings.SettingsChanged += OnUserSettingsChanged;

            _suppressUpdate = true;
            _selectedTheme = _userSettings.AppTheme;
            _suppressUpdate = false;
            OnPropertyChanged(nameof(SelectedTheme));
        }

        public IReadOnlyList<ThemeOption> ThemeOptions { get; }

        public AppTheme SelectedTheme
        {
            get => _selectedTheme;
            set
            {
                if (SetProperty(ref _selectedTheme, value) && !_suppressUpdate)
                {
                    _themeService.ApplyTheme(value);
                    _userSettings.AppTheme = value;
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            _userSettings.SettingsChanged -= OnUserSettingsChanged;
        }

        private void OnUserSettingsChanged(object? sender, EventArgs e)
        {
            try
            {
                _suppressUpdate = true;
                SelectedTheme = _userSettings.AppTheme;
            }
            finally
            {
                _suppressUpdate = false;
            }
        }

        public record ThemeOption(AppTheme Value, string DisplayName);
    }
}