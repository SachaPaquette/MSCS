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
        public ReaderPreferencesViewModel Preferences => _preferences;

        private void InitializePreferences()
        {
            _preferences.SetProfileKey(DetermineProfileKey());
        }

        public void RefreshPreferencesProfileKey()
        {
            _preferences.SetProfileKey(DetermineProfileKey());
        }
    }
}