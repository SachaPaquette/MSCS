using MaterialDesignThemes.Wpf;
using MSCS.Commands;
using MSCS.Enums;
using MSCS.Helpers;
using MSCS.Models;
using System;
using System.Globalization;
using System.Windows.Input;

namespace MSCS.ViewModels
{
    public partial class ReaderViewModel
    {
        private void InitializeReaderProfile()
        {
            var key = DetermineProfileKey();
            _profileKey = key;
            var profile = _userSettings?.GetReaderProfile(key) ?? ReaderProfile.CreateDefault();

            _isApplyingProfile = true;
            try
            {
                Theme = profile.Theme;
                WidthFactor = profile.WidthFactor;
                MaxPageWidth = profile.MaxPageWidth;
                ScrollPageFraction = profile.ScrollPageFraction;
                ScrollDurationMs = profile.ScrollDurationMs;
            }
            finally
            {
                _isApplyingProfile = false;
            }

            UpdateScrollPreset();
        }

        private string? DetermineProfileKey()
        {
            if (!string.IsNullOrWhiteSpace(_chapterListViewModel?.Manga?.Title))
            {
                return _chapterListViewModel.Manga!.Title;
            }

            return string.IsNullOrWhiteSpace(MangaTitle) ? null : MangaTitle;
        }

        private void PersistReaderProfile()
        {
            if (_userSettings == null)
            {
                return;
            }

            var key = DetermineProfileKey();
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            _profileKey = key;
            var profile = new ReaderProfile
            {
                Theme = Theme,
                WidthFactor = WidthFactor,
                MaxPageWidth = MaxPageWidth,
                ScrollPageFraction = ScrollPageFraction,
                ScrollDurationMs = ScrollDurationMs
            };

            _userSettings.SetReaderProfile(key, profile);
        }

        private void ApplyScrollPreset(ReaderScrollPreset preset)
        {
            if (preset == ReaderScrollPreset.Custom)
            {
                return;
            }

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
            finally
            {
                _isApplyingProfile = false;
            }

            PersistReaderProfile();
        }

        private void UpdateScrollPreset()
        {
            var preset = DeterminePreset(_scrollPageFraction, _scrollDurationMs);
            _suppressPresetPropagation = true;
            try
            {
                ScrollPreset = preset;
            }
            finally
            {
                _suppressPresetPropagation = false;
            }
        }

        private static ReaderScrollPreset DeterminePreset(double fraction, int duration)
        {
            if (IsClose(fraction, 0.65) && duration == 180)
            {
                return ReaderScrollPreset.Compact;
            }

            if (IsClose(fraction, Constants.DefaultSmoothScrollPageFraction) && duration == Constants.DefaultSmoothScrollDuration)
            {
                return ReaderScrollPreset.Balanced;
            }

            if (IsClose(fraction, 1.1) && duration == 320)
            {
                return ReaderScrollPreset.Immersive;
            }

            return ReaderScrollPreset.Custom;
        }

        private static bool IsClose(double value, double target, double tolerance = 0.01)
        {
            return Math.Abs(value - target) <= tolerance;
        }

        private void InitializePreferenceCommands()
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
                {
                    WidthFactor = direct;
                }
                else if (param is string str && double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    WidthFactor = parsed;
                }
            });

            SetThemeCommand = new RelayCommand(param =>
            {
                if (param is ReaderTheme theme)
                {
                    Theme = theme;
                }
                else if (param is string str && Enum.TryParse(str, out ReaderTheme parsed))
                {
                    Theme = parsed;
                }
            });

            SetScrollPresetCommand = new RelayCommand(param =>
            {
                if (param is ReaderScrollPreset preset)
                {
                    ScrollPreset = preset;
                }
                else if (param is string str && Enum.TryParse(str, true, out ReaderScrollPreset parsed))
                {
                    ScrollPreset = parsed;
                }
            });

            OnPropertyChanged(nameof(IncreaseZoomCommand));
            OnPropertyChanged(nameof(DecreaseZoomCommand));
            OnPropertyChanged(nameof(ResetZoomCommand));
            OnPropertyChanged(nameof(SetZoomPresetCommand));
            OnPropertyChanged(nameof(SetThemeCommand));
            OnPropertyChanged(nameof(SetScrollPresetCommand));
        }
    }
}