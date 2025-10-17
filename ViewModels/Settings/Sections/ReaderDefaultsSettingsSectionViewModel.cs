using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using MSCS.Commands;
using MSCS.Enums;
using MSCS.Helpers;
using MSCS.Models;
using MSCS.Services;

namespace MSCS.ViewModels.Settings
{
    public class ReaderDefaultsSettingsSectionViewModel : SettingsSectionViewModel
    {
        private readonly UserSettings _userSettings;
        private bool _suppressUpdate;
        private ReaderTheme _selectedReaderTheme;
        private double _readerWidthFactor;
        private double _readerMaxPageWidth;
        private double _readerScrollPageFraction;
        private int _readerScrollDurationMs;

        public ReaderDefaultsSettingsSectionViewModel(UserSettings userSettings)
            : base("readerDefaults", "Reader defaults", "Customize how chapters open in the reader by default.")
        {
            _userSettings = userSettings ?? throw new ArgumentNullException(nameof(userSettings));

            ReaderThemeOptions = Enum.GetValues<ReaderTheme>()
                .Cast<ReaderTheme>()
                .Select(theme => new ReaderThemeOption(theme, GetReaderThemeName(theme)))
                .ToList();

            ResetReaderDefaultsCommand = new RelayCommand(_ => ResetReaderDefaults());

            _userSettings.SettingsChanged += OnUserSettingsChanged;

            ApplyDefaultReaderProfile(_userSettings.GetDefaultReaderProfile());
        }

        public ICommand ResetReaderDefaultsCommand { get; }

        public IReadOnlyList<ReaderThemeOption> ReaderThemeOptions { get; }

        public ReaderTheme SelectedReaderTheme
        {
            get => _selectedReaderTheme;
            set => UpdateReaderTheme(value, persist: true);
        }

        public double ReaderWidthFactor
        {
            get => _readerWidthFactor;
            set => UpdateReaderWidthFactor(value, persist: true);
        }

        public double ReaderMaxPageWidth
        {
            get => _readerMaxPageWidth;
            set => UpdateReaderMaxPageWidth(value, persist: true);
        }

        public double ReaderScrollPageFraction
        {
            get => _readerScrollPageFraction;
            set => UpdateReaderScrollPageFraction(value, persist: true);
        }

        public int ReaderScrollDurationMs
        {
            get => _readerScrollDurationMs;
            set => UpdateReaderScrollDuration(value, persist: true);
        }

        public double ReaderZoomPercent => Math.Round(_readerWidthFactor * 100);

        public double ReaderScrollPercent => Math.Round(_readerScrollPageFraction * 100);

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            _userSettings.SettingsChanged -= OnUserSettingsChanged;
        }

        private void ApplyDefaultReaderProfile(ReaderProfile profile)
        {
            UpdateReaderTheme(profile.Theme, persist: false);
            UpdateReaderWidthFactor(profile.WidthFactor, persist: false);
            UpdateReaderMaxPageWidth(profile.MaxPageWidth, persist: false);
            UpdateReaderScrollPageFraction(profile.ScrollPageFraction, persist: false);
            UpdateReaderScrollDuration(profile.ScrollDurationMs, persist: false);
        }

        private void UpdateReaderTheme(ReaderTheme value, bool persist)
        {
            if (SetProperty(ref _selectedReaderTheme, value, nameof(SelectedReaderTheme)) && persist && !_suppressUpdate)
            {
                PersistDefaultReaderProfile();
            }
        }

        private void UpdateReaderWidthFactor(double value, bool persist)
        {
            var clamped = Math.Clamp(value, 0.3, 1.0);
            if (SetProperty(ref _readerWidthFactor, clamped, nameof(ReaderWidthFactor)))
            {
                OnPropertyChanged(nameof(ReaderZoomPercent));
                if (persist && !_suppressUpdate)
                {
                    PersistDefaultReaderProfile();
                }
            }
        }

        private void UpdateReaderMaxPageWidth(double value, bool persist)
        {
            var clamped = Math.Clamp(value, 600, 1600);
            if (SetProperty(ref _readerMaxPageWidth, clamped, nameof(ReaderMaxPageWidth)) && persist && !_suppressUpdate)
            {
                PersistDefaultReaderProfile();
            }
        }

        private void UpdateReaderScrollPageFraction(double value, bool persist)
        {
            var clamped = Math.Clamp(value, 0.5, 1.2);
            if (SetProperty(ref _readerScrollPageFraction, clamped, nameof(ReaderScrollPageFraction)))
            {
                OnPropertyChanged(nameof(ReaderScrollPercent));
                if (persist && !_suppressUpdate)
                {
                    PersistDefaultReaderProfile();
                }
            }
        }

        private void UpdateReaderScrollDuration(int value, bool persist)
        {
            var clamped = Math.Clamp(value, 100, 500);
            if (SetProperty(ref _readerScrollDurationMs, clamped, nameof(ReaderScrollDurationMs)) && persist && !_suppressUpdate)
            {
                PersistDefaultReaderProfile();
            }
        }

        private void PersistDefaultReaderProfile()
        {
            var profile = new ReaderProfile
            {
                Theme = _selectedReaderTheme,
                WidthFactor = _readerWidthFactor,
                MaxPageWidth = _readerMaxPageWidth,
                ScrollPageFraction = _readerScrollPageFraction,
                ScrollDurationMs = _readerScrollDurationMs
            };

            _userSettings.SetReaderProfile(null, profile);
        }

        private void ResetReaderDefaults()
        {
            UpdateReaderTheme(ReaderTheme.Midnight, persist: false);
            UpdateReaderWidthFactor(Constants.DefaultWidthFactor, persist: false);
            UpdateReaderMaxPageWidth(Constants.DefaultMaxPageWidth, persist: false);
            UpdateReaderScrollPageFraction(Constants.DefaultSmoothScrollPageFraction, persist: false);
            UpdateReaderScrollDuration(Constants.DefaultSmoothScrollDuration, persist: false);

            PersistDefaultReaderProfile();
        }

        private void OnUserSettingsChanged(object? sender, EventArgs e)
        {
            try
            {
                _suppressUpdate = true;
                ApplyDefaultReaderProfile(_userSettings.GetDefaultReaderProfile());
            }
            finally
            {
                _suppressUpdate = false;
            }
        }

        private static string GetReaderThemeName(ReaderTheme theme)
        {
            return theme switch
            {
                ReaderTheme.Midnight => "Midnight",
                ReaderTheme.PureBlack => "Pure black",
                ReaderTheme.Sepia => "Sepia",
                ReaderTheme.HighContrast => "High contrast",
                _ => theme.ToString()
            };
        }

        public record ReaderThemeOption(ReaderTheme Value, string DisplayName);
    }
}