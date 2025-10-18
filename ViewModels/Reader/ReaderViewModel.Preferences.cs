// ReaderViewModel.Preferences.cs
using MSCS.Commands;
using MSCS.Enums;
using MSCS.Helpers;
using MSCS.Models;
using MSCS.Services;
using System;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace MSCS.ViewModels
{
    public partial class ReaderViewModel
    {
        private PreferencesVM? _preferences;
        public PreferencesVM Preferences => _preferences ??= new PreferencesVM(_userSettings);

        public void InitPreferences()
        {
            if (_preferences == null)
                _preferences = new PreferencesVM(_userSettings);

            _preferences.UpdateProfileKey(DetermineProfileKey());
        }

        public void RefreshPreferencesProfileKey()
            => _preferences?.UpdateProfileKey(DetermineProfileKey());

        public sealed class PreferencesVM : BaseViewModel
        {
            private readonly UserSettings? _userSettings;
            private string? _profileKey;
            private bool _isApplyingProfile;
            private bool _suppressPresetPropagation;

            private double _widthFactor = Constants.DefaultWidthFactor;
            private double _maxPageWidth = Constants.DefaultMaxPageWidth;
            private ReaderTheme _theme = ReaderTheme.Midnight;
            private double _scrollPageFraction = Constants.DefaultSmoothScrollPageFraction;
            private int _scrollDurationMs = Constants.DefaultSmoothScrollDuration;
            private ReaderScrollPreset _scrollPreset = ReaderScrollPreset.Balanced;

            private static readonly SolidColorBrush MidnightBackground = CreateFrozenBrush("#0F151F");
            private static readonly SolidColorBrush MidnightSurface = CreateFrozenBrush("#111727");
            private static readonly SolidColorBrush BlackBackground = CreateFrozenBrush("#000000");
            private static readonly SolidColorBrush BlackSurface = CreateFrozenBrush("#050505");
            private static readonly SolidColorBrush SepiaBackground = CreateFrozenBrush("#21160C");
            private static readonly SolidColorBrush SepiaSurface = CreateFrozenBrush("#2B1D12");
            private static readonly SolidColorBrush HighContrastBackground = CreateFrozenBrush("#0B0B0B");
            private static readonly SolidColorBrush HighContrastSurface = CreateFrozenBrush("#1E1E1E");

            public PreferencesVM(UserSettings? userSettings)
            {
                _userSettings = userSettings;
                InitializeCommands();
            }

            public ICommand IncreaseZoomCommand { get; private set; } = new RelayCommand(_ => { }, _ => false);
            public ICommand DecreaseZoomCommand { get; private set; } = new RelayCommand(_ => { }, _ => false);
            public ICommand ResetZoomCommand { get; private set; } = new RelayCommand(_ => { }, _ => false);
            public ICommand SetZoomPresetCommand { get; private set; } = new RelayCommand(_ => { }, _ => false);
            public ICommand SetThemeCommand { get; private set; } = new RelayCommand(_ => { });
            public ICommand SetScrollPresetCommand { get; private set; } = new RelayCommand(_ => { }, _ => false);

            public double WidthFactor
            {
                get => _widthFactor;
                set
                {
                    var normalized = NormalizeWidthFactor(value);
                    if (SetProperty(ref _widthFactor, normalized))
                    {
                        OnPropertyChanged(nameof(ZoomPercent));
                        PersistReaderProfile();
                    }
                }
            }

            public double MaxPageWidth
            {
                get => _maxPageWidth;
                set
                {
                    var clamped = Math.Max(400, value);
                    if (SetProperty(ref _maxPageWidth, clamped))
                    {
                        PersistReaderProfile();
                    }
                }
            }

            public ReaderTheme Theme
            {
                get => _theme;
                set
                {
                    if (SetProperty(ref _theme, value))
                    {
                        OnPropertyChanged(nameof(ReaderBackgroundBrush));
                        OnPropertyChanged(nameof(ReaderSurfaceBrush));
                        PersistReaderProfile();
                    }
                }
            }

            public Brush ReaderBackgroundBrush => Theme switch
            {
                ReaderTheme.PureBlack => BlackBackground,
                ReaderTheme.Sepia => SepiaBackground,
                ReaderTheme.HighContrast => HighContrastBackground,
                _ => MidnightBackground
            };

            public Brush ReaderSurfaceBrush => Theme switch
            {
                ReaderTheme.PureBlack => BlackSurface,
                ReaderTheme.Sepia => SepiaSurface,
                ReaderTheme.HighContrast => HighContrastSurface,
                _ => MidnightSurface
            };

            public double ZoomPercent => Math.Round((double.IsFinite(_widthFactor) ? _widthFactor : Constants.DefaultWidthFactor) * 100);

            public double ScrollPageFraction
            {
                get => _scrollPageFraction;
                set
                {
                    var clamped = double.IsNaN(value) ? Constants.DefaultSmoothScrollPageFraction : Math.Clamp(value, 0.2, 2.0);
                    if (SetProperty(ref _scrollPageFraction, clamped))
                    {
                        UpdateScrollPreset();
                        PersistReaderProfile();
                    }
                }
            }

            public int ScrollDurationMs
            {
                get => _scrollDurationMs;
                set
                {
                    var clamped = Math.Clamp(value, 0, 2000);
                    if (SetProperty(ref _scrollDurationMs, clamped))
                    {
                        OnPropertyChanged(nameof(ScrollDuration));
                        UpdateScrollPreset();
                        PersistReaderProfile();
                    }
                }
            }

            public TimeSpan ScrollDuration => TimeSpan.FromMilliseconds(Math.Max(0, ScrollDurationMs));

            public ReaderScrollPreset ScrollPreset
            {
                get => _scrollPreset;
                set
                {
                    if (SetProperty(ref _scrollPreset, value) && !_suppressPresetPropagation)
                    {
                        ApplyScrollPreset(value);
                    }
                }
            }

            // Called by ReaderViewModel
            public void UpdateProfileKey(string? profileKey)
            {
                if (string.Equals(_profileKey, profileKey, StringComparison.Ordinal))
                    return;

                _profileKey = profileKey;
                LoadProfile();
            }

            public void LoadProfile()
            {
                var profile = _userSettings?.GetReaderProfile(_profileKey) ?? ReaderProfile.CreateDefault();

                _isApplyingProfile = true;
                try
                {
                    Theme = profile.Theme;
                    WidthFactor = profile.WidthFactor;
                    MaxPageWidth = profile.MaxPageWidth;
                    ScrollPageFraction = profile.ScrollPageFraction;
                    ScrollDurationMs = profile.ScrollDurationMs;
                }
                finally { _isApplyingProfile = false; }

                UpdateScrollPreset();
            }

            // Internals
            private void PersistReaderProfile()
            {
                if (_isApplyingProfile || _userSettings == null || string.IsNullOrWhiteSpace(_profileKey))
                    return;

                var profile = new ReaderProfile
                {
                    Theme = Theme,
                    WidthFactor = WidthFactor,
                    MaxPageWidth = MaxPageWidth,
                    ScrollPageFraction = ScrollPageFraction,
                    ScrollDurationMs = ScrollDurationMs
                };
                _userSettings.SetReaderProfile(_profileKey, profile);
            }

            private void ApplyScrollPreset(ReaderScrollPreset preset)
            {
                if (preset == ReaderScrollPreset.Custom)
                    return;

                (double fraction, int duration) = preset switch
                {
                    ReaderScrollPreset.Compact => (0.65, 180),
                    ReaderScrollPreset.Immersive => (1.1, 320),
                    _ => (Constants.DefaultSmoothScrollPageFraction, Constants.DefaultSmoothScrollDuration)
                };

                _isApplyingProfile = true;
                try
                {
                    ScrollPageFraction = fraction;
                    ScrollDurationMs = duration;
                }
                finally { _isApplyingProfile = false; }

                PersistReaderProfile();
            }

            private void UpdateScrollPreset()
            {
                var preset = DeterminePreset(_scrollPageFraction, _scrollDurationMs);
                _suppressPresetPropagation = true;
                try { ScrollPreset = preset; }
                finally { _suppressPresetPropagation = false; }
            }

            private static ReaderScrollPreset DeterminePreset(double fraction, int duration)
            {
                if (IsClose(fraction, 0.65) && duration == 180) return ReaderScrollPreset.Compact;
                if (IsClose(fraction, Constants.DefaultSmoothScrollPageFraction) && duration == Constants.DefaultSmoothScrollDuration) return ReaderScrollPreset.Balanced;
                if (IsClose(fraction, 1.1) && duration == 320) return ReaderScrollPreset.Immersive;
                return ReaderScrollPreset.Custom;
            }

            private static bool IsClose(double value, double target, double tolerance = 0.01)
                => Math.Abs(value - target) <= tolerance;

            private static double NormalizeWidthFactor(double value)
                => double.IsFinite(value) ? Math.Clamp(value, 0.3, 1.0) : Constants.DefaultWidthFactor;

            private void InitializeCommands()
            {
                IncreaseZoomCommand = new RelayCommand(_ => WidthFactor = Math.Min(1.0, WidthFactor + 0.05));
                DecreaseZoomCommand = new RelayCommand(_ => WidthFactor = Math.Max(0.3, WidthFactor - 0.05));
                ResetZoomCommand = new RelayCommand(_ =>
                {
                    WidthFactor = Constants.DefaultWidthFactor;
                    MaxPageWidth = Constants.DefaultMaxPageWidth;
                });
                SetZoomPresetCommand = new RelayCommand(param =>
                {
                    if (param is double direct)
                        WidthFactor = direct;
                    else if (param is string str && double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                        WidthFactor = parsed;
                });
                SetThemeCommand = new RelayCommand(param =>
                {
                    if (param is ReaderTheme theme)
                        Theme = theme;
                    else if (param is string str && Enum.TryParse(str, out ReaderTheme parsed))
                        Theme = parsed;
                });
                SetScrollPresetCommand = new RelayCommand(param =>
                {
                    if (param is ReaderScrollPreset preset)
                        ScrollPreset = preset;
                    else if (param is string str && Enum.TryParse(str, true, out ReaderScrollPreset parsed))
                        ScrollPreset = parsed;
                });

                OnPropertyChanged(nameof(IncreaseZoomCommand));
                OnPropertyChanged(nameof(DecreaseZoomCommand));
                OnPropertyChanged(nameof(ResetZoomCommand));
                OnPropertyChanged(nameof(SetZoomPresetCommand));
                OnPropertyChanged(nameof(SetThemeCommand));
                OnPropertyChanged(nameof(SetScrollPresetCommand));
            }

            private static SolidColorBrush CreateFrozenBrush(string hex)
            {
                var color = (Color)ColorConverter.ConvertFromString(hex)!;
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
        }
    }
}